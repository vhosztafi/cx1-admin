import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { chromium } from 'playwright';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['127.0.0.1','localhost'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/servicing-business';await mkdir(output,{recursive:true});
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
  await page.setViewportSize({width:1560,height:1000});const before=await get(`/api/v1/policies/${fixture.policyId}`);
  await page.goto(origin+`/policies/${fixture.policyId}`);await field('Draft type').selectOption('cancellation');await field('Requested effective date').fill('2026-10-25');await field('Reason for draft').fill('Fictional trade activity browser scenario');await button('Create servicing draft').click();await page.waitForURL(/\/drafts\//);
  const draftId=page.url().split('/').at(-1),path=`/api/v1/drafts/${draftId}`;
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await button('Amend trade activities').click();
  await field('Annual turnover (GBP)').fill('123456.78');
  await other.goto(origin+`/drafts/${draftId}`);await other.getByLabel('Takeover or abandonment reason',{exact:true}).fill('Fictional business lease takeover');await other.getByRole('button',{name:'Take over editing',exact:true}).click();
  await page.getByText('Editing ownership changed. These form values are retained; reacquire the draft before applying them.',{exact:true}).waitFor();assert.equal(await button('Apply to draft').isDisabled(),true);
  await button('Keep form and return').click();assert.equal(await button('Resume trade activity form').evaluate(el=>el===document.activeElement),true);
  await other.getByRole('button',{name:'Release editing lease',exact:true}).click();await other.getByRole('heading',{name:'Read-only draft',exact:true}).waitFor();
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await button('Resume trade activity form').click();assert.equal(await field('Annual turnover (GBP)').inputValue(),'123456.78');
  await field('Vehicle sales (%)').fill('101');assert.equal(await button('Apply to draft').isDisabled(),true);await field('Vehicle sales (%)').fill('75');
  const number=(before.snapshot.risk.business.activities??[]).length+1;
  await button('Add occupation').click();await field(`Occupation ${number}`).selectOption({label:'Valeting - at Premises Only'});await field(`Occupation ${number} turnover share (%)`).fill('25');
  await button('Apply to draft').click();await page.waitForFunction(()=>document.activeElement?.textContent==='Amend trade activities');await save();
  let saved=await get(path);const initial=saved.proposal.changes.find(x=>x.kind==='business');assert.equal(initial.riskItemId,fixture.policyId);assert.equal(initial.payload.turnover,'123456.78');assert.equal(initial.payload.declaredActivitySplit.sales,7500);assert.equal(initial.payload.activities.at(-1).turnoverBasisPoints,2500);
  const activityId=initial.payload.activities.at(-1).id;assert.ok(activityId);assert.ok((await get(path+'/editor')).assessment.readinessIssues.length>0);
  await page.reload();await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await button('Amend trade activities').click();assert.equal(await field('Annual turnover (GBP)').inputValue(),'123456.78');
  await field('Annual turnover (GBP)').fill('');await field(`Occupation ${number} turnover share (%)`).fill('33.33');
  await page.screenshot({path:`${output}/${fixture.policyId}-desktop.png`});await page.setViewportSize({width:390,height:844});const bounds=await page.getByRole('dialog').boundingBox();assert.ok(bounds.x>=0&&bounds.x+bounds.width<=390);assert.equal(await page.getByRole('dialog').evaluate(el=>el.scrollWidth<=el.clientWidth),true);await page.screenshot({path:`${output}/${fixture.policyId}-mobile.png`});
  await button('Apply to draft').click();await save();saved=await get(path);assert.equal(saved.proposal.changes[0].changeId,initial.changeId);assert.equal(saved.proposal.changes[0].payload.turnover,undefined);assert.equal(saved.proposal.changes[0].payload.activities.at(-1).id,activityId);assert.equal(saved.proposal.changes[0].payload.activities.at(-1).turnoverBasisPoints,3333);assert.equal((await get(path+'/editor')).assessment.proposed.risk.business.turnover,undefined);
  await button('Amend trade activities').click();await button(`Remove occupation ${number}`).click();await button('Apply to draft').click();await save();assert.equal((await get(path)).proposal.changes[0].payload.activities.some(x=>x.id===activityId),false);
  await field('Takeover or abandonment reason').fill('Fictional business scenario complete');await field('I confirm this draft should be abandoned.').check();await button('Abandon draft').click();await page.getByText('This draft is closed. Its saved history remains available.',{exact:true}).waitFor();assert.deepEqual((await get(`/api/v1/policies/${fixture.policyId}`)).snapshot,before.snapshot);
  report.journeys.push({policyId:fixture.policyId,draftId,exactAmountsAndShares:true,invalidPercentageBlocked:true,stableOccupationEditRemove:true,explicitTurnoverClear:true,incompleteSave:true,leaseRetention:true,focusAndMobile:true,issuedUnchanged:true});
 }
 assert.deepEqual(errors,[]);await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}finally{await browser.close();}
