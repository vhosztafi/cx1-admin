import {chromium} from 'playwright';
import assert from 'node:assert/strict';
import {readFile, mkdir, writeFile} from 'node:fs/promises';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
assert.ok(['127.0.0.1','localhost'].includes(new URL(origin).hostname),'Local fictional demo only');
const output='.local/browser-evidence/underwriting-rating';await mkdir(output,{recursive:true});
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const browser=await chromium.launch({channel:'chrome',headless:true});const contexts=[],errors=[],report={journeys:[]};
const today=new Intl.DateTimeFormat('en-CA',{timeZone:'Europe/London',year:'numeric',month:'2-digit',day:'2-digit'}).format(new Date());
const shift=days=>{const value=new Date(today+'T12:00:00Z');value.setUTCDate(value.getUTCDate()+days);return value.toISOString().slice(0,10);};
async function login(role){const context=await browser.newContext({viewport:{width:1560,height:1000}});contexts.push(context);const page=await context.newPage();page.setDefaultTimeout(25000);page.on('pageerror',error=>errors.push(error.message));await page.goto(origin+'/login');await page.getByLabel('Email address').fill(role+'@cover.example');await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');return page;}
async function get(page,path){const response=await page.request.get(origin+path);assert.equal(response.status(),200,`${path}: ${await response.text()}`);return{data:await response.json(),etag:response.headers().etag};}
async function post(page,path,data,etag,multipart){const csrf=(await get(page,'/api/v1/auth/csrf')).data.requestToken;const response=await page.request.post(origin+path,{headers:{'X-CSRF-Token':csrf,'Idempotency-Key':crypto.randomUUID(),...(etag?{'If-Match':etag}:{})},...(multipart?{multipart}:{data})});assert.ok(response.ok(),`${path}: ${response.status()} ${await response.text()}`);return{data:await response.json(),etag:response.headers().etag};}
async function until(read,accept){const end=Date.now()+40000;let value;do{value=await read();if(accept(value))return value;await new Promise(resolve=>setTimeout(resolve,250));}while(Date.now()<end);assert.fail(JSON.stringify(value));}
async function capture(page,name){await page.evaluate(()=>window.scrollTo({top:0,left:0,behavior:'instant'}));await page.screenshot({path:`${output}/${name}-desktop.png`,fullPage:true});await page.setViewportSize({width:390,height:844});await page.evaluate(()=>window.scrollTo({top:0,left:0,behavior:'instant'}));assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:`${output}/${name}-mobile.png`,fullPage:true});await page.setViewportSize({width:1560,height:1000});}
let page,displayFixture;
try{
 const author=await login('agency-admin'),reviewer=await login('agency-reviewer');page=await login('servicing');
 const oldProducts=(await get(page,'/api/v1/quote-products?relationshipId=51000000-0000-4000-8000-000000000003')).data.items.filter(item=>item.captureEligible);
 assert.equal(oldProducts.length,2);
 const newProducts=(await get(author,'/api/v1/agency-product-catalog')).data.items.filter(item=>item.productCode.startsWith('motor-trade-'));
 assert.equal(newProducts.length,2);assert.ok(newProducts.every(item=>item.distributionEligible));
 const marker=crypto.randomUUID().slice(0,8),details=JSON.parse(await readFile('scripts/fixtures/complete-agency.json','utf8'));
 details.legalName='Fictional Underwriting Demo '+marker;details.relationshipManagerId=(await get(author,'/api/v1/account')).data.id;
 details.commercialTerms.effectiveFrom=shift(-1);details.compliance.tobaSignedOn=shift(-1);details.compliance.piExpiresOn=shift(365);
 const agency=(await post(author,'/api/v1/agencies',{details,onboardingStep:6,products:oldProducts.map(item=>({productVersionId:item.productVersionId,effectiveFrom:shift(-1),brokerCommissionBasisPoints:1250}))})).data;
 const agencyRoute='/api/v1/agencies/'+agency.id;
 const change=async(suffix,data,multipart)=>post(author,agencyRoute+suffix,data,(await get(author,agencyRoute)).etag,multipart);
 await change('/invitations',{displayName:'Fictional underwriting broker administrator',email:`underwriting-${marker}@cover.example`,role:'broker-admin'});
 const file=(await change('/evidence-files',undefined,{fileName:'fictional-underwriting-agency.txt',contentType:'text/plain',file:{name:'fictional-underwriting-agency.txt',mimeType:'text/plain',buffer:Buffer.from('Fictional approved agency evidence; no live transmission.')}})).data;
 for(const kind of ['toba','professional-indemnity','dpa','client-money'])await change('/evidence',{kind,fileId:file.id,notes:'Fictional reviewed agency evidence',...(kind==='professional-indemnity'?{expiresOn:shift(365)}:{})});
 for(const kind of ['fca','financial-check','sanctions','ownership'])await change('/checks',{kind});
 await until(async()=>(await get(author,agencyRoute)).data,item=>item.validation.valid);
 const activation=(await change('/activate',{reason:'Fictional underwriting demonstration activation'})).data;
 await post(reviewer,`/api/v1/agency-state-requests/${activation.id}/decision`,{outcome:'approve',reason:'Independent fictional demonstration approval'},(await get(reviewer,`/api/v1/agency-state-requests/${activation.id}`)).etag);
 const clientName='Fictional Underwriting '+marker;
 const client=await post(page,'/api/v1/clients',{legalName:clientName,entityType:'sole-trader',address:{line1:'1 Example Street',town:'Example Town',postcode:'AB1 2CD',country:'GB'}});
 const relationship=(await post(page,`/api/v1/clients/${client.data.id}/relationships`,{agencyId:agency.id},client.etag)).data;
 const examples=JSON.parse(await readFile('contracts/examples/underwriting-demo.json','utf8')).proposals;
 const quotes=[];
 for(const product of oldProducts){
  const proposal=structuredClone(examples[product.productCode]);proposal.termIntent.localStartDate=shift(1);
  proposal.insured.firstName='Fictional';proposal.insured.surname='Underwriting '+marker;proposal.insured.proposerNames=[clientName];
  const created=(await post(page,'/api/v1/quotes',{relationshipId:relationship.id,productVersionId:product.productVersionId,proposal})).data;
  const route='/api/v1/quotes/'+created.id;let saved=await get(page,route);
  for(const vehicle of proposal.risk.vehicles){const request=(await post(page,route+'/lookups',{revisionId:saved.data.revisionId,kind:'vehicle',scope:'vehicle',riskItemId:vehicle.id,scenario:'no-match'},saved.etag)).data;
   const lookup=await until(async()=>(await get(page,route+'/lookups')).data.items.find(item=>item.id===request.id),item=>item?.state==='no-match');
   await post(page,route+'/lookup-selections',{lookupId:lookup.id,revisionId:saved.data.revisionId,inputFingerprint:lookup.inputFingerprint,manualReason:'Fictional vehicle capture reviewed'},saved.etag);saved=await get(page,route);
  }
  await page.goto(origin+`/quotes/${created.id}/edit`);await page.getByRole('button',{name:'8 Cover & excess',exact:true}).click();
  await page.getByLabel('Tools and equipment choice',{exact:true}).selectOption('true');await page.getByLabel('Tools and equipment limit (£)',{exact:true}).fill('10000');await page.getByLabel('Tools and equipment excess (£)',{exact:true}).fill('250');
  if(product.productCode==='motor-trade-combined'){
   await page.getByLabel('Stock and custody choice',{exact:true}).selectOption('true');await page.getByLabel('Stock and custody limit (£)',{exact:true}).fill('50000');await page.getByLabel('Stock and custody excess (£)',{exact:true}).fill('250');await page.getByLabel('Stock and custody any one vehicle limit (£)',{exact:true}).fill('10000');
   await page.getByLabel('Premises choice',{exact:true}).selectOption('true');await page.getByLabel('Premises limit (£)',{exact:true}).fill('100000');await page.getByLabel('Premises excess (£)',{exact:true}).fill('500');await page.getByRole('group',{name:'Covered trading premises',exact:true}).getByRole('checkbox').first().check();
  }
  const nextRevision=saved.data.revisionNumber+1;await page.getByRole('button',{name:'Save draft',exact:true}).click();await page.getByText(`Draft saved · Revision ${nextRevision}`,{exact:true}).waitFor();saved=await get(page,route);
  assert.equal(saved.data.proposal.cover.requestedSections.find(item=>item.code==='tools-equipment').limit,'10000.00');
  await capture(page,product.productCode+'-cover');quotes.push({id:created.id,route,productCode:product.productCode,oldRevisionId:saved.data.revisionId,oldProposal:saved.data.proposal});
 }
 const terms={effectiveFrom:today,reason:'Adopt published fictional underwriting products',commercialTerms:{...details.commercialTerms,effectiveFrom:today},settlement:details.settlement,paymentTermsDays:details.paymentTermsDays,creditLimit:details.creditLimit,products:newProducts.map(item=>({productVersionId:item.productVersionId,effectiveFrom:today,brokerCommissionBasisPoints:1250}))};
 const request=(await change('/terms-requests',terms)).data;
 await post(reviewer,`/api/v1/agency-terms-requests/${request.id}/decision`,{outcome:'approve',reason:'Independent published-version agreement'},(await get(reviewer,`/api/v1/agency-terms-requests/${request.id}`)).etag);
 for(const quote of quotes){
  await page.goto(origin+'/quotes/'+quote.id);await page.getByRole('button',{name:'Refresh published versions',exact:true}).click();let dialog=page.getByRole('dialog');
  assert.equal(await dialog.getByLabel('Underwriting action reason').evaluate(node=>node===document.activeElement),true);
  const product=newProducts.find(item=>item.productCode===quote.productCode);await dialog.getByLabel('Published version',{exact:true}).selectOption(product.productVersionId);await dialog.getByRole('checkbox').check();await dialog.getByLabel('Underwriting action reason').fill('Adopt approved published version for this quote');await dialog.getByRole('button',{name:'Refresh published versions',exact:true}).click();await dialog.waitFor({state:'hidden'});
  let saved=await get(page,quote.route);assert.notEqual(saved.data.revisionId,quote.oldRevisionId);assert.deepEqual((await get(page,quote.route+'/revisions/'+quote.oldRevisionId)).data.proposal,quote.oldProposal);
  // Published-version adoption deliberately invalidates lookup provenance.
  // Record a fresh manual decision against the new exact revision and product.
  for(const vehicle of saved.data.proposal.risk.vehicles){
   const requested=(await post(page,quote.route+'/lookups',{revisionId:saved.data.revisionId,kind:'vehicle',scope:'vehicle',riskItemId:vehicle.id,scenario:'no-match'},saved.etag)).data;
   const lookup=await until(async()=>(await get(page,quote.route+'/lookups')).data.items.find(item=>item.id===requested.id),item=>item?.state==='no-match');
   await post(page,quote.route+'/lookup-selections',{lookupId:lookup.id,revisionId:saved.data.revisionId,inputFingerprint:lookup.inputFingerprint,manualReason:'Vehicle details reviewed against the adopted published version'},saved.etag);saved=await get(page,quote.route);
  }
  await page.reload();
  const attempts=[];await page.route(`**${quote.route}/rate`,async route=>{attempts.push({body:route.request().postData(),key:route.request().headers()['idempotency-key'],etag:route.request().headers()['if-match']});const response=await route.fetch();assert.equal(response.status(),202,await response.text());if(attempts.length===1)await route.abort('failed');else await route.fulfill({response});});
  await page.getByRole('button',{name:'Rate quote',exact:true}).click();dialog=page.getByRole('dialog');await dialog.getByLabel('Underwriting action reason').fill('Fictional business demonstration rating');await dialog.getByRole('button',{name:'Rate quote',exact:true}).click();await dialog.getByRole('button',{name:'Retry same action',exact:true}).waitFor();await page.keyboard.press('Escape');assert.equal(await dialog.isVisible(),true);assert.equal(await dialog.getByRole('button',{name:'Cancel',exact:true}).isDisabled(),true);await dialog.getByRole('button',{name:'Retry same action',exact:true}).click();await dialog.waitFor({state:'hidden'});assert.deepEqual(attempts[0],attempts[1]);await page.unroute(`**${quote.route}/rate`);
  let assessment=await until(async()=>(await get(page,quote.route+'/underwriting')).data,item=>Boolean(item.ratingId));
  const rating=(await get(page,'/api/v1/ratings/'+assessment.ratingId)).data;assert.equal(rating.applicable,true);assert.ok(rating.factors.some(item=>item.code.includes('tools')));
  await page.reload();await page.getByText('Total payable',{exact:true}).waitFor();assert.equal((await page.locator('.underwriting-rail').boundingBox()).width,314);await page.getByText('Rating factors and provenance',{exact:true}).click();await capture(page,quote.productCode+'-rated');
  await page.getByRole('button',{name:'Re-rate quote',exact:true}).click();dialog=page.getByRole('dialog');await dialog.getByLabel('Underwriting action reason').fill('Re-rate the same saved risk with current configuration');
  for(let index=0;index<6;index++){await page.keyboard.press('Tab');assert.equal(await page.evaluate(()=>Boolean(document.activeElement.closest('dialog'))),true);}
  await dialog.getByRole('button',{name:'Rate quote',exact:true}).click();await dialog.waitFor({state:'hidden'});
  assessment=await until(async()=>(await get(page,quote.route+'/underwriting')).data,item=>Boolean(item.ratingId)&&item.ratingId!==rating.id);
  assert.equal((await get(page,'/api/v1/ratings/'+rating.id)).data.applicable,false);
  const firstHistory=(await get(page,quote.route+'/ratings?pageSize=1')).data;assert.equal(firstHistory.items.length,1);assert.ok(firstHistory.nextCursor);
  const secondHistory=(await get(page,quote.route+'/ratings?pageSize=1&cursor='+encodeURIComponent(firstHistory.nextCursor))).data;assert.equal(secondHistory.items.length,1);assert.notEqual(firstHistory.items[0].id,secondHistory.items[0].id);
  displayFixture={quote:(await get(page,quote.route)).data,assessment,rating:(await get(page,'/api/v1/ratings/'+assessment.ratingId)).data};await page.reload();
  await page.getByRole('button',{name:'Submit for underwriting',exact:true}).click();dialog=page.getByRole('dialog');await dialog.getByLabel('Underwriting action reason').fill('Route outstanding proof to underwriting');await dialog.getByRole('button',{name:'Submit for underwriting',exact:true}).click();await dialog.waitFor({state:'hidden'});await page.getByText('Submitted for underwriting. Outstanding requirements remain below.',{exact:true}).waitFor();
  await page.getByRole('button',{name:'Return to draft',exact:true}).click();dialog=page.getByRole('dialog');await dialog.getByLabel('Underwriting action reason').fill('Retain this reason after concurrent change');
  saved=await get(page,quote.route);await post(page,quote.route+'/submit',{cycleId:assessment.context.cycleId,reason:'Concurrent legitimate routing update'},saved.etag);
  await dialog.getByRole('button',{name:'Return to draft',exact:true}).click();await dialog.getByRole('button',{name:'Read current state',exact:true}).waitFor();assert.equal(await dialog.getByLabel('Underwriting action reason').inputValue(),'Retain this reason after concurrent change');await dialog.getByRole('button',{name:'Read current state',exact:true}).click();await dialog.getByRole('button',{name:'Cancel',exact:true}).click();await page.getByRole('button',{name:'Reload saved quote',exact:true}).click();
  await page.getByRole('button',{name:'Return to draft',exact:true}).click();dialog=page.getByRole('dialog');await dialog.getByLabel('Underwriting action reason').fill('Review the next revision; retain rating history');await dialog.getByRole('button',{name:'Return to draft',exact:true}).click();await dialog.waitFor({state:'hidden'});await page.getByRole('button',{name:'View rating history',exact:true}).click();await page.getByRole('region',{name:'Retained quote rating results',exact:true}).waitFor();
  assert.equal((await get(page,quote.route)).data.state,'draft');assert.equal((await get(page,'/api/v1/ratings/'+rating.id)).data.applicable,false);assert.equal((await get(page,'/api/v1/ratings/'+rating.id)).data.grossPayable,rating.grossPayable);
  await page.getByRole('region',{name:'Retained quote rating results',exact:true}).getByRole('button').first().click();await page.getByRole('heading',{name:'Selected saved rating',exact:true}).waitFor();
  await capture(page,quote.productCode+'-history');
  // Leave a current priced example for the business demo after checking recovery.
  saved=await get(page,quote.route);await post(page,quote.route+'/rate',{revisionId:saved.data.revisionId,reason:'Current fictional business demonstration example'},saved.etag);
  const demo=await until(async()=>(await get(page,quote.route+'/underwriting')).data,item=>Boolean(item.ratingId));
  report.journeys.push({quoteId:quote.id,productCode:quote.productCode,oldRevisionId:quote.oldRevisionId,ratingId:rating.id,demoCurrentRatingId:demo.ratingId,jobId:assessment.jobId,grossPayable:rating.grossPayable,checks:['cover controls persisted','explicit published refresh','immutable old proposal','real provider rating','lost committed response exact retry','uncertain Escape blocked','re-rate supersedes prior result','paged retained results','keyboard focus wraps inside dialog','submission','concurrent stale revision retained','return preserves price history','historical detail loaded','314px rail and390px containment']});
 }
 // Read-only interception fixtures supplement the actual persisted journeys above.
 // They prove presentation of slow/terminal/expired states, not SQL outcomes.
 report.displayFixtures=[];
 for(const state of ['pending','failed','expired']){
  const fixture=structuredClone(displayFixture),path='/api/v1/quotes/'+fixture.quote.id;
  fixture.quote.state=state==='expired'?'rated':'rating-pending';fixture.assessment.state=fixture.quote.state;
  fixture.assessment.capabilities.canRate=state==='expired';fixture.assessment.capabilities.canSubmit=false;fixture.assessment.capabilities.canRevise=true;
  fixture.assessment.blockers=state==='failed'?[{code:'quote-rating-failed',message:'The fictional rating did not complete.'}]:state==='expired'?[{code:'quote-rating-expired',message:'Obtain a current rating.'}]:[];
  if(state!=='expired')delete fixture.assessment.ratingId;else{fixture.rating.expiresAt='2026-01-01T00:00:00Z';fixture.rating.applicable=false;}
  await page.route(`**${path}`,route=>route.fulfill({json:fixture.quote,headers:{etag:fixture.assessment.quoteEtag}}));
  await page.route(`**${path}/underwriting`,route=>route.fulfill({json:fixture.assessment,headers:{etag:fixture.assessment.quoteEtag}}));
  await page.route(`**/api/v1/ratings/${fixture.rating.id}`,route=>route.fulfill({json:fixture.rating}));
  await page.route(`**/api/v1/jobs/${fixture.assessment.jobId}`,route=>route.fulfill({json:{state:state==='failed'?'failed':'pending',attempts:state==='failed'?6:0,attemptLimit:6,retryAllowed:false}}));
  await page.goto(origin+'/quotes/'+fixture.quote.id);
  await page.getByText(state==='pending'?'Rating requested. A premium will appear when the saved result is available.':state==='failed'?'Rating could not complete. Review the attempt and retry.':'This rating has expired. Re-rate before sending or issuing.',{exact:true}).waitFor();
  if(state!=='expired')assert.equal(await page.getByText('Total payable',{exact:true}).count(),0);
  await capture(page,'fixture-'+state);report.displayFixtures.push({state,evidence:'Intercepted read responses only; no mutation or persistence claim'});
  await page.unrouteAll({behavior:'wait'});
 }
 const restricted=await login('agency-admin');assert.equal((await restricted.request.get(origin+quotes[0].route+'/underwriting')).status(),403);
 assert.deepEqual(errors,[]);Object.assign(report,{agencyId:agency.id,clientId:client.data.id,relationshipId:relationship.id,passed:true});await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}catch(error){if(page){await page.screenshot({path:`${output}/failure.png`,fullPage:true}).catch(()=>{});await writeFile(`${output}/failure.txt`,await page.locator('main').innerText()).catch(()=>{});}await writeFile(`${output}/partial-report.json`,JSON.stringify(report,null,2));throw error;}finally{for(const context of contexts)await context.close();await browser.close();}
