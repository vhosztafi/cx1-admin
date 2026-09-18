import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {chromium} from 'playwright';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/servicing-capacity';await mkdir(output,{recursive:true});
const fixtures=JSON.parse(await readFile('.local/browser-evidence/underwriting-issue/report.json','utf8')).journeys;
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1560,height:1000}});page.setDefaultTimeout(45000);
const errors=[];page.on('pageerror',error=>errors.push(error.message));
page.on('response',response=>{if(response.status()>=400&&response.request().method()==='POST')console.log('Command failure: '+response.status()+' '+new URL(response.url()).pathname);});
const button=name=>page.getByRole('button',{name,exact:true}),field=name=>page.getByLabel(name,{exact:true});
const report={startedAt:new Date().toISOString(),journeys:[]};let path;
async function get(route){const response=await page.request.get(origin+route);assert.equal(response.status(),200,await response.text());assert.match(response.headers()['cache-control']??'',/no-store/);return response.json();}
async function until(route,predicate){for(let i=0;i<100;i++){const value=await get(route);if(predicate(value))return value;await page.waitForTimeout(250);}throw new Error('Persisted state did not arrive: '+route);}
async function lease(){const draft=await get(path);if(Date.parse(draft.lease?.expiresAt??'')<Date.now()+60000){const response=page.waitForResponse(r=>r.url().endsWith(path+'/lease')&&r.request().method()==='PUT');await button('Renew editing lease').click();assert.equal((await response).status(),200);}}
async function command(suffix,click,status=200){await lease();const saved=await page.request.get(origin+path);assert.equal(saved.status(),200);const etag=saved.headers().etag;
 await page.waitForFunction(version=>document.querySelector('[data-servicing-draft-etag]')?.getAttribute('data-servicing-draft-etag')===version,etag);
 const response=page.waitForResponse(r=>r.url().endsWith(path+suffix)&&r.request().method()==='POST');await click();const result=await response;assert.equal(result.status(),status,await result.text());await page.getByText('Supporting information saved.',{exact:true}).waitFor();return result.json();}
async function abandon(reason){await field('Takeover or abandonment reason').fill(reason);await field('I confirm this draft should be abandoned.').check();await button('Abandon draft').click();await page.getByText('Abandoned',{exact:true}).waitFor();}
async function capture(panel,name){await panel.evaluate(element=>window.scrollTo(0,element.getBoundingClientRect().top+window.scrollY-90));await page.screenshot({path:output+'/'+name});}
try {
 await page.goto(origin+'/login');await field('Email address').fill('underwriter@cover.example');await field('Password').fill(password);await button('Sign in').click();await page.waitForURL(origin+'/');
 const prior=await readFile(output+'/active.json','utf8').then(JSON.parse).catch(error=>{if(error.code==='ENOENT')return null;throw error;});
 if(prior?.createdBy==='servicing capacity browser verification'&&fixtures.some(item=>item.policyId===prior.policyId)) {
  const draft=await get(`/api/v1/drafts/${prior.draftId}`);assert.equal(draft.policyId,prior.policyId);assert.equal(draft.proposal.reason,'Fictional servicing capacity browser journey');
  if(draft.state==='draft'){await page.goto(origin+`/drafts/${prior.draftId}`);await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await abandon('Close prior fictional capacity browser attempt, retaining its history');}
 }
 for(const fixture of fixtures) {
  await page.setViewportSize({width:1560,height:1000});const before=await get(`/api/v1/policies/${fixture.policyId}`);
  await page.goto(origin+`/policies/${fixture.policyId}`);await field('Draft type').selectOption('adjustment');await field('Requested effective date').fill('2026-10-01');await field('Reason for draft').fill('Fictional servicing capacity browser journey');await button('Create servicing draft').click();await page.waitForURL(/\/drafts\//);
  const draftId=page.url().split('/').at(-1);path=`/api/v1/drafts/${draftId}`;await writeFile(output+'/active.json',JSON.stringify({draftId,policyId:fixture.policyId,createdBy:'servicing capacity browser verification'}));
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await field('Cover to change').selectOption('tools-equipment');await button('Amend cover').click();const dialog=page.getByRole('dialog',{name:'Amend cover',exact:true});
  await dialog.getByLabel('Tools and equipment choice',{exact:true}).selectOption('true');await dialog.getByLabel('Tools and equipment limit (£)',{exact:true}).fill('10000.00');await dialog.getByLabel('Tools and equipment excess (£)',{exact:true}).fill('250.00');await dialog.getByRole('button',{name:'Apply to draft',exact:true}).click();
  await button('Save draft').click();await page.getByText('Draft action saved.',{exact:true}).waitFor();await button('Rate saved adjustment').click();await page.getByText('Rated',{exact:true}).waitFor();
  const referrals=await get(path+'/referrals'),referral=referrals.items.find(item=>item.ruleCode==='cover-tools-equipment');assert.ok(referral,'Tools limit referral required');const cycleId=referrals.cycleId;
  await page.getByRole('link',{name:'Refer to capacity provider',exact:true}).click();assert.ok(page.url().endsWith('#servicing-referrals'));
  const row=page.locator(`[data-referral-id="${referral.id}"]`);await row.getByText('Carrier capacity request',{exact:true}).click();await row.getByLabel('Capacity request reason',{exact:true}).fill('Request fictional tools permission for this saved change');
  const created=await command('/capacity',()=>row.getByRole('button',{name:'Create carrier request',exact:true}).click(),201);const caseId=created.id;const casePath=`/capacity/${caseId}`;
  const panel=row.getByRole('region',{name:'Servicing carrier case',exact:true});await panel.waitFor();
  await panel.getByLabel('Carrier message',{exact:true}).fill('Fictional request for tools equipment capacity up to GBP 15000.00');await panel.getByLabel('Carrier message reason',{exact:true}).fill('Submit the exact fictional saved tools schedule');
  const submitted=await command(casePath+'/submissions',()=>panel.getByRole('button',{name:'Submit carrier request',exact:true}).click(),202);
  await until(path+casePath,item=>item.case.state==='queried');await panel.getByRole('button',{name:'Reply and resubmit to carrier',exact:true}).waitFor();
  await panel.getByLabel('Carrier message',{exact:true}).fill('Fictional clarification: tools are secured overnight and inventory reviewed');await panel.getByLabel('Carrier message reason',{exact:true}).fill('Answer the fictional carrier query with a new submission');
  const replied=await command(casePath+'/query-replies',()=>panel.getByRole('button',{name:'Reply and resubmit to carrier',exact:true}).click(),202);assert.notEqual(replied.submissionId,submitted.submissionId);
  await until(path+casePath,item=>item.case.state==='queried'&&item.case.currentSubmissionId===replied.submissionId);
  console.log(fixture.productCode+': created, queried and replied through the UI');
  async function proof(code) {
   await field('Supporting document').setInputFiles({name:`fictional-carrier-${code}.txt`,mimeType:'text/plain',buffer:Buffer.from('Fictional reviewed carrier proof. No real personal or insurance data.')});
   const uploaded=await command('/evidence/uploads',()=>button('Upload document').click(),201);
   const requirements=await get(path+'/evidence/requirements');const required=requirements.requirements.find(item=>item.requirement.code===code).requirement;
   const group=page.locator(`[data-requirement-code="${code}"][data-risk-item-id="${required.riskItemId??''}"]`);
   await group.getByLabel('Saved document',{exact:true}).selectOption(uploaded.id);await group.getByLabel('Attachment reason',{exact:true}).fill('Fictional proof attached to its exact current carrier purpose');
   const attached=await command('/evidence',()=>group.getByRole('button',{name:'Attach proof',exact:true}).click(),201);
   const card=page.locator(`[data-evidence-id="${attached.id}"]`);await card.getByLabel('Review outcome',{exact:true}).selectOption('accepted');await card.getByLabel('Review or withdrawal reason',{exact:true}).fill('Fictional document reviewed and accepted for this exact purpose');
   await command(`/evidence/${attached.id}/reviews`,()=>card.getByRole('button',{name:'Record review',exact:true}).click());return attached.id;
  }
  const responseProof=await proof('capacity-response');await panel.getByText('Record a supplied carrier response',{exact:true}).click();
  await panel.getByLabel('Carrier response proof',{exact:true}).selectOption(responseProof);await panel.getByLabel('Carrier underwriter',{exact:true}).fill('Fictional carrier underwriter');await panel.getByLabel('Carrier reference',{exact:true}).fill('DEMO-SERVICING-TOOLS');
  const local=value=>{const d=new Date(value);d.setMinutes(d.getMinutes()-d.getTimezoneOffset());return d.toISOString().slice(0,19);};
  await panel.getByLabel('Carrier response received',{exact:true}).fill(local(Date.now()));await panel.getByLabel('Carrier outcome',{exact:true}).selectOption('approve-with-conditions');
  await panel.getByLabel('Carrier permission starts',{exact:true}).fill(local(Date.now()-60000));await panel.getByLabel('Carrier permission ends',{exact:true}).fill('2027-12-31T23:00');await panel.getByLabel('Carrier maximum amount (GBP)',{exact:true}).fill('15000.00');
  const responseFields=panel.getByRole('group',{name:'Supplied carrier response',exact:true});for(const checkbox of await responseFields.getByRole('checkbox').all())await checkbox.check();
  await responseFields.getByLabel('Condition type',{exact:true}).selectOption('provide-trading-history');await responseFields.getByRole('button',{name:'Add condition',exact:true}).click();
  await panel.getByLabel('Carrier response text',{exact:true}).fill('Fictional approval up to GBP 15000.00, subject to reviewed trading history');await panel.getByLabel('Carrier response reason',{exact:true}).fill('Record fictional supplied permission and explicit condition');
  await command(casePath+'/responses',()=>panel.getByRole('button',{name:'Record supplied carrier response',exact:true}).click(),201);
  const conditional=await get(path+casePath);assert.equal(conditional.case.state,'conditional');assert.equal(conditional.ready,false);const condition=conditional.conditions[0];assert.ok(condition);
  const trading=await proof('trading-history');assert.equal((await get(path+casePath)).ready,false,'Reviewed proof alone must not resolve a carrier condition');
  const resolution=panel.locator(`[data-carrier-condition-id="${condition.id}"]`);await resolution.getByLabel('Reviewed carrier condition proof',{exact:true}).selectOption(trading);await resolution.getByLabel('Carrier resolution reason',{exact:true}).fill('Explicitly resolve the carrier condition using current accepted trading proof');
  await command(`/capacity-conditions/${condition.id}/resolutions`,()=>resolution.getByRole('button',{name:'Record carrier condition resolution',exact:true}).click());
  await until(path+casePath,item=>item.ready);assert.equal((await get(path+'/referrals')).items.find(item=>item.id===referral.id).decisionReady,false,'Carrier permission is not an internal referral approval');
  await panel.getByRole('button',{name:'Carrier authority context',exact:true}).click();await panel.getByText('tools limit: GBP 15000.00',{exact:true}).waitFor();
  await capture(panel,`${fixture.productCode}-authority-desktop.png`);await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await capture(panel,`${fixture.productCode}-authority-mobile.png`);await page.setViewportSize({width:1560,height:1000});
  await panel.getByLabel('Carrier request action',{exact:true}).selectOption('reopen');await panel.getByLabel('Carrier action reason',{exact:true}).fill('Reopen fictional permission while preserving its full correspondence');await command(casePath+'/actions',()=>panel.getByRole('button',{name:'Confirm reopen',exact:true}).click());
  const reopened=await get(path+casePath);assert.equal(reopened.case.state,'draft');assert.equal(reopened.ready,false);assert.equal(reopened.case.currentResponseId,conditional.case.currentResponseId);
  const history=await get(path+casePath+'/responses');assert.ok(history.items.length>=3);assert.equal((await get(path+casePath+'/submissions')).items.length,2);
  await abandon('Fictional carrier browser verification complete; retain all history');assert.deepEqual((await get(`/api/v1/policies/${fixture.policyId}`)).snapshot,before.snapshot);
  report.journeys.push({productCode:fixture.productCode,policyId:fixture.policyId,draftId,cycleId,caseId,conditionId:condition.id,checks:['real UI submission/query/reply','reviewed exact response proof','supplied dated conditional approval','review alone does not resolve condition','explicit resolution enables carrier readiness only','390px containment','reopen removes applicability and retains history','issued snapshot unchanged']});await writeFile(output+'/report.json',JSON.stringify(report,null,2));
 }
 assert.deepEqual(errors,[]);report.completedAt=new Date().toISOString();await writeFile(output+'/report.json',JSON.stringify(report,null,2));console.log('Verified servicing carrier journeys for both Motor Trade products.');
}catch(error){await page.screenshot({path:output+'/failure.png',fullPage:true}).catch(()=>{});await writeFile(output+'/failure.txt',String(error));throw error;}
finally{await browser.close();}
