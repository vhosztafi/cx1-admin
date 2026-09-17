import {chromium} from 'playwright';
import assert from 'node:assert/strict';
import {readFile,mkdir,writeFile} from 'node:fs/promises';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
assert.ok(['127.0.0.1','localhost'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/underwriting-capacity';await mkdir(output,{recursive:true});
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const ratingFixtures=JSON.parse(await readFile('.local/browser-evidence/underwriting-rating/report.json','utf8')).journeys;
const examples=JSON.parse(await readFile('contracts/examples/underwriting-demo.json','utf8')).proposals;
const browser=await chromium.launch({channel:'chrome',headless:true}),contexts=[],errors=[],report={journeys:[]};
const day=new Date();day.setUTCDate(day.getUTCDate()+1);const tomorrow=day.toISOString().slice(0,10);
async function login(role){const context=await browser.newContext({viewport:{width:1560,height:1000},timezoneId:'UTC'});contexts.push(context);const page=await context.newPage();page.setDefaultTimeout(25000);page.on('pageerror',e=>errors.push(e.message));await until(async()=>{try{return(await page.request.get(origin+'/api/v1/auth/csrf')).ok();}catch{return false;}},Boolean);await page.goto(origin+'/login');await page.getByLabel('Email address').fill(role+'@cover.example');await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');return page;}
async function get(page,path){const r=await page.request.get(origin+path);assert.equal(r.status(),200,`${path}: ${await r.text()}`);return{data:await r.json(),etag:r.headers().etag};}
async function post(page,path,data,etag,multipart){const csrf=(await get(page,'/api/v1/auth/csrf')).data.requestToken;const r=await page.request.post(origin+path,{headers:{'X-CSRF-Token':csrf,'Idempotency-Key':crypto.randomUUID(),...(etag?{'If-Match':etag}:{})},...(multipart?{multipart}:{data})});assert.ok(r.ok(),`${path}: ${r.status()} ${await r.text()}`);return{data:await r.json(),etag:r.headers().etag};}
async function until(read,accept){const end=Date.now()+45000;do{const value=await read();if(accept(value))return value;await new Promise(r=>setTimeout(r,250));}while(Date.now()<end);throw Error('Timed out waiting for persisted outcome');}
async function open(page,id){await page.goto(origin+'/quotes/'+id);await page.getByRole('tab',{name:'Underwriting',exact:true}).click();await page.getByRole('heading',{name:'Supporting information',exact:true}).waitFor();}
async function confirm(page){const dialog=page.getByRole('dialog');await dialog.getByRole('button',{name:'Confirm action',exact:true}).click();await dialog.waitFor({state:'hidden'});}
async function selectReferral(page,id){const box=page.locator(`[data-referral-id="${id}"]`).getByRole('checkbox');if(!await box.isChecked())await box.check();}
async function decision(page,id,outcome,reason){await selectReferral(page,id);await page.getByLabel('Decision outcome',{exact:true}).selectOption(outcome);await page.getByLabel('Referral decision reason',{exact:true}).fill(reason);}
async function addCondition(page,code){await page.getByLabel('Condition type',{exact:true}).selectOption(code);if(code==='overnight-security')await page.getByLabel('Condition risk target',{exact:true}).selectOption({index:1});if(code==='named-drivers-only')await page.getByRole('checkbox',{name:'Jamie Example',exact:true}).check();await page.getByRole('button',{name:'Add condition',exact:true}).click();}
async function create(page,source,product,stock){const proposal=structuredClone(examples[product]);proposal.termIntent.localStartDate=tomorrow;proposal.risk.business.startedOn='2025-01-01';if(product==='motor-trade-combined'){const section=proposal.cover.requestedSections.find(x=>x.code==='stock-custody');section.limit=stock??'90000.00';}
 const created=(await post(page,'/api/v1/quotes',{relationshipId:source.relationshipId,productVersionId:source.productVersionId,proposal})).data;const route='/api/v1/quotes/'+created.id;let saved=await get(page,route);
 for(const vehicle of proposal.risk.vehicles){const requested=(await post(page,route+'/lookups',{revisionId:saved.data.revisionId,kind:'vehicle',scope:'vehicle',riskItemId:vehicle.id,scenario:'no-match'},saved.etag)).data;const lookup=await until(async()=>(await get(page,route+'/lookups')).data.items.find(x=>x.id===requested.id),x=>x?.state==='no-match');await post(page,route+'/lookup-selections',{lookupId:lookup.id,revisionId:saved.data.revisionId,inputFingerprint:lookup.inputFingerprint,manualReason:'Fictional decision UI fixture'},saved.etag);saved=await get(page,route);}
 await post(page,route+'/rate',{revisionId:saved.data.revisionId,reason:'Fictional decision UI demonstration'},saved.etag);await until(async()=>(await get(page,route+'/underwriting')).data,x=>!!x.ratingId);return created.id;
}
async function ready(page){
 const form=page.getByLabel('Capacity submission message',{exact:true});
 try { await form.waitFor({timeout:5000}); } catch(error) {
  // Versioned evidence paging can be invalidated by concurrent persisted work.
  // Exercise the visible recovery action, never retry a mutation implicitly.
  if(!await page.getByText('This action conflicts with the current quote or command. Your edits are retained.',{exact:true}).count())throw error;
  await page.getByRole('button',{name:'Refresh escalation',exact:true}).click();await form.waitFor();report.pageRefreshRecovery=(report.pageRefreshRecovery??0)+1;
 }
}
async function attachResponse(page,id,submissionId){
 const route='/api/v1/quotes/'+id, assessment=(await get(page,route+'/underwriting')).data;
 const requirement=assessment.proofRequirements.find(x=>x.capacitySubmissionId===submissionId);assert.ok(requirement);
 await page.getByText('Attach and review supporting proof',{exact:true}).click();
 const name=`fictional-carrier-response-${crypto.randomUUID().slice(0,6)}.txt`;
 await page.getByLabel('Underwriting evidence file',{exact:true}).setInputFiles({name,mimeType:'text/plain',buffer:Buffer.from('Fictional capacity letter: stock 150000 GBP, subject to W-07. Business demonstration only.')});
 await page.getByRole('button',{name:'Upload document',exact:true}).click();await confirm(page);await ready(page);
 await page.getByText('Attach and review supporting proof',{exact:true}).click();
 await page.getByLabel(`${requirement.label} · Saved document`,{exact:true}).selectOption({label:name});
 await page.getByLabel(`${requirement.label} · Attachment reason`,{exact:true}).fill('Actual fictional carrier letter for this exact submission');
 const field=page.locator('fieldset').filter({has:page.getByLabel(`${requirement.label} · Saved document`,{exact:true})});await field.getByRole('button',{name:'Attach proof',exact:true}).click();await confirm(page);await ready(page);
 await page.getByText('Attach and review supporting proof',{exact:true}).click();
 const association=(await get(page,route+'/underwriting/evidence')).data.items.find(x=>x.fileName===name);assert.equal(association.capacitySubmissionId,submissionId);
 const card=page.locator(`[data-evidence-id="${association.id}"]`);await card.getByLabel(`Evidence reason for ${name}`,{exact:true}).fill('Reviewed the fictional letter against this submission');await card.getByRole('button',{name:'Record review',exact:true}).click();await confirm(page);await ready(page);return association;
}
if(process.argv.includes('--readback')){
 try{
  const saved=JSON.parse(await readFile(`${output}/report.json`,'utf8')),page=await login('underwriter');
  const combined=saved.journeys.find(x=>x.productCode==='motor-trade-combined');let invalidate=true;
  await page.route(`**/api/v1/quotes/${combined.quoteId}/underwriting/evidence?*`,async route=>{if(invalidate){invalidate=false;await route.fulfill({status:409,contentType:'application/problem+json',body:JSON.stringify({status:409,code:'underwriting-history-changed'})});}else await route.continue();});
  await page.goto(origin+'/escalations/'+combined.escalationId);await page.getByText('This action conflicts with the current quote or command. Your edits are retained.',{exact:true}).waitFor();await page.getByRole('button',{name:'Refresh escalation',exact:true}).click();await ready(page);await page.unroute(`**/api/v1/quotes/${combined.quoteId}/underwriting/evidence?*`);
  assert.ok(await page.getByText('Fictional supplied letter approving stock to GBP 150000 subject to overnight security W-07.',{exact:true}).count());await page.screenshot({path:`${output}/persisted-desktop-viewport.png`});
  await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:`${output}/persisted-mobile-viewport.png`});
  const historical=saved.journeys.find(x=>x.productCode==='motor-trade-road-risks');await page.goto(origin+'/escalations/'+historical.escalationId);await page.getByText('Historical request. Its correspondence is retained; actions use the current quote cycle.',{exact:true}).waitFor();
  saved.readback={persistedSuppliedLetter:true,historicalRequest:true,explicitEvidenceRefreshRecovery:true};await writeFile(`${output}/report.json`,JSON.stringify(saved,null,2));assert.deepEqual(errors,[]);console.log(JSON.stringify(saved.readback));
 }finally{for(const context of contexts)await context.close();await browser.close();}
 process.exit(0);
}
let uw;
try {
 const servicing=await login('servicing');uw=await login('underwriter');
 for(const fixture of ratingFixtures){
  const source=(await get(servicing,'/api/v1/quotes/'+fixture.quoteId)).data;
  const combined=fixture.productCode==='motor-trade-combined',id=await create(servicing,source,fixture.productCode,combined?'150000.00':undefined),route='/api/v1/quotes/'+id;
  const refs=(await get(uw,'/api/v1/referrals?quoteId='+id)).data.items;
  const referral=refs.find(x=>x.ruleCode===(combined?'stock-limit':'UW-22'));assert.ok(referral,JSON.stringify(refs));
  await open(uw,id);const card=uw.locator(`[data-referral-id="${referral.id}"]`);
  await card.getByText('Refer to capacity provider',{exact:true}).click();await card.getByLabel(`Escalation reason for ${referral.ruleCode}`,{exact:true}).fill('Fictional capacity referral for business demonstration');await card.getByRole('button',{name:'Create capacity escalation',exact:true}).click();await confirm(uw);
  await uw.locator(`[data-referral-id="${referral.id}"]`).getByRole('link',{name:'Open capacity escalation',exact:true}).click();await ready(uw);
  const escalationId=new URL(uw.url()).pathname.split('/').at(-1),escRoute='/api/v1/escalations/'+escalationId;
  let view=(await get(uw,escRoute)).data;assert.equal(view.state,'draft');
  await uw.getByRole('tab',{name:'Authority context',exact:true}).click();await uw.getByRole('heading',{name:'Requested cover and retained binder limits',exact:true}).waitFor();assert.ok(view.binderContext.length);await uw.screenshot({path:`${output}/${fixture.productCode}-authority.png`,fullPage:true});await uw.getByRole('tab',{name:'Escalation',exact:true}).click();
  const scenario=view.scenarios.find(x=>x.label.toLowerCase().includes(combined?'approve stock':'query'));assert.ok(scenario,JSON.stringify(view.scenarios));
  await uw.getByLabel('Capacity submission message',{exact:true}).fill('Fictional capacity request.\nPlease review the exact retained scope.');await uw.getByLabel('Capacity demo scenario',{exact:true}).selectOption(scenario.id);
  const attempts=[];let lose=true;await uw.route('**'+escRoute+'/send',async intercepted=>{const r=intercepted.request();attempts.push({body:r.postData(),key:r.headers()['idempotency-key'],etag:r.headers()['if-match']});if(lose){lose=false;const response=await intercepted.fetch();assert.equal(response.status(),202);await intercepted.abort('failed');}else await intercepted.continue();});
  await uw.getByRole('button',{name:'Review submission',exact:true}).click();const dialog=uw.getByRole('dialog');const first=dialog.getByRole('button',{name:'Back to form',exact:true}),last=dialog.getByRole('button',{name:'Confirm action',exact:true});await last.focus();await uw.keyboard.press('Tab');assert.equal(await first.evaluate(x=>document.activeElement===x),true);
  await dialog.getByRole('button',{name:'Confirm action',exact:true}).click();await uw.getByRole('button',{name:'Retry same action',exact:true}).waitFor();await uw.keyboard.press('Escape');assert.equal(await dialog.isVisible(),true);await uw.getByRole('button',{name:'Retry same action',exact:true}).click();await dialog.waitFor({state:'hidden'});assert.deepEqual(attempts[0],attempts[1]);await uw.unroute('**'+escRoute+'/send');
  view=await until(async()=>(await get(uw,escRoute)).data,x=>!!x.currentResponseId);await uw.getByRole('button',{name:'Refresh escalation',exact:true}).click();await ready(uw);assert.equal(view.state,combined?'approved':'queried');assert.equal(view.messages.length,2);assert.equal(view.messages.filter(x=>x.provenance==='demo-provider').length,1);assert.ok(view.attemptHistory.length);
  assert.equal(view.serviceStandard,'2 working days (Monday–Friday)');assert.ok(!['Saturday','Sunday'].includes(new Intl.DateTimeFormat('en-GB',{timeZone:'Europe/London',weekday:'long'}).format(new Date(view.responseDueAt))));await uw.getByText(view.serviceStandard,{exact:true}).waitFor();report.workingDayDeadline=true;
  if(combined){
   const proof=await attachResponse(uw,id,view.currentSubmissionId);view=(await get(uw,escRoute)).data;
   await uw.getByLabel('Capacity response outcome',{exact:true}).selectOption('approve-with-conditions');await uw.getByLabel('Provider underwriter',{exact:true}).fill('Fictional Carrier Underwriter');await uw.getByLabel('Provider reference',{exact:true}).fill('DEMO-CAPACITY-W07');
   await uw.getByLabel('Response received at',{exact:true}).fill(new Date(Date.now()-1000).toISOString().slice(0,19));await uw.getByLabel('Capacity response body',{exact:true}).fill('Fictional supplied letter approving stock to GBP 150000 subject to overnight security W-07.');await uw.getByLabel('Authorised GBP limit',{exact:true}).fill('150000.00');await uw.getByLabel('Authority valid from',{exact:true}).fill(new Date().toISOString().slice(0,10)+'T00:00');await uw.getByLabel('Authority valid until',{exact:true}).fill('2028-01-01T00:00');await addCondition(uw,'overnight-security');await uw.getByLabel('Reviewed response evidence',{exact:true}).selectOption(proof.id);
   await uw.getByRole('button',{name:'Review supplied response',exact:true}).click();await confirm(uw);await ready(uw);view=(await get(uw,escRoute)).data;assert.equal(view.state,'conditional');assert.equal(view.messages.length,3);assert.ok(view.messages.some(x=>x.provenance==='supplied-response'&&x.conditions?.[0].code==='overnight-security'));
   const detail=(await get(uw,'/api/v1/referrals/'+referral.id)).data;assert.ok(detail.conditions.some(x=>x.definition.code==='overnight-security'));report.suppliedResponse={evidenceId:proof.id,conditions:detail.conditions.length};
  }
  await uw.getByLabel('Capacity submission message',{exact:true}).fill('Keep this message after a stale quote update');await uw.getByLabel('Capacity demo scenario',{exact:true}).selectOption(scenario.id);await uw.getByRole('button',{name:'Review submission',exact:true}).click();
  await post(servicing,route+'/underwriting/evidence-files',undefined,(await get(servicing,route)).etag,{fileName:'fictional-concurrent-capacity.txt',contentType:'text/plain',file:{name:'fictional-concurrent-capacity.txt',mimeType:'text/plain',buffer:Buffer.from('Fictional stale capacity UI check')}});
  await uw.getByRole('dialog').getByRole('button',{name:'Confirm action',exact:true}).click();await uw.getByRole('button',{name:'Read current state',exact:true}).click();await uw.getByRole('dialog').getByText(/Current state:/).waitFor();await uw.getByRole('button',{name:'Back to form',exact:true}).click();assert.equal(await uw.getByLabel('Capacity submission message',{exact:true}).inputValue(),'Keep this message after a stale quote update');
  await uw.getByRole('button',{name:'Refresh escalation',exact:true}).click();await ready(uw);await uw.screenshot({path:`${output}/${fixture.productCode}-desktop.png`,fullPage:true});const rail=await uw.locator('.underwriting-rail').boundingBox();assert.ok(Math.abs(rail.width-314)<2);await uw.setViewportSize({width:390,height:844});assert.equal(await uw.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await uw.screenshot({path:`${output}/${fixture.productCode}-mobile.png`,fullPage:true});await uw.setViewportSize({width:1560,height:1000});
  await servicing.goto(origin+'/escalations/'+escalationId);await ready(servicing);assert.equal(await servicing.getByLabel('Capacity submission message',{exact:true}).isDisabled(),true);
  report.journeys.push({quoteId:id,escalationId,productCode:fixture.productCode,checks:['actual referral navigation','retained binder limits','lost committed submission exact retry','uncertain Escape protection','recorded demo outcome and attempts','stale412 retained draft/readback','servicing read without write','314px rail and390px containment']});await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));
 }
 const lastJourney=report.journeys.at(-1),lastRoute='/api/v1/escalations/'+lastJourney.escalationId;
 let lastView=(await get(uw,lastRoute)).data;
 await uw.getByLabel('Capacity submission message',{exact:true}).fill('Retained message after account switch');await uw.getByLabel('Capacity demo scenario',{exact:true}).selectOption(lastView.scenarios[0].id);await uw.getByRole('button',{name:'Review submission',exact:true}).click();
 let token=(await get(uw,'/api/v1/auth/csrf')).data.requestToken;assert.ok((await uw.request.post(origin+'/api/v1/auth/logout',{headers:{'X-CSRF-Token':token}})).ok());
 token=(await get(uw,'/api/v1/auth/csrf')).data.requestToken;assert.ok((await uw.request.post(origin+'/api/v1/auth/login',{headers:{'X-CSRF-Token':token},data:{email:'servicing@cover.example',password}})).ok());
 await uw.getByRole('dialog').getByRole('button',{name:'Confirm action',exact:true}).click();await uw.getByRole('dialog').getByRole('alert').waitFor();await uw.getByRole('button',{name:'Back to form',exact:true}).click();assert.equal(await uw.getByLabel('Capacity submission message',{exact:true}).inputValue(),'Retained message after account switch');assert.equal((await get(uw,lastRoute)).data.messages.length,lastView.messages.length);report.accountSwitch={noWrite:true,draftRetained:true};
 const revised=await login('underwriter');await revised.goto(origin+'/escalations/'+lastJourney.escalationId);await ready(revised);await revised.getByLabel('Revision reason',{exact:true}).fill('Reduce request through an explicit new quote revision');await revised.getByRole('button',{name:'Return quote to draft',exact:true}).click();await confirm(revised);await revised.getByText('Historical request. Its correspondence is retained; actions use the current quote cycle.',{exact:true}).waitFor();await revised.reload();await revised.getByText('Historical request. Its correspondence is retained; actions use the current quote cycle.',{exact:true}).waitFor();lastView=(await get(revised,lastRoute)).data;assert.equal(lastView.current,false);assert.equal(lastView.capabilities.canSend,false);await revised.getByRole('link',{name:/Back to quote/}).click();await revised.waitForURL(origin+'/quotes/'+lastJourney.quoteId);assert.equal((await get(revised,'/api/v1/quotes/'+lastJourney.quoteId)).data.state,'draft');report.revision={historyRetained:true,parentNavigation:true};
 assert.deepEqual(errors,[]);await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report));
}catch(error){if(uw)await uw.screenshot({path:`${output}/failure.png`,fullPage:true});throw error;}finally{for(const context of contexts)await context.close();await browser.close();}



