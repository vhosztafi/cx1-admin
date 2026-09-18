import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {chromium} from 'playwright';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/servicing-issue';await mkdir(output,{recursive:true});
const previousReport=await readFile(output+'/report.json','utf8').then(JSON.parse).catch(error=>{if(error.code==='ENOENT')return null;throw error;});
const fixtures=JSON.parse(await readFile('.local/browser-evidence/underwriting-issue/report.json','utf8')).journeys;
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1560,height:1000}});page.setDefaultTimeout(45000);
const errors=[];page.on('pageerror',e=>errors.push(e.message));const report={startedAt:new Date().toISOString(),journeys:[]};let path;
const button=name=>page.getByRole('button',{name,exact:true}),field=name=>page.getByLabel(name,{exact:true});
async function get(route){const response=await page.request.get(origin+route);assert.equal(response.status(),200,await response.text());assert.match(response.headers()['cache-control']??'',/no-store/);return response.json();}
async function until(route,predicate){for(let i=0;i<120;i++){const data=await get(route);if(predicate(data))return data;await page.waitForTimeout(250);}throw new Error('Saved state did not arrive: '+route);}
async function lease(){const draft=await get(path);if(Date.parse(draft.lease?.expiresAt??'')<Date.now()+60000){const response=page.waitForResponse(r=>r.url().endsWith(path+'/lease')&&r.request().method()==='PUT');await button('Renew editing lease').click();assert.equal((await response).status(),200);}}
async function command(suffix,click,status=200){await lease();const saved=await page.request.get(origin+path);const etag=saved.headers().etag;
 await page.waitForFunction(version=>document.querySelector('[data-servicing-draft-etag]')?.getAttribute('data-servicing-draft-etag')===version,etag);
 const response=page.waitForResponse(r=>r.url().endsWith(path+suffix)&&r.request().method()==='POST');await click();const result=await response;assert.equal(result.status(),status,await result.text());await page.getByText('Supporting information saved.',{exact:true}).waitFor();return result.json();}
async function abandon(){await field('Takeover or abandonment reason').fill('Fictional terms browser verification complete; retain its immutable history');await field('I confirm this draft should be abandoned.').check();await button('Abandon draft').click();await page.getByText('Abandoned',{exact:true}).waitFor();}
async function correct(name){await button('Correct policyholder details').click();await field('Trading name').fill(name);await button('Apply to draft').click();await button('Save draft').click();await page.getByText('Draft action saved.',{exact:true}).waitFor();}
const local=value=>{const d=new Date(value);d.setMinutes(d.getMinutes()-d.getTimezoneOffset());return d.toISOString().slice(0,19);};
try {
 await page.goto(origin+'/login');await field('Email address').fill('underwriter@cover.example');await field('Password').fill(password);await button('Sign in').click();await page.waitForURL(origin+'/');
 const prior=await readFile(output+'/active.json','utf8').then(JSON.parse).catch(e=>{if(e.code==='ENOENT')return null;throw e;});
 if(prior?.createdBy==='servicing issue browser verification'&&fixtures.some(x=>x.policyId===prior.policyId)){
  path=`/api/v1/drafts/${prior.draftId}`;const draft=await get(path);assert.equal(draft.policyId,prior.policyId);assert.equal(draft.proposal.reason,'Fictional servicing issue browser journey');
  if(draft.state==='draft'){await page.goto(origin+`/drafts/${prior.draftId}`);await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await abandon();}
 }
 for(const fixture of fixtures){
  await page.setViewportSize({width:1560,height:1000});const before=await get(`/api/v1/policies/${fixture.policyId}`);
  const previous=previousReport?.journeys.find(x=>x.policyId===fixture.policyId)?.receipt;
  await page.goto(origin+`/policies/${fixture.policyId}`+(previous?`?termId=${previous.termId}&versionId=${previous.versionIds.at(-1)}`:''));await field('Draft type').selectOption('adjustment');await field('Requested effective date').fill('2026-10-01');await field('Reason for draft').fill('Fictional servicing issue browser journey');await button('Create servicing draft').click();await page.waitForURL(/\/drafts\//);
  const draftId=page.url().split('/').at(-1);path=`/api/v1/drafts/${draftId}`;await writeFile(output+'/active.json',JSON.stringify({draftId,policyId:fixture.policyId,createdBy:'servicing issue browser verification'}));
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await correct('Fictional servicing contract demonstration '+Date.now());
  await button('Rate saved adjustment').click();await page.getByText('Rated',{exact:true}).waitFor();
  const requirements=await get(path+'/evidence/requirements');const cycleId=requirements.cycleId;assert.equal((await get(path+'/referrals')).items.length,0,'Use a within-authority fixture');
  await field('Supporting document').setInputFiles({name:'fictional-servicing-contract.txt',mimeType:'text/plain',buffer:Buffer.from('Fictional proof, signed statement and acceptance for demonstration only.')});
  const upload=await command('/evidence/uploads',()=>button('Upload document').click(),201);
  async function proof(required){
   const group=page.locator(`[data-requirement-code="${required.code}"][data-risk-item-id="${required.riskItemId??''}"]`);
   await group.getByLabel('Saved document',{exact:true}).selectOption(upload.id);await group.getByLabel('Attachment reason',{exact:true}).fill('Fictional proof attached for this exact servicing purpose');
   const attached=await command('/evidence',()=>group.getByRole('button',{name:'Attach proof',exact:true}).click(),201);
   const card=page.locator(`[data-evidence-id="${attached.id}"]`);await card.getByLabel('Review or withdrawal reason',{exact:true}).fill('Fictional document reviewed against this current purpose');
   await command(`/evidence/${attached.id}/reviews`,()=>card.getByRole('button',{name:'Record review',exact:true}).click());return attached.id;
  }
  for(const item of requirements.requirements)await proof(item.requirement);
  const prepared=await command('/terms/prepare',()=>button('Prepare servicing terms').click(),201);
  let view=await get(path+'/terms');assert.equal(view.terms.id,prepared.id);assert.equal(view.canSend,false);assert.equal(view.acceptance,null);
  const signed=(await get(path+'/evidence/requirements')).requirements.find(x=>x.requirement.code==='signed-statement').requirement;assert.equal(signed.termsVersionId,prepared.id);await proof(signed);
  const panel=page.getByRole('region',{name:'Servicing terms and acceptance',exact:true});await panel.getByRole('group',{name:'Send prepared terms',exact:true}).getByRole('checkbox').first().check();
  let original,body;await page.route('**'+path+'/terms/send',async route=>{original=route.request().headers();body=route.request().postData();const response=await route.fetch();assert.equal(response.status(),202);await route.abort('failed');},{times:1});
  await button('Send servicing terms').click();await button('Retry same proof action').waitFor();assert.equal(await button('Save draft').isDisabled(),true);
  const retry=page.waitForRequest(r=>r.url().endsWith(path+'/terms/send'));await button('Retry same proof action').click();const repeated=await retry;
  for(const header of ['idempotency-key','if-match','x-edit-lease'])assert.equal(repeated.headers()[header],original[header]);assert.equal(repeated.postData(),body);
  await page.getByText('Supporting information saved.',{exact:true}).waitFor();view=await until(path+'/terms',x=>x.delivery?.state==='delivered');
  assert.equal(view.acceptance,null);assert.equal((await get(path+'/terms/history/deliveries')).items.length,1);
  const acceptance=(await get(path+'/evidence/requirements')).requirements.find(x=>x.requirement.code==='acceptance-proof').requirement;const acceptanceProof=await proof(acceptance);
  await field('Accepted by').fill('Fictional insured customer');await field('Acceptance received at').fill(local(Date.now()));await field('Reviewed acceptance proof').selectOption(acceptanceProof);
  const accepted=await command('/acceptances',()=>button('Record servicing acceptance').click(),201);view=await get(path+'/terms');assert.equal(view.acceptance.id,accepted.id);assert.equal(view.acceptanceApplicable,true);
  await panel.getByText('Current acceptance',{exact:true}).waitFor();await panel.evaluate(el=>window.scrollTo(0,el.getBoundingClientRect().top+scrollY-90));await page.screenshot({path:output+'/'+fixture.productCode+'-terms-desktop.png'});
  await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await panel.evaluate(el=>window.scrollTo(0,el.getBoundingClientRect().top+scrollY-90));await page.screenshot({path:output+'/'+fixture.productCode+'-terms-mobile.png'});
  await panel.getByRole('group',{name:'Record customer acceptance',exact:true}).evaluate(el=>window.scrollTo(0,el.getBoundingClientRect().top+scrollY-90));await page.screenshot({path:output+'/'+fixture.productCode+'-acceptance-mobile.png'});await page.setViewportSize({width:1560,height:1000});
  await lease();
  await field('Issue reason').fill('Issue the fictional accepted changes with exact retry verification');
  await field('I confirm the accepted changes and effective dates.').check();
  let issueHeaders,issueBody,issued;
  await page.route('**'+path+'/issue',async route=>{issueHeaders=route.request().headers();issueBody=route.request().postData();const response=await route.fetch();assert.equal(response.status(),201,await response.text());issued=await response.json();await route.abort('failed');},{times:1});
  await button('Issue accepted adjustment').click();await button('Retry same issue').waitFor();
  assert.equal(await button('Save draft').isDisabled(),true);
  const issueRetry=page.waitForRequest(r=>r.url().endsWith(path+'/issue'));await button('Retry same issue').click();const repeatedIssue=await issueRetry;
  for(const header of ['idempotency-key','if-match','x-edit-lease'])assert.equal(repeatedIssue.headers()[header],issueHeaders[header]);assert.equal(repeatedIssue.postData(),issueBody);
  await page.getByText('Adjustment issued',{exact:true}).waitFor();await page.getByText('Issued',{exact:true}).waitFor();
  await page.getByText(/Adjustment premium £.*Revised term premium £/).waitFor();
  assert.equal((await get(path)).state,'issued');assert.equal((await get(path)).lease?.active??false,false);
  assert.equal(issued.versionIds.length,1);assert.equal(issued.documentRequestIds.length,3);assert.equal(issued.midIntentIds.length,1);
  const retained=await get(`/api/v1/policies/${fixture.policyId}/terms/${issued.termId}/versions/${issued.versionId}`);
  assert.equal(retained.snapshot.snapshotFormat,'issued-servicing-1');assert.equal(retained.snapshot.provenance.servicingIssueDecisionId,issued.decisionId);
  assert.equal(retained.financials.journalId,issued.journalId);assert.equal(retained.financials.amountDue,issued.netAmount);
  const current=await get(`/api/v1/policies/${fixture.policyId}`);assert.equal(current.versionId,before.versionId);assert.deepEqual(current.snapshot,before.snapshot);
  await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
  const receiptPanel=page.getByRole('article',{name:'Adjustment issue receipt'});await receiptPanel.scrollIntoViewIfNeeded();await page.screenshot({path:output+'/'+fixture.productCode+'-receipt-mobile.png'});
  await page.reload();await page.getByText('Issued',{exact:true}).waitFor();assert.equal((await get(path)).state,'issued');
  assert.deepEqual((await get(`/api/v1/policies/${fixture.policyId}/terms/${issued.termId}/versions/${issued.versionId}`)).snapshot,retained.snapshot);
  report.journeys.push({productCode:fixture.productCode,policyId:fixture.policyId,draftId,cycleId,receipt:issued,contentHash:retained.contentHash,checks:['actual UI prepare/signature/send/acceptance/issue','lost issue response exact retry','sealed financial graph and immutable provenance readback','future current snapshot unchanged','390px receipt containment','reload retains issued draft and exact snapshot']});
  await writeFile(output+'/report.json',JSON.stringify(report,null,2));console.log(fixture.productCode+': servicing issue verified');
 }
 assert.deepEqual(errors,[]);report.completedAt=new Date().toISOString();await writeFile(output+'/report.json',JSON.stringify(report,null,2));
}catch(error){await page.screenshot({path:output+'/failure.png',fullPage:true});await writeFile(output+'/failure.txt',String(error));throw error;}finally{await browser.close();}
