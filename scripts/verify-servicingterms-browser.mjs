import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {chromium} from 'playwright';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/servicing-terms';await mkdir(output,{recursive:true});
const fixtures=JSON.parse(await readFile(process.env.COVER_POLICY_FIXTURES??'.local/browser-evidence/underwriting-issue/report.json','utf8')).journeys;
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
 if(prior?.createdBy==='servicing terms browser verification'&&fixtures.some(x=>x.policyId===prior.policyId)){
  path=`/api/v1/drafts/${prior.draftId}`;const draft=await get(path);assert.equal(draft.policyId,prior.policyId);assert.equal(draft.proposal.reason,'Fictional servicing terms browser journey');
  if(draft.state==='draft'){await page.goto(origin+`/drafts/${prior.draftId}`);await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await abandon();}
 }
 for(const fixture of fixtures){
  await page.setViewportSize({width:1560,height:1000});const before=await get(`/api/v1/policies/${fixture.policyId}`);
  await page.goto(origin+`/policies/${fixture.policyId}`);await field('Draft type').selectOption('adjustment');await field('Requested effective date').fill('2026-10-01');await field('Reason for draft').fill('Fictional servicing terms browser journey');await button('Create servicing draft').click();await page.waitForURL(/\/drafts\//);
  const draftId=page.url().split('/').at(-1);path=`/api/v1/drafts/${draftId}`;await writeFile(output+'/active.json',JSON.stringify({draftId,policyId:fixture.policyId,createdBy:'servicing terms browser verification'}));
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();await correct('Fictional servicing contract demonstration');
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
  await correct('Fictional changed contract requiring fresh acceptance');
  await panel.getByText('Historical acceptance — fresh confirmation required',{exact:true}).waitFor();
  const stale=await page.request.get(origin+path+'/terms');assert.equal(stale.status(),409);assert.equal((await get(path+'/terms/history/acceptances')).items.length,1);
  await button('Rate saved adjustment').click();await until(path+'/ratings',x=>x.current?.id!==cycleId&&x.current?.applicable);
  const changed=await get(path+'/terms');assert.equal(changed.terms,null);assert.equal(changed.acceptance,null);assert.equal(changed.acceptanceApplicable,false);
  await abandon();assert.deepEqual((await get(`/api/v1/policies/${fixture.policyId}`)).snapshot,before.snapshot);
  report.journeys.push({productCode:fixture.productCode,policyId:fixture.policyId,draftId,cycleId,termsId:prepared.id,deliveryId:view.delivery.id,acceptanceId:accepted.id,checks:['actual UI prepare/signature/send/acceptance','lost send response exact retry with one delivery','delivery separate from acceptance','current reviewed terms-bound proof','390px containment','amendment invalidates terms and acceptance','fresh cycle requires fresh terms','history retained and issued snapshot unchanged']});
  await writeFile(output+'/report.json',JSON.stringify(report,null,2));console.log(fixture.productCode+': servicing terms and acceptance verified');
 }
 assert.deepEqual(errors,[]);report.completedAt=new Date().toISOString();await writeFile(output+'/report.json',JSON.stringify(report,null,2));
}catch(error){await page.screenshot({path:output+'/failure.png',fullPage:true});await writeFile(output+'/failure.txt',String(error));throw error;}finally{await browser.close();}
