import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { chromium } from 'playwright';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['127.0.0.1','localhost'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/servicing-premises';await mkdir(output,{recursive:true});
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
  await page.setViewportSize({width:1560,height:1000});const before=await get(`/api/v1/policies/${fixture.policyId}`),number=(before.snapshot.risk.premises??[]).length+1;
  await page.goto(origin+`/policies/${fixture.policyId}`);await field('Draft type').selectOption('cancellation');await field('Requested effective date').fill('2026-10-25');await field('Reason for draft').fill('Fictional typed premises browser scenario');await button('Create servicing draft').click();await page.waitForURL(/\/drafts\//);
  const draftId=page.url().split('/').at(-1),path=`/api/v1/drafts/${draftId}`;
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await button('Add premises').click();await page.getByRole('dialog').waitFor();
  assert.equal(await page.getByRole('dialog').getByText('Driver basis and restrictions',{exact:true}).count(),0);
  await field(`Premises ${number} · Postcode`).fill('SW1A 1AA');
  await other.goto(origin+`/drafts/${draftId}`);await other.getByLabel('Takeover or abandonment reason',{exact:true}).fill('Fictional modal lease takeover test');await other.getByRole('button',{name:'Take over editing',exact:true}).click();
  await page.getByText('Editing ownership changed. These form values are retained; reacquire the draft before applying them.',{exact:true}).waitFor();assert.equal(await button('Apply to draft').isDisabled(),true);
  await button('Keep form and return').click();assert.equal(await button('Resume premises form').evaluate(el=>el===document.activeElement),true);
  await other.getByRole('button',{name:'Release editing lease',exact:true}).click();await other.getByRole('heading',{name:'Read-only draft',exact:true}).waitFor();
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await button('Resume premises form').click();assert.equal(await field(`Premises ${number} · Postcode`).inputValue(),'SW1A 1AA');
  await field(`Premises ${number} \u00b7 Street`).fill('Fictional Demo Street');
  const combined=before.snapshot.productCode.includes('combined');
  if(combined)await field(`Premises ${number} \u00b7 Buildings sum insured`).fill('125000.01');
  await button('Apply to draft').click();await page.getByRole('dialog').waitFor({state:'hidden'});await page.waitForFunction(()=>document.activeElement?.textContent==='Add premises');await save();
  let saved=await get(path);const addition=saved.proposal.changes.find(x=>x.kind==='premises'&&x.operation==='add');assert.ok(addition);assert.equal(addition.payload.address.postcode,'SW1A 1AA');
  assert.ok(addition.riskItemId); if(combined)assert.ok(JSON.stringify(addition.payload).includes('125000.01'));
  await page.reload();await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await field('Premises to change').selectOption(addition.riskItemId);
  await button('Edit premises').click();assert.equal(await field(`Premises ${number} · Postcode`).inputValue(),'SW1A 1AA');await field(`Premises ${number} · Street`).fill('Revised Fictional Street');
  await page.screenshot({path:`${output}/${fixture.policyId}-desktop.png`});await page.setViewportSize({width:390,height:844});
  const bounds=await page.getByRole('dialog').boundingBox();assert.ok(bounds.x>=0&&bounds.x+bounds.width<=390);assert.equal(await page.getByRole('dialog').evaluate(el=>el.scrollWidth<=el.clientWidth),true);
  await page.screenshot({path:`${output}/${fixture.policyId}-mobile.png`});await button('Apply to draft').click();await save();
  saved=await get(path);assert.equal(saved.proposal.changes[0].changeId,addition.changeId);assert.equal(saved.proposal.changes[0].riskItemId,addition.riskItemId);assert.equal(saved.proposal.changes[0].payload.address.street,'Revised Fictional Street');

  await button('Remove premises').click();await button('Apply to draft').click();await save();assert.equal((await get(path)).proposal.changes.length,0);
  const original=before.snapshot.risk.premises?.[0];
  if(original){
   await field('Premises to change').selectOption(original.id);await button('Edit premises').click();await field('Premises 1 \u00b7 Postcode').fill('');await button('Apply to draft').click();await save();
   saved=await get(path);assert.equal(saved.proposal.changes[0].payloadMode,'replace');assert.equal(saved.proposal.changes[0].payload.address.postcode,undefined);
   const cleared=await get(path+'/editor');assert.equal(cleared.assessment.proposed.risk.premises[0].address.postcode,undefined);assert.equal(cleared.assessment.base.risk.premises[0].address.postcode,original.address.postcode);
  }
  await field('Takeover or abandonment reason').fill('Fictional premises scenario completed');await page.getByLabel('I confirm this draft should be abandoned.',{exact:true}).check();await button('Abandon draft').click();await page.getByText('This draft is closed. Its saved history remains available.',{exact:true}).waitFor();
  assert.deepEqual((await get(`/api/v1/policies/${fixture.policyId}`)).snapshot,before.snapshot);
  report.journeys.push({policyId:fixture.policyId,draftId,typedIncompleteSave:true,stableReloadAndEdit:true,cancelAddition:true,explicitIssuedFieldClear:!!original,modalRetainedAcrossTakeover:true,focusRestored:true,mobileDialogContained:true,issuedUnchanged:true});
 }
 assert.deepEqual(errors,[]);await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}finally{await browser.close();}
