import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { chromium } from 'playwright';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
assert.ok(['127.0.0.1','localhost'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/servicing-date-review'; await mkdir(output,{recursive:true});
const fixtures=JSON.parse(await readFile('.local/browser-evidence/underwriting-issue/report.json','utf8')).journeys;
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const browser=await chromium.launch({channel:'chrome',headless:true});
const page=await browser.newPage({viewport:{width:1560,height:1100}}); page.setDefaultTimeout(30000);
const errors=[];page.on('pageerror',error=>errors.push(error.message));const report={journeys:[]};
async function get(path){const response=await page.request.get(origin+path);assert.equal(response.status(),200,await response.text());return {data:await response.json(),etag:response.headers().etag};}
try {
 await page.goto(origin+'/login');await page.getByLabel('Email address').fill('servicing@cover.example');await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 for(const fixture of fixtures){
  await page.setViewportSize({width:1560,height:1100});
  const before=(await get(`/api/v1/policies/${fixture.policyId}`)).data;
  await page.goto(origin+`/policies/${fixture.policyId}`);
  await page.getByLabel('Draft type',{exact:true}).selectOption('cancellation');
  await page.getByLabel('Requested effective date',{exact:true}).fill('2026-10-25');
  await page.getByLabel('Reason for draft',{exact:true}).fill('Fictional date and comparison browser scenario');
  await page.getByRole('button',{name:'Create servicing draft',exact:true}).click();await page.waitForURL(/\/drafts\//);
  const draftId=page.url().split('/').at(-1),path=`/api/v1/drafts/${draftId}`;
  await page.getByRole('button',{name:'Acquire editing lease',exact:true}).click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await page.getByLabel('Requested by',{exact:true}).selectOption('insured');await page.getByLabel('Requester name',{exact:true}).fill('Fictional insured requester');
  await page.getByLabel('Effective date (London)',{exact:true}).fill('2027-03-28');await page.getByLabel('Effective time (London)',{exact:true}).fill('01:30');
  await page.getByText('Enter a valid London date and time. The clocks skip some times in spring.',{exact:true}).waitFor();
  await page.getByLabel('Effective date (London)',{exact:true}).fill('2026-10-25');await page.getByText('This time occurs twice. Choose GMT or British Summer Time.',{exact:true}).waitFor();
  await page.getByLabel('Effective clock offset',{exact:true}).selectOption('60');
  await page.getByRole('button',{name:'Save draft',exact:true}).click();await page.getByText('Draft action saved.',{exact:true}).waitFor();
  const saved=await get(path);assert.equal(saved.data.proposal.commonEffectiveIntent.utcOffsetMinutes,60);assert.deepEqual(saved.data.proposal.requestedBy,{kind:'insured',name:'Fictional insured requester'});
  // The typed picker is a separate unfinished task. Prepare a saved change via
  // the real API here solely to verify the comparison and removal UI.
  const driver=before.snapshot.risk.drivers[0];const proposal=structuredClone(saved.data.proposal);
  proposal.changes=[{changeId:crypto.randomUUID(),riskItemId:driver.id,kind:'driver',operation:'update',payload:{fullName:'Fictional comparison driver'}}];
  const csrf=(await get('/api/v1/auth/csrf')).data.requestToken;
  const prepared=await page.request.put(origin+path+'/proposal',{headers:{'X-CSRF-Token':csrf,'If-Match':saved.etag,'Idempotency-Key':crypto.randomUUID(),'X-Edit-Lease':saved.data.lease.leaseToken},data:proposal});assert.equal(prepared.status(),200,await prepared.text());
  await page.getByRole('button',{name:'Remove proposed change 1',exact:true}).waitFor();
  await page.locator('.servicing-difference summary').first().click();
  await page.getByText('Fictional comparison driver',{exact:true}).waitFor();
  await page.getByText('The full name must agree with the first name and surname.',{exact:true}).waitFor();
  await page.getByLabel('Reason for change',{exact:true}).fill('Unsaved reason survives saved comparison polling');
  await page.getByText('This review describes the saved proposal. Save your local changes to update it.',{exact:true}).waitFor();
  await page.waitForTimeout(5500);assert.equal(await page.getByLabel('Reason for change',{exact:true}).inputValue(),'Unsaved reason survives saved comparison polling');
  await page.evaluate(()=>window.scrollTo(0,0));await page.screenshot({path:`${output}/${fixture.product??fixture.policyId}-desktop.png`,fullPage:true});
  await page.setViewportSize({width:390,height:844});
  assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
  await page.evaluate(()=>window.scrollTo(0,0));await page.screenshot({path:`${output}/${fixture.product??fixture.policyId}-mobile.png`,fullPage:true});
  await page.getByRole('button',{name:'Remove proposed change 1',exact:true}).click();
  await page.getByRole('button',{name:'Save draft',exact:true}).click();await page.getByText('Draft action saved.',{exact:true}).waitFor();
  assert.equal((await get(path)).data.proposal.changes.length,0);
  await page.getByText('No material risk differences in the saved proposal.',{exact:true}).waitFor();
  await page.getByLabel('Takeover or abandonment reason',{exact:true}).fill('Fictional date review scenario completed');
  await page.getByLabel('I confirm this draft should be abandoned.',{exact:true}).check();await page.getByRole('button',{name:'Abandon draft',exact:true}).click();
  await page.getByText('This draft is closed. Its saved history remains available.',{exact:true}).waitFor();
  assert.deepEqual((await get(`/api/v1/policies/${fixture.policyId}`)).data.snapshot,before.snapshot);
  report.journeys.push({policyId:fixture.policyId,draftId,dateAndRequesterPersisted:true,savedComparison:true,localEditsRetained:true,removeProposalPersisted:true,mobileOverflow:false,issuedUnchanged:true});
 }
 assert.deepEqual(errors,[]);await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}finally{await browser.close();}
