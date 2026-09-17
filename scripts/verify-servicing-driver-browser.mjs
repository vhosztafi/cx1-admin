import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { chromium } from 'playwright';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['127.0.0.1','localhost'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/servicing-driver';await mkdir(output,{recursive:true});
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
  await page.setViewportSize({width:1560,height:1000});const before=await get(`/api/v1/policies/${fixture.policyId}`),number=before.snapshot.risk.drivers.length+1;
  await page.goto(origin+`/policies/${fixture.policyId}`);await field('Draft type').selectOption('cancellation');await field('Requested effective date').fill('2026-10-25');await field('Reason for draft').fill('Fictional typed driver browser scenario');await button('Create servicing draft').click();await page.waitForURL(/\/drafts\//);
  const draftId=page.url().split('/').at(-1),path=`/api/v1/drafts/${draftId}`;
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await button('Add named driver').click();await page.getByRole('dialog').waitFor();
  assert.equal(await page.getByRole('dialog').getByText('Driver basis and restrictions',{exact:true}).count(),0);
  await field(`Driver ${number} · Full name`).fill('Fictional incomplete driver');
  await other.goto(origin+`/drafts/${draftId}`);await other.getByLabel('Takeover or abandonment reason',{exact:true}).fill('Fictional modal lease takeover test');await other.getByRole('button',{name:'Take over editing',exact:true}).click();
  await page.getByText('Editing ownership changed. These form values are retained; reacquire the draft before applying them.',{exact:true}).waitFor();assert.equal(await button('Apply to draft').isDisabled(),true);
  await button('Keep form and return').click();assert.equal(await button('Resume driver form').evaluate(el=>el===document.activeElement),true);
  await other.getByRole('button',{name:'Release editing lease',exact:true}).click();await other.getByRole('heading',{name:'Read-only draft',exact:true}).waitFor();
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await button('Resume driver form').click();assert.equal(await field(`Driver ${number} · Full name`).inputValue(),'Fictional incomplete driver');
  const histories=[['occupations','Additional occupations','Additional Occupation','reference'],['convictions','Motoring convictions','Fine Amount','money'],['losses','Accidents and claims','Total Cost','money'],['criminalConvictions','Criminal convictions','Sentence Length (months)','count'],['countyCourtJudgments','County court judgments','Amount','money']];
  for(const [key,label,edit,kind] of histories){
    await button(`Add ${label.toLowerCase()} for driver ${number}`).click();const input=field(`Driver ${number} · ${label} 1 · ${edit}`);
    if(kind==='reference')await input.selectOption('0');else await input.fill('0');
    if(key==='occupations')await field(`Driver ${number} · Additional occupations 1 · Business Use Required`).selectOption('false');
  }
  await button('Apply to draft').click();await page.getByRole('dialog').waitFor({state:'hidden'});await page.waitForFunction(()=>document.activeElement?.textContent==='Add named driver');await save();
  let saved=await get(path);const addition=saved.proposal.changes.find(x=>x.kind==='driver'&&x.operation==='add');assert.ok(addition);assert.equal(addition.payload.fullName,'Fictional incomplete driver');
  for(const [key] of histories)assert.ok(addition.payload[key][0].id);
  const assessed=await get(path+'/editor');assert.ok(assessed.assessment.readinessIssues.some(x=>x.path===`/risk/drivers/${number-1}/dateOfBirth`));
  await page.reload();await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await field('Named driver to change').selectOption(addition.riskItemId);
  await button('Edit named driver').click();assert.equal(await field(`Driver ${number} · Full name`).inputValue(),'Fictional incomplete driver');await field(`Driver ${number} · Date of Birth`).fill('1990-01-01');
  await page.screenshot({path:`${output}/${fixture.policyId}-desktop.png`});await page.setViewportSize({width:390,height:844});
  const bounds=await page.getByRole('dialog').boundingBox();assert.ok(bounds.x>=0&&bounds.x+bounds.width<=390);assert.equal(await page.getByRole('dialog').evaluate(el=>el.scrollWidth<=el.clientWidth),true);
  await page.screenshot({path:`${output}/${fixture.policyId}-mobile.png`});await button('Apply to draft').click();await save();
  saved=await get(path);assert.equal(saved.proposal.changes[0].changeId,addition.changeId);assert.equal(saved.proposal.changes[0].riskItemId,addition.riskItemId);assert.equal(saved.proposal.changes[0].payload.dateOfBirth,'1990-01-01');
  for(const [key] of histories)assert.deepEqual(saved.proposal.changes[0].payload[key],addition.payload[key]);
  await button('Remove named driver').click();await button('Apply to draft').click();await save();assert.equal((await get(path)).proposal.changes.length,0);
  // A replacement of an issued driver clears the field rather than inheriting
  // the original date. This exercises the actual typed editor and persistence.
  const original=before.snapshot.risk.drivers[0];await field('Named driver to change').selectOption(original.id);await button('Edit named driver').click();await field('Driver 1 · Date of Birth').fill('');await button('Apply to draft').click();await save();
  saved=await get(path);assert.equal(saved.proposal.changes[0].payloadMode,'replace');assert.equal(saved.proposal.changes[0].payload.dateOfBirth,undefined);
  const cleared=await get(path+'/editor');assert.equal(cleared.assessment.proposed.risk.drivers[0].dateOfBirth,undefined);assert.equal(cleared.assessment.base.risk.drivers[0].dateOfBirth,original.dateOfBirth);
  await button('Remove named driver').click();await button('Apply to draft').click();await save();
  const removed=await get(path);assert.equal(removed.proposal.changes[0].operation,'remove');assert.equal(removed.proposal.changes[0].riskItemId,original.id);
  assert.equal((await get(path+'/editor')).assessment.proposed.risk.drivers.some(driver=>driver.id===original.id),false);
  await field('Takeover or abandonment reason').fill('Fictional driver scenario completed');await page.getByLabel('I confirm this draft should be abandoned.',{exact:true}).check();await button('Abandon draft').click();await page.getByText('This draft is closed. Its saved history remains available.',{exact:true}).waitFor();
  assert.deepEqual((await get(`/api/v1/policies/${fixture.policyId}`)).snapshot,before.snapshot);
  report.journeys.push({policyId:fixture.policyId,draftId,typedIncompleteSave:true,stableReloadAndEdit:true,cancelAddition:true,explicitIssuedFieldClear:true,issuedDriverRemovalProposed:true,modalRetainedAcrossTakeover:true,focusRestored:true,mobileDialogContained:true,issuedUnchanged:true});
 }
 assert.deepEqual(errors,[]);await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}finally{await browser.close();}
