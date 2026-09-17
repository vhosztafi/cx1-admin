import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { chromium } from 'playwright';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['127.0.0.1','localhost'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/servicing-editor-audit';await mkdir(output,{recursive:true});
const fixtures=JSON.parse(await readFile('.local/browser-evidence/underwriting-issue/report.json','utf8')).journeys;
const password=(await readFile('.local/demo-password.txt','utf8')).trim(),browser=await chromium.launch({channel:'chrome',headless:true});
const page=await browser.newPage({viewport:{width:1560,height:1000}});page.setDefaultTimeout(30000);const errors=[];page.on('pageerror',error=>errors.push(error.message));const report={journeys:[]};
const other=await browser.newPage({viewport:{width:1560,height:1000}});other.setDefaultTimeout(30000);other.on('pageerror',error=>errors.push(error.message));
const button=name=>page.getByRole('button',{name,exact:true}),field=name=>page.getByLabel(name,{exact:true});
async function get(path){const r=await page.request.get(origin+path);assert.equal(r.status(),200,await r.text());return r.json();}
async function save(){await button('Save draft').click();await page.getByText('Draft action saved.',{exact:true}).waitFor();}
try{
 await page.goto(origin+'/login');await field('Email address').fill('servicing@cover.example');await field('Password').fill(password);await button('Sign in').click();await page.waitForURL(origin+'/');
 await other.goto(origin+'/login');await other.getByLabel('Email address').fill('underwriter@cover.example');await other.getByLabel('Password',{exact:true}).fill(password);await other.getByRole('button',{name:'Sign in',exact:true}).click();await other.waitForURL(origin+'/');
 for(const fixture of fixtures){
  const before=await get(`/api/v1/policies/${fixture.policyId}`);await page.goto(origin+`/policies/${fixture.policyId}`);
  await field('Draft type').selectOption('cancellation');await field('Requested effective date').fill('2026-10-25');await field('Reason for draft').fill('Fictional final servicing editor audit');await button('Create servicing draft').click();await page.waitForURL(/\/drafts\//);
  const draftId=page.url().split('/').at(-1),path=`/api/v1/drafts/${draftId}`;
  await page.getByRole('link',{name:'Back to policy',exact:true}).click();await page.waitForURL(origin+`/policies/${fixture.policyId}`);await page.locator(`a[href="/drafts/${draftId}"]`).click();await page.waitForURL(origin+`/drafts/${draftId}`);
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await button('Add named driver').click();const n=before.snapshot.risk.drivers.length+1;
  await field(`Driver ${n} \u00b7 Full name`).fill('Taylor Fictional');await field(`Driver ${n} \u00b7 Date of Birth`).fill('1990-01-01');await field(`Driver ${n} \u00b7 Type of Licence`).selectOption('0');await field(`Driver ${n} \u00b7 Driving licence number`).fill('FICTIONAL-DRIVER');await field(`Driver ${n} \u00b7 Driving test date`).fill('2008-01-01');
  await button(`Add accidents and claims for driver ${n}`).click();await field(`Driver ${n} \u00b7 Accidents and claims 1 \u00b7 Total Cost`).fill('8400');await field(`Driver ${n} \u00b7 Accidents and claims 1 \u00b7 Fault involvement`).selectOption({label:'fault'});
  await button('Apply to draft').click();await save();let saved=await get(path);const addition=saved.proposal.changes[0];assert.equal(addition.payload.licence.number,'FICTIONAL-DRIVER');assert.equal(addition.payload.licence.testDate,'2008-01-01');assert.ok(addition.payload.licence.type.collection);assert.equal(addition.payload.losses[0].fault,'fault');
  const response=await page.request.get(origin+path),etag=response.headers().etag,csrf=(await get('/api/v1/auth/csrf')).requestToken;
  const illegal=structuredClone(saved.proposal);illegal.changes[0].effectiveIntent={...illegal.commonEffectiveIntent,localDate:'2026-10-27'};
  const denied=await page.request.put(origin+path+'/proposal',{headers:{'X-CSRF-Token':csrf,'If-Match':etag,'Idempotency-Key':crypto.randomUUID(),'X-Edit-Lease':saved.lease.leaseToken},data:illegal});assert.equal(denied.status(),422,await denied.text());assert.equal((await get(path)).revisionId,saved.revisionId);
  await button('Remove proposed change 1').click();await save();assert.equal((await get(path)).proposal.changes.length,0);
  if(before.snapshot.productCode==='motor-trade-combined'){
   await field('Cover to change').selectOption('stock-custody');await button('Amend cover').click();await field('Stock and custody choice').selectOption('true');await field('Stock and custody limit (£)').fill('80000');await field('Stock and custody excess (£)').fill('500');await field('Stock and custody any one vehicle limit (£)').fill('20000');await button('Apply to draft').click();await save();const stock=(await get(path)).proposal.changes[0];assert.equal(stock.payload.requestedSections[0].limit,'80000.00');
   await button('Amend cover').click();await field('Stock and custody limit (£)').fill('85000');await button('Apply to draft').click();await save();assert.equal((await get(path)).proposal.changes[0].changeId,stock.changeId);await button('Remove proposed change 1').click();await save();assert.equal((await get(path)).proposal.changes.length,0);
  }
  await field('Takeover or abandonment reason').fill('Fictional final editor audit complete');await field('I confirm this draft should be abandoned.').check();await button('Abandon draft').click();await page.getByText('This draft is closed. Its saved history remains available.',{exact:true}).waitFor();assert.deepEqual((await get(`/api/v1/policies/${fixture.policyId}`)).snapshot,before.snapshot);
  report.journeys.push({policyId:fixture.policyId,draftId,policyAndDraftNavigation:true,sourceDriverFieldsPersisted:true,claimDetailsPersisted:true,nonCoverDateRejected:true,proposalRemoval:true,stockLimitEditRemove:before.snapshot.productCode==='motor-trade-combined',issuedUnchanged:true});
 }
 assert.deepEqual(errors,[]);await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}finally{await browser.close();}
