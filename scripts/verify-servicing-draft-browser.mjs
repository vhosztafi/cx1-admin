import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {chromium} from 'playwright';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
assert.ok(['127.0.0.1','localhost'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/servicing-draft';await mkdir(output,{recursive:true});
const fixtures=JSON.parse(await readFile('.local/browser-evidence/underwriting-issue/report.json','utf8')).journeys;
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const browser=await chromium.launch({channel:'chrome',headless:true});
const a=await browser.newContext({viewport:{width:1560,height:1000}}),b=await browser.newContext({viewport:{width:1560,height:1000}});
const first=await a.newPage(),second=await b.newPage();const errors=[];
for(const page of [first,second]){page.setDefaultTimeout(30000);page.on('pageerror',error=>errors.push(error.message));}
async function login(page,email){await page.goto(origin+'/login');await page.getByLabel('Email address').fill(email);await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');}
async function get(page,path){const r=await page.request.get(origin+path);assert.equal(r.status(),200,await r.text());assert.match(r.headers()['cache-control']??'',/no-store/);return r.json();}
const report={journeys:[]};
try{
 await login(first,'servicing@cover.example');await login(second,'underwriter@cover.example');
 for(const fixture of fixtures){
  const before=await get(first,`/api/v1/policies/${fixture.policyId}`);
  await first.goto(origin+`/policies/${fixture.policyId}`);await first.getByRole('heading',{name:'Servicing drafts',exact:true}).waitFor();
  // Cancellation drafts may coexist, making repeat runs additive and preserving
  // every prior draft. This test never resets or deletes a business record.
  await first.getByLabel('Draft type',{exact:true}).selectOption('cancellation');
  await first.getByLabel('Requested effective date',{exact:true}).fill(before.snapshot.term.startsAt.slice(0,10));
  await first.getByLabel('Reason for draft',{exact:true}).fill('Fictional browser draft creation');
  await first.getByRole('button',{name:'Create servicing draft',exact:true}).click();await first.waitForURL(/\/drafts\/[a-f0-9-]+$/);
  const draftId=first.url().split('/').at(-1),path=`/api/v1/drafts/${draftId}`;
  await first.getByRole('button',{name:'Acquire editing lease',exact:true}).click();await first.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await first.getByLabel('Reason for change',{exact:true}).fill('Fictional saved browser proposal');
  await first.getByRole('button',{name:'Save draft',exact:true}).click();await first.getByText('Draft action saved.',{exact:true}).waitFor();
  assert.equal((await get(first,path)).proposal.reason,'Fictional saved browser proposal');
  await first.reload();await first.getByLabel('Reason for change',{exact:true}).waitFor();assert.equal(await first.getByLabel('Reason for change',{exact:true}).inputValue(),'Fictional saved browser proposal');
  await first.getByRole('button',{name:'Acquire editing lease',exact:true}).click();await first.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await first.getByLabel('Reason for change',{exact:true}).fill('Unsaved first editor text must survive takeover');
  const old=await get(first,path);await second.goto(origin+`/drafts/${draftId}`);
  await second.getByLabel('Takeover or abandonment reason',{exact:true}).fill('Fictional senior review needs this draft');
  await second.getByRole('button',{name:'Take over editing',exact:true}).click();await second.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await first.getByRole('heading',{name:'Read-only draft',exact:true}).waitFor();
  assert.equal(await first.getByLabel('Reason for change',{exact:true}).inputValue(),'Unsaved first editor text must survive takeover');assert.equal(await first.getByRole('button',{name:'Save draft',exact:true}).isDisabled(),true);
  const taken=await get(second,path);assert.notEqual(taken.lease.leaseToken,old.lease.leaseToken);assert.equal(taken.lease.generation,old.lease.generation+1);
  const readResponse=await first.request.get(origin+path),etag=readResponse.headers().etag;
  const csrf=(await get(first,'/api/v1/auth/csrf')).requestToken;
  const headers={'If-Match':etag,'Idempotency-Key':crypto.randomUUID(),'X-Edit-Lease':old.lease.leaseToken};
  assert.equal((await first.request.put(origin+path+'/proposal',{headers,data:old.proposal})).status(),403);
  assert.equal((await first.request.put(origin+path+'/proposal',{headers:{...headers,'X-CSRF-Token':'invalid'},data:old.proposal})).status(),403);
  assert.equal((await first.request.put(origin+path+'/proposal',{headers:{...headers,'X-CSRF-Token':csrf},data:old.proposal})).status(),409);
  await second.getByRole('button',{name:'Renew editing lease',exact:true}).click();await second.getByText('Draft action saved.',{exact:true}).waitFor();
  await second.getByRole('button',{name:'Release editing lease',exact:true}).click();await second.getByRole('heading',{name:'Read-only draft',exact:true}).waitFor();
  await first.getByRole('button',{name:'Acquire editing lease',exact:true}).click();await first.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  assert.equal(await first.getByLabel('Reason for change',{exact:true}).inputValue(),'Unsaved first editor text must survive takeover');
  await first.getByRole('button',{name:'Save draft',exact:true}).click();await first.getByText('Draft action saved.',{exact:true}).waitFor();
  assert.equal((await get(first,path)).proposal.reason,'Unsaved first editor text must survive takeover');
  await first.getByLabel('Takeover or abandonment reason',{exact:true}).fill('Fictional browser journey is complete');
  await first.getByLabel('I confirm this draft should be abandoned.').check();await first.getByRole('button',{name:'Abandon draft',exact:true}).click();await first.getByText('Abandoned',{exact:true}).waitFor();
  assert.equal((await get(first,path)).state,'abandoned');
  await first.getByText('This draft is closed. Its saved history remains available.',{exact:true}).waitFor();
  const after=await get(first,`/api/v1/policies/${fixture.policyId}`);assert.equal(after.contentHash,before.contentHash);assert.deepEqual(after.snapshot,before.snapshot);
  report.journeys.push({productCode:fixture.productCode,policyId:fixture.policyId,draftId,checks:['persisted creation and saved readback','reload resumes proposal','two-user takeover','retained unsaved input and read-only loser','stale holder rejected','missing/invalid CSRF denied','renew and release','reacquire and save retained edits','explicit abandonment','issued snapshot unchanged']});
 }
 await first.screenshot({path:output+'/desktop.png',fullPage:true});await first.setViewportSize({width:390,height:844});
 assert.equal(await first.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await first.screenshot({path:output+'/mobile.png',fullPage:true});assert.deepEqual(errors,[]);
 await writeFile(output+'/report.json',JSON.stringify(report,null,2)+'\n');console.log(`${report.journeys.length} persistent two-user servicing draft journeys passed.`);
}catch(error){await first.screenshot({path:output+'/failure-first.png',fullPage:true}).catch(()=>{});await second.screenshot({path:output+'/failure-second.png',fullPage:true}).catch(()=>{});throw error;}
finally{await a.close();await b.close();await browser.close();}
