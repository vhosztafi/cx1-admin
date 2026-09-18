import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {chromium} from 'playwright';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
assert.ok(['127.0.0.1','localhost'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/servicing-evidence';await mkdir(output,{recursive:true});
const navigationOnly=process.env.COVER_SERVICING_NAVIGATION_ONLY==='1';const reportPath=output+(navigationOnly?'/navigation-report.json':'/report.json');
const fixtures=JSON.parse(await readFile('.local/browser-evidence/underwriting-issue/report.json','utf8')).journeys;
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const browser=await chromium.launch({channel:'chrome',headless:true});
const page=await browser.newPage({viewport:{width:1560,height:1000}});page.setDefaultTimeout(45000);
const errors=[];page.on('pageerror',error=>errors.push(error.message));
page.on('requestfailed',request=>{if(['POST','PUT','DELETE'].includes(request.method())&&request.url().includes('/api/v1/drafts/'))console.log('Mutation transport failure: '+request.method()+' '+new URL(request.url()).pathname+' '+request.failure()?.errorText);});
page.on('response',response=>{if(response.status()>=400&&['POST','PUT','DELETE'].includes(response.request().method())&&response.url().includes('/api/v1/drafts/'))console.log('Mutation HTTP failure: '+response.status()+' '+new URL(response.url()).pathname);});
const report={startedAt:new Date().toISOString(),journeys:[]};
const button=name=>page.getByRole('button',{name,exact:true}),field=name=>page.getByLabel(name,{exact:true});
async function get(path){const response=await page.request.get(origin+path);assert.equal(response.status(),200,await response.text());assert.match(response.headers()['cache-control']??'',/no-store/);return response.json();}
async function waitForRead(path,predicate){for(let i=0;i<80;i++){const value=await get(path);if(predicate(value))return value;await page.waitForTimeout(250);}throw new Error('Saved read did not reach expected state: '+path);}
let path;
async function lease(){const draft=await get(path);if(Date.parse(draft.lease?.expiresAt??'')<Date.now()+60000){const response=page.waitForResponse(r=>r.url().endsWith(path+'/lease')&&r.request().method()==='PUT');await button('Renew editing lease').click();assert.equal((await response).status(),200);}}
async function command(suffix,click,status=200){await lease();const response=page.waitForResponse(r=>r.url().endsWith(path+suffix)&&r.request().method()==='POST');await click();assert.equal((await response).status(),status,await(await response).text());await page.getByText('Supporting information saved.',{exact:true}).waitFor();}
try{
 await page.goto(origin+'/login');await field('Email address').fill('underwriter@cover.example');await field('Password').fill(password);await button('Sign in').click();await page.waitForURL(origin+'/');
 // Close only an unfinished draft explicitly recorded by this same harness.
 // Abandonment retains its evidence and decisions; it never resets demo data.
 const prior=await readFile(`${output}/active.json`,'utf8').then(JSON.parse).catch(error=>{if(error.code==='ENOENT')return null;throw error;});
 if(prior?.createdBy==='servicing evidence browser verification'&&fixtures.some(x=>x.policyId===prior.policyId)){
  const draft=await get(`/api/v1/drafts/${prior.draftId}`);
  if(draft.state==='draft'){
   assert.equal(draft.policyId,prior.policyId);assert.equal(draft.proposal.reason,'Fictional servicing evidence browser journey');
   await page.goto(origin+`/drafts/${prior.draftId}`);await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
   await field('Takeover or abandonment reason').fill('Close the prior fictional browser attempt while retaining its audit');await field('I confirm this draft should be abandoned.').check();await button('Abandon draft').click();await page.getByText('Abandoned',{exact:true}).waitFor();
  }
 }
 for(const fixture of fixtures){
  await page.setViewportSize({width:1560,height:1000});const before=await get(`/api/v1/policies/${fixture.policyId}`);
  await page.goto(origin+`/policies/${fixture.policyId}`);await field('Draft type').selectOption('adjustment');await field('Requested effective date').fill('2026-10-01');await field('Reason for draft').fill('Fictional servicing evidence browser journey');await button('Create servicing draft').click();await page.waitForURL(/\/drafts\//);
  const draftId=page.url().split('/').at(-1);path=`/api/v1/drafts/${draftId}`;
  await writeFile(`${output}/active.json`,JSON.stringify({draftId,policyId:fixture.policyId,productCode:fixture.productCode,createdBy:'servicing evidence browser verification'}));
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await button('Amend trade activities').click();await field('Business start date').fill('2025-01-01');await button('Apply to draft').click();
  assert.equal(await button('Upload document').isDisabled(),true);
  await button('Save draft').click();await page.getByText('Draft action saved.',{exact:true}).waitFor();
  await button('Rate saved adjustment').click();await page.getByText('Rated',{exact:true}).waitFor();
  let requirements=await get(path+'/evidence/requirements');assert.equal(requirements.applicable,true);assert.ok(requirements.requirements.every(x=>!x.satisfied));
  const cycleId=requirements.cycleId;let referrals=await get(path+'/referrals');const referral=referrals.items.find(x=>x.ruleCode==='UW-22');assert.ok(referral);
  console.log(fixture.productCode+': saved, rated and current referral loaded');
  const editor=await get(path+'/editor');const driver=editor.assessment.slices[0].proposed.risk.drivers[0];assert.ok(driver);
  const row=page.locator(`[data-referral-id="${referral.id}"]`);await row.getByRole('checkbox').check();
  await field('Decision outcome').selectOption('approve-with-conditions');await field('Condition type').selectOption('provide-driver-proof');await field('Condition risk target').selectOption(driver.id);await field('Driver proof purpose').selectOption('photocard-both-sides');await button('Add condition').click();await field('Decision reason').fill('Fictional licence content must be independently reviewed');
  await command(`/referrals/${referral.id}/decisions`,()=>button('Record decision for 1 selected').click());
  referrals=await get(path+'/referrals');const condition=referrals.items.find(x=>x.id===referral.id).conditions[0];assert.equal(condition.satisfied,false);
  console.log(fixture.productCode+': conditional decision persisted');
  await page.getByRole('link',{name:'Review referrals',exact:true}).click();assert.ok(page.url().endsWith('#servicing-referrals'));
  await page.getByRole('link',{name:'Back to policy',exact:true}).click();await page.locator(`a[href="/drafts/${draftId}#servicing-referrals"]`).click();
  await page.locator('#servicing-referrals').waitFor();await page.waitForFunction(()=>document.activeElement?.id==='servicing-referrals');
  await row.waitFor();await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await row.getByText('Your current authority and binder limits',{exact:true}).click();await row.getByText(/Current decision authority is available/).waitFor();
  const authority=await get(path+`/referrals/${referral.id}/authority?pageSize=5`);assert.equal(authority.canDecide,true);assert.ok(authority.items.length>0);
  await field('Supporting document').setInputFiles({name:'fictional-servicing-proof.txt',mimeType:'text/plain',buffer:Buffer.from('Fictional supporting evidence for the local servicing demonstration. No real personal data.')});
  let original;
  await page.route('**'+path+'/evidence/uploads',async route=>{original=route.request().headers();const committed=await route.fetch();assert.equal(committed.status(),201);await route.abort('failed');},{times:1});
  await button('Upload document').click();await button('Retry same proof action').waitFor();assert.equal(await button('Save draft').isDisabled(),true);assert.equal(await button('Re-rate saved adjustment').isDisabled(),true);
  const retry=page.waitForRequest(r=>r.url().endsWith(path+'/evidence/uploads'));await button('Retry same proof action').click();const retried=await retry;
  for(const key of ['idempotency-key','if-match','x-edit-lease'])assert.equal(retried.headers()[key],original[key]);
  await page.getByText('Supporting information saved.',{exact:true}).waitFor();const files=await get(path+'/evidence-files');assert.equal(files.items.length,1);const fileId=files.items[0].id;
  console.log(fixture.productCode+': lost upload response retried exactly once');
  let renewalChecked=false;
  const attach=async requirement=>{
   await lease();
   const group=page.locator(`[data-requirement-code="${requirement.code}"][data-risk-item-id="${requirement.riskItemId??''}"]`);
   await group.getByLabel('Saved document',{exact:true}).selectOption(fileId);await group.getByLabel('Attachment reason',{exact:true}).fill('Fictional proof attached for this exact saved purpose');
   if(!renewalChecked){const renewal=page.waitForResponse(r=>r.url().endsWith(path+'/lease')&&r.request().method()==='PUT');await button('Renew editing lease').click();assert.equal((await renewal).status(),200);await group.locator('button:not(:disabled)').waitFor();assert.equal(await group.getByLabel('Saved document',{exact:true}).inputValue(),fileId);assert.equal(await group.getByLabel('Attachment reason',{exact:true}).inputValue(),'Fictional proof attached for this exact saved purpose');renewalChecked=true;}
   await command('/evidence',()=>group.getByRole('button',{name:'Attach proof',exact:true}).click(),201);
   return (await get(path+`/evidence?cycleId=${cycleId}`)).items.find(x=>x.code===requirement.code&&x.riskItemId===requirement.riskItemId&&!x.withdrawn);
  };
  const review=async(association,outcome)=>{
   await lease();
   const card=page.locator(`[data-evidence-id="${association.id}"]`);await card.getByLabel('Review outcome',{exact:true}).selectOption(outcome);await card.getByLabel('Review or withdrawal reason',{exact:true}).fill(`Fictional ${outcome} content review for this proof purpose`);
   await command(`/evidence/${association.id}/reviews`,()=>card.getByRole('button',{name:'Record review',exact:true}).click());
  };
  const resolve=async association=>{
   await lease();
   const group=page.locator(`[data-condition-id="${condition.id}"]`);await group.getByLabel('Reviewed proof',{exact:true}).selectOption(association.id);await group.getByLabel('Resolution reason',{exact:true}).fill('Fictional reviewed proof satisfies the exact licence condition');
   await command(`/conditions/${condition.id}/resolutions`,()=>group.getByRole('button',{name:'Record condition resolution',exact:true}).click());
  };
  const licence=requirements.requirements.find(x=>x.requirement.code==='photocard-both-sides'&&x.requirement.riskItemId===driver.id).requirement;
  const first=await attach(licence);assert.ok(first);assert.equal((await get(path+'/evidence/requirements')).requirements.find(x=>x.requirement.inputFingerprint===licence.inputFingerprint&&x.requirement.code===licence.code).satisfied,false);
  if(navigationOnly){
   assert.equal(first.reason,'Fictional proof attached for this exact saved purpose');await page.reload();await page.locator(`[data-evidence-id="${first.id}"]`).waitFor();
   assert.ok((await get(path+`/evidence?cycleId=${cycleId}`)).items.some(x=>x.id===first.id));
   await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.locator(`[data-evidence-id="${first.id}"]`).screenshot({path:`${output}/${fixture.productCode}-navigation-proof.png`});
   await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await field('Takeover or abandonment reason').fill('Fictional referral navigation verification complete');await field('I confirm this draft should be abandoned.').check();await button('Abandon draft').click();await page.getByText('Abandoned',{exact:true}).waitFor();
   assert.deepEqual((await get(`/api/v1/policies/${fixture.policyId}`)).snapshot,before.snapshot);
   report.journeys.push({productCode:fixture.productCode,policyId:fixture.policyId,draftId,cycleId,checks:['policy-to-own-draft referral navigation and focus','draft referral review link','persisted conditional referral','exact lost-upload retry','forced renewal retains file selection and reason while stale writes disabled','attachment persisted after renewal and reload','390px containment','issued snapshot unchanged']});await writeFile(reportPath,JSON.stringify(report,null,2));continue;
  }
  await review(first,'rejected');await review(first,'accepted');await resolve(first);
  referrals=await get(path+'/referrals');assert.equal(referrals.items.find(x=>x.id===referral.id).conditions[0].satisfied,true);assert.equal(referrals.items.find(x=>x.id===referral.id).decisionReady,false,'Missing trading proof remains independently blocking');
  for(const {requirement} of requirements.requirements.filter(x=>!(x.requirement.code===licence.code&&x.requirement.riskItemId===licence.riskItemId))){const association=await attach(requirement);await review(association,'accepted');}
  requirements=await get(path+'/evidence/requirements');assert.ok(requirements.requirements.every(x=>x.satisfied));
  console.log(fixture.productCode+': all required proof persisted and reviewed');
  if(fixture.productCode==='motor-trade-combined')assert.ok(requirements.requirements.some(x=>x.requirement.code==='premises-security'),'Combined journey must exercise premises proof');
  assert.equal((await get(path+'/referrals')).items.find(x=>x.id===referral.id).decisionReady,true);
  const old=page.locator(`[data-evidence-id="${first.id}"]`);await old.getByLabel('Review or withdrawal reason',{exact:true}).fill('Withdraw fictional proof to verify dependent condition becomes outstanding');await command(`/evidence/${first.id}/withdraw`,()=>old.getByRole('button',{name:'Withdraw proof',exact:true}).click());
  assert.equal((await get(path+'/referrals')).items.find(x=>x.id===referral.id).decisionReady,false);
  const replacement=await attach(licence);await review(replacement,'accepted');await resolve(replacement);
  await page.reload();await page.getByText('Reviewed proof received',{exact:true}).first().waitFor();assert.equal((await get(path+'/referrals')).items.find(x=>x.id===referral.id).decisionReady,true);
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  assert.equal((await get(path+`/evidence/${first.id}/events`)).items.length,3);
  await page.screenshot({path:`${output}/${fixture.productCode}-desktop.png`,fullPage:true});await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:`${output}/${fixture.productCode}-mobile.png`,fullPage:true});
  await button('Re-rate saved adjustment').click();const history=await waitForRead(path+'/ratings',x=>x.current?.id!==cycleId&&x.current?.applicable);assert.ok(history.current);
  await field('Evidence rating cycle').selectOption(cycleId);await page.locator(`[data-evidence-id="${first.id}"]`).waitFor();assert.equal(await page.locator(`[data-evidence-id="${replacement.id}"]`).getByRole('button',{name:'Record review',exact:true}).isDisabled(),true);
  const download=await page.request.get(origin+path+`/evidence-files/${fileId}/content`);assert.equal(download.status(),200);assert.match(await download.text(),/Fictional supporting evidence/);
  await field('Takeover or abandonment reason').fill('Fictional evidence browser verification complete');await field('I confirm this draft should be abandoned.').check();await button('Abandon draft').click();await page.getByText('Abandoned',{exact:true}).waitFor();
  assert.ok((await get(path+`/evidence?cycleId=${cycleId}`)).items.length>0);assert.deepEqual((await get(`/api/v1/policies/${fixture.policyId}`)).snapshot,before.snapshot);
  report.journeys.push({productCode:fixture.productCode,policyId:fixture.policyId,draftId,cycleId,fileId,referralId:referral.id,checks:['lease renewal retains proof form without stale write authority','policy-to-own-draft referral navigation and focus','draft review referral link','dirty edits block proof','conditional decision with saved driver target','current authority display','lost upload response exact retry and one stored file','driver and applicable premises proofs','rejected then accepted content','independently blocking trading proof','condition resolution','withdrawal removes readiness','replacement restores readiness','reload persistence','historical cycle readonly','scoped download','390px containment','issued snapshot unchanged']});
  await writeFile(reportPath,JSON.stringify(report,null,2));
 }
 assert.deepEqual(errors,[]);report.completedAt=new Date().toISOString();await writeFile(reportPath,JSON.stringify(report,null,2));console.log(navigationOnly?'Both Motor Trade targeted navigation and renewal browser journeys passed.':'Both Motor Trade servicing evidence browser journeys passed.');
}catch(error){await page.screenshot({path:`${output}/failure.png`,fullPage:true}).catch(()=>{});throw error;}finally{await browser.close();}
