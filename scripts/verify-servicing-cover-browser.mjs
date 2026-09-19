import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { chromium } from 'playwright';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['127.0.0.1','localhost'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/servicing-cover';await mkdir(output,{recursive:true});
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
  await page.goto(origin+`/policies/${fixture.policyId}`);await field('Draft type').selectOption('adjustment');await field('Requested effective date').fill('2026-10-25');await field('Reason for draft').fill('Fictional dated cover editor browser scenario');await button('Create servicing draft').click();await page.waitForURL(/\/drafts\//);
  const draftId=page.url().split('/').at(-1),path=`/api/v1/drafts/${draftId}`;
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await field('Cover to change').selectOption('tools-equipment');await button('Amend cover').click();
  await field('Tools and equipment choice').selectOption('true');await field('Tools and equipment limit (£)').fill('5000.25');await field('Tools and equipment excess (£)').fill('250');
  await other.goto(origin+`/drafts/${draftId}`);await other.getByLabel('Takeover or abandonment reason',{exact:true}).fill('Fictional cover lease takeover');await other.getByRole('button',{name:'Take over editing',exact:true}).click();
  await page.getByText('Editing ownership changed. These form values are retained; reacquire the draft before applying them.',{exact:true}).waitFor();assert.equal(await button('Apply to draft').isDisabled(),true);
  await button('Keep form and return').click();assert.equal(await button('Resume cover form').evaluate(el=>el===document.activeElement),true);await other.getByRole('button',{name:'Release editing lease',exact:true}).click();await other.getByRole('heading',{name:'Read-only draft',exact:true}).waitFor();
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await button('Resume cover form').click();assert.equal(await field('Tools and equipment limit (£)').inputValue(),'5000.25');await button('Apply to draft').click();await page.waitForFunction(()=>document.activeElement?.textContent==='Amend cover');
  await field('Effective date basis').selectOption('per-cover-change');await field('Use an individual cover date').check();await field('Cover change 1 date (London)').fill('2026-10-27');await save();
  let saved=await get(path);const initial=saved.proposal.changes[0];assert.equal(initial.effectiveIntent.localDate,'2026-10-27');assert.equal(initial.payload.requestedSections[0].limit,'5000.25');
  await page.reload();await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await field('Cover to change').selectOption('tools-equipment');await button('Amend cover').click();assert.equal(await field('Tools and equipment limit (£)').inputValue(),'5000.25');await field('Tools and equipment limit (£)').fill('6000.75');
  await page.screenshot({path:`${output}/${fixture.policyId}-desktop.png`});await page.setViewportSize({width:390,height:844});const bounds=await page.getByRole('dialog').boundingBox();assert.ok(bounds.x>=0&&bounds.x+bounds.width<=390);assert.equal(await page.getByRole('dialog').evaluate(el=>el.scrollWidth<=el.clientWidth),true);await page.screenshot({path:`${output}/${fixture.policyId}-mobile.png`});await button('Apply to draft').click();await save();saved=await get(path);assert.equal(saved.proposal.changes[0].changeId,initial.changeId);assert.deepEqual(saved.proposal.changes[0].effectiveIntent,initial.effectiveIntent);
  await field('Cover to change').selectOption('all');await button('Amend cover').click();const originalTools=before.snapshot.cover.requestedSections.find(x=>x.code==='tools-equipment');assert.equal(await field('Tools and equipment choice').inputValue(),originalTools?.selected===true?'true':originalTools?.selected===false?'false':'');await field('Tools and equipment choice').selectOption('false');await button('Apply to draft').click();await save();
  const assessment=(await get(path+'/editor')).assessment;assert.equal(assessment.slices.length,2);assert.equal(assessment.slices[0].proposed.cover.requestedSections.find(x=>x.code==='tools-equipment').selected,false);assert.equal(assessment.slices[1].proposed.cover.requestedSections.find(x=>x.code==='tools-equipment').limit,'6000.75');
  let dependency=false;
  if(before.snapshot.productCode==='motor-trade-combined'&&before.snapshot.risk.premises?.length){
   await field('Cover to change').selectOption('premises');await button('Amend cover').click();await field('Premises choice').selectOption('false');await button('Apply to draft').click();await save();
   await field('Premises to change').selectOption(before.snapshot.risk.premises[0].id);await button('Remove premises').click();await button('Apply to draft').click();await save();assert.equal((await get(path+'/editor')).assessment.proposed.risk.premises.some(x=>x.id===before.snapshot.risk.premises[0].id),false);dependency=true;
  }
  await field('Takeover or abandonment reason').fill('Fictional cover scenario complete');await field('I confirm this draft should be abandoned.').check();await button('Abandon draft').click();await page.getByText('This draft is closed. Its saved history remains available.',{exact:true}).waitFor();assert.deepEqual((await get(`/api/v1/policies/${fixture.policyId}`)).snapshot,before.snapshot);
  report.journeys.push({policyId:fixture.policyId,draftId,sectionLimitsPersisted:true,laterDateRetainedOnEdit:true,noFutureValuesInEarlierReplacement:true,cumulativeSlicesVerified:true,premisesDependencyCorrected:dependency,leaseRetention:true,focusAndMobile:true,issuedUnchanged:true});
 }
 assert.deepEqual(errors,[]);await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}finally{await browser.close();}
