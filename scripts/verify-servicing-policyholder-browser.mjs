import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { chromium } from 'playwright';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['127.0.0.1','localhost'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/servicing-policyholder';await mkdir(output,{recursive:true});
const fixtures=JSON.parse(await readFile(process.env.COVER_POLICY_FIXTURES??'.local/browser-evidence/underwriting-issue/report.json','utf8')).journeys;
const password=(await readFile('.local/demo-password.txt','utf8')).trim(),browser=await chromium.launch({channel:'chrome',headless:true});
const page=await browser.newPage({viewport:{width:1560,height:1000}});page.setDefaultTimeout(30000);const errors=[];page.on('pageerror',error=>errors.push(error.message));const report={journeys:[]};
const other=await browser.newPage({viewport:{width:1560,height:1000}});other.setDefaultTimeout(30000);other.on('pageerror',error=>errors.push(error.message));
const button=name=>page.getByRole('button',{name,exact:true}),field=name=>page.getByLabel(name,{exact:true});
async function get(path){const r=await page.request.get(origin+path);assert.equal(r.status(),200,await r.text());return r.json();}
async function save(){const pending=page.waitForResponse(r=>new URL(r.url()).pathname===new URL(page.url()).pathname.replace('/drafts/','/api/v1/drafts/')+'/proposal'&&r.request().method()==='PUT');await button('Save draft').click();const response=await pending;assert.equal(response.status(),200,await response.text());await page.getByText('Draft action saved.',{exact:true}).waitFor();}
try{
 await page.goto(origin+'/login');await field('Email address').fill('servicing@cover.example');await field('Password').fill(password);await button('Sign in').click();await page.waitForURL(origin+'/');
 await other.goto(origin+'/login');await other.getByLabel('Email address').fill('underwriter@cover.example');await other.getByLabel('Password',{exact:true}).fill(password);await other.getByRole('button',{name:'Sign in',exact:true}).click();await other.waitForURL(origin+'/');
 for(const fixture of fixtures){
  await page.setViewportSize({width:1560,height:1000});const before=await get(`/api/v1/policies/${fixture.policyId}`);
  await page.goto(origin+`/policies/${fixture.policyId}`);await field('Draft type').selectOption('adjustment');await field('Requested effective date').fill('2026-10-25');await field('Reason for draft').fill('Fictional policyholder correction browser scenario');await button('Create servicing draft').click();await page.waitForURL(/\/drafts\//);
  const draftId=page.url().split('/').at(-1),path=`/api/v1/drafts/${draftId}`;
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await button('Correct policyholder details').click();
  await field('Trading name').fill('Fictional corrected trading name');
  await other.goto(origin+`/drafts/${draftId}`);await other.getByLabel('Takeover or abandonment reason',{exact:true}).fill('Fictional policyholder lease takeover');await other.getByRole('button',{name:'Take over editing',exact:true}).click();
  await page.getByText('Editing ownership changed. These form values are retained; reacquire the draft before applying them.',{exact:true}).waitFor();assert.equal(await button('Apply to draft').isDisabled(),true);
  await button('Keep form and return').click();assert.equal(await button('Resume policyholder form').evaluate(el=>el===document.activeElement),true);
  await other.getByRole('button',{name:'Release editing lease',exact:true}).click();await other.getByRole('heading',{name:'Read-only draft',exact:true}).waitFor();
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await button('Resume policyholder form').click();assert.equal(await field('Trading name').inputValue(),'Fictional corrected trading name');
  await field('Proposer 1 full name').fill('');await field('Proposer 3 full name').fill('Fictional Additional Proposer');assert.equal(await button('Apply to draft').isDisabled(),true);await button('Remove empty proposer slots').click();
  await field('Postcode').fill('SW1A 1AA');await button('Apply to draft').click();await page.waitForFunction(()=>document.activeElement?.textContent==='Correct policyholder details');await save();
  let saved=await get(path);const initial=saved.proposal.changes.find(x=>x.kind==='policyholder');const assessed=await get(path+'/editor');assert.equal(initial.riskItemId,assessed.clientId);assert.equal(initial.payload.tradingName,'Fictional corrected trading name');assert.equal(initial.payload.address.postcode,'SW1A 1AA');assert.equal(initial.payload.clientId,undefined);assert.equal(initial.payload.clientAgencyRelationshipId,undefined);
  await page.reload();await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await button('Correct policyholder details').click();assert.equal(await field('Trading name').inputValue(),'Fictional corrected trading name');await field('Trading name').fill('');await field('Postcode').fill('');
  await page.screenshot({path:`${output}/${fixture.policyId}-desktop.png`});await page.setViewportSize({width:390,height:844});const bounds=await page.getByRole('dialog').boundingBox();assert.ok(bounds.x>=0&&bounds.x+bounds.width<=390);assert.equal(await page.getByRole('dialog').evaluate(el=>el.scrollWidth<=el.clientWidth),true);await page.screenshot({path:`${output}/${fixture.policyId}-mobile.png`});
  await button('Apply to draft').click();await save();saved=await get(path);assert.equal(saved.proposal.changes[0].changeId,initial.changeId);assert.equal(saved.proposal.changes[0].payload.tradingName,undefined);assert.equal(saved.proposal.changes[0].payload.address.postcode,undefined);const cleared=(await get(path+'/editor')).assessment.proposed.insured;assert.equal(cleared.tradingName,undefined);assert.equal(cleared.address.postcode,undefined);
  await field('Takeover or abandonment reason').fill('Fictional policyholder scenario complete');await field('I confirm this draft should be abandoned.').check();await button('Abandon draft').click();await page.getByText('This draft is closed. Its saved history remains available.',{exact:true}).waitFor();assert.deepEqual((await get(`/api/v1/policies/${fixture.policyId}`)).snapshot,before.snapshot);
  report.journeys.push({policyId:fixture.policyId,draftId,policyholderIdentityScoped:true,nameGapBlocked:true,stableEditReload:true,explicitFieldClear:true,leaseRetention:true,focusAndMobile:true,issuedUnchanged:true});
 }
 assert.deepEqual(errors,[]);await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}finally{await browser.close();}
