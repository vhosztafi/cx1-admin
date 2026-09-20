import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';
import {underwritingResponseValidator} from './validate-underwriting-response.mjs';

export async function commercialServicingIssueJourney({page,f,policy,draftId,checks,renewal=false,leaseOwned=false}) {
 const timings=[],requests=[];
 page.on('requestfailed',request=>{if(request.url().includes('/api/v1/'))requests.push({path:new URL(request.url()).pathname,method:request.method(),failure:request.failure()?.errorText});});
 page.on('response',response=>{if(response.url().includes('/api/v1/')&&response.status()>=400){const entry={path:new URL(response.url()).pathname,status:response.status()};requests.push(entry);void response.json().then(body=>{entry.code=body.code??body.type;}).catch(()=>{});}});
 const path=`/api/v1/drafts/${draftId}`,button=name=>page.getByRole('button',{name,exact:true}),field=name=>page.getByLabel(name,{exact:true});
 const get=async suffix=>{const r=await page.request.get(f.apiOrigin+path+suffix,{timeout:90000});assert.equal(r.status(),200,await r.text());return r.json();};
 async function until(suffix,predicate){for(let i=0;i<120;i++){const data=await get(suffix);if(predicate(data))return data;await page.waitForTimeout(250);}throw new Error('Commercial servicing state did not arrive: '+suffix);}
 async function command(suffix,click,status=200,notice=true,recover=true) {
  const pattern='**'+path+suffix;let body,rejectRoute,responseStatus,responseElapsedMs;const started=Date.now(),tasks=new Set();
  const routeFailed=new Promise((_,reject)=>{rejectRoute=reject;});routeFailed.catch(()=>{});
  const capture=route=>{const task=(async()=>{try{if(route.request().method()!=='POST')return await route.fallback();const response=await route.fetch({url:f.apiOrigin+path+suffix,timeout:90000});body=await response.json();responseStatus=response.status();responseElapsedMs=Date.now()-started;await route.fulfill({response,json:body});}catch(error){rejectRoute(new Error(`Commercial command ${suffix} routing failed: ${error.name}`));await route.abort().catch(()=>{});}})();tasks.add(task);task.finally(()=>tasks.delete(task));return task;};
  const aborted=request=>{if(request.method()==='POST'&&new URL(request.url()).pathname===path+suffix)rejectRoute(new Error('Commercial browser command aborted before acknowledgement'));};
  page.on('requestfailed',aborted);await page.route(pattern,capture);
  try{const ack=page.waitForResponse(r=>r.url().endsWith(path+suffix)&&r.request().method()==='POST');ack.catch(()=>{});await click();assert.equal((await Promise.race([ack,routeFailed])).status(),status,JSON.stringify(body));if(notice)await page.getByText('Supporting information saved.',{exact:true}).waitFor();return body;}
  catch(error){await Promise.all([...tasks]);const retry=button(suffix==='/issue'?'Retry same issue':'Retry same proof action');if(recover&&responseStatus===status&&await retry.isVisible()){await page.unroute(pattern,capture);page.off('requestfailed',aborted);timings.push({path:suffix,responseElapsedMs,responseStatus,recoveredViaExactProofRetry:suffix!=='/issue',recoveredViaExactIssueRetry:suffix==='/issue'});return await command(suffix,()=>retry.click(),status,notice,false);}await writeFile(f.output+'/commercial-servicing-action-failure.json',JSON.stringify({path:suffix,bodyReceived:body!==undefined,responseStatus,responseElapsedMs,requests,alerts:await page.getByRole('alert').allTextContents(),statuses:await page.getByRole('status').allTextContents(),text:await page.locator('main').innerText(),buttons:await page.getByRole('button').evaluateAll(nodes=>nodes.map(node=>({text:node.textContent,disabled:node.matches(':disabled')})))},null,2));throw error;}
  finally{page.off('requestfailed',aborted);await page.unroute(pattern,capture);await Promise.all([...tasks]);timings.push({path:suffix,elapsedMs:Date.now()-started,responseElapsedMs,responseStatus});await writeFile(f.output+'/commercial-servicing-commands.json',JSON.stringify(timings,null,2));}
 }
 if(!leaseOwned){await page.goto(f.webOrigin+`/drafts/${draftId}`);await button('Acquire editing lease').click();}
 await command('/rate',()=>button(renewal?'Rate saved renewal':'Rate saved adjustment').click(),202,false);
 const rating=await until('/ratings',x=>x.current?.applicable);assert.equal(rating.current.result.fee,renewal?'45.00':'25.00');
 await field('Supporting document').setInputFiles({name:'fictional-commercial-adjustment.txt',mimeType:'text/plain',buffer:Buffer.from('Fictional commercial adjustment evidence, signed terms and acceptance for business demonstration only.')});
 const upload=await command('/evidence/uploads',()=>button('Upload document').click(),201);
 async function proof(required){
  const group=page.locator(`[data-requirement-code="${required.code}"][data-risk-item-id="${required.riskItemId??''}"]`);
  await group.getByLabel('Saved document',{exact:true}).selectOption(upload.id);await group.getByLabel('Attachment reason',{exact:true}).fill('Fictional exact commercial adjustment proof');
  const attached=await command('/evidence',()=>group.getByRole('button',{name:'Attach proof',exact:true}).click(),201);
  const card=page.locator(`[data-evidence-id="${attached.id}"]`);await card.getByLabel('Review outcome',{exact:true}).selectOption('accepted');await card.getByLabel('Review or withdrawal reason',{exact:true}).fill('Reviewed fictional commercial evidence for this exact rated change');
  await command(`/evidence/${attached.id}/reviews`,()=>card.getByRole('button',{name:'Record review',exact:true}).click());return attached.id;
 }
 for(const item of (await get('/evidence/requirements')).requirements)if(!item.satisfied)await proof(item.requirement);
 const referrals=await get('/referrals');assert.equal(referrals.nextCursor,null);
 for(const row of referrals.items.filter(x=>['AU-05','AU-06'].includes(x.ruleCode))){
  const card=page.locator(`[data-referral-id="${row.id}"]`);await card.getByText('Carrier capacity request',{exact:true}).click();
  await card.getByLabel('Capacity request reason',{exact:true}).fill('Fictional commercial carrier permission for saved adjustment');
  const raised=await command('/capacity',()=>card.getByRole('button',{name:'Create carrier request',exact:true}).click(),201);
  const cases=await get(`/capacity?referralId=${row.id}&pageSize=50`);const caseId=cases.items.find(x=>x.id===raised.id)?.id??cases.items[0].id;
  const detail=await get(`/capacity/${caseId}`);const scenario=detail.scenarios.find(x=>x.label==='cc-approve-requested');assert.ok(scenario);
  await card.getByLabel('Demo carrier scenario',{exact:true}).selectOption(scenario.id);
  await card.getByLabel('Carrier message',{exact:true}).fill('Fictional commercial carrier review of the saved complete schedule');await card.getByLabel('Carrier message reason',{exact:true}).fill('Request fictional commercial numerical permission');
  await command(`/capacity/${caseId}/submissions`,()=>card.getByRole('button',{name:'Submit carrier request',exact:true}).click(),202);
  await until(`/capacity/${caseId}`,x=>x.case.state==='approved');
 }
 for(const row of referrals.items)await page.locator(`[data-referral-id="${row.id}"]`).getByRole('checkbox').first().check();
 if(referrals.items.length){await field('Decision reason').fill('Approve fictional commercial adjustment with reviewed proof and carrier permission');await command(referrals.items.length===1?`/referrals/${referrals.items[0].id}/decisions`:'/referrals/decisions',()=>button(`Record decision for ${referrals.items.length} selected`).click());}
 const prepared=await command('/terms/prepare',()=>button(renewal?'Prepare renewal invitation':'Prepare servicing terms').click(),201);
 await proof((await get('/evidence/requirements')).requirements.find(x=>x.requirement.code==='signed-statement'&&x.requirement.termsVersionId===prepared.id).requirement);
 const panel=page.getByRole('region',{name:renewal?'Renewal invitation and acceptance':'Servicing terms and acceptance',exact:true});await panel.getByRole('group',{name:'Send prepared terms',exact:true}).getByRole('checkbox').first().check();
 await command('/terms/send',()=>button(renewal?'Send renewal invitation':'Send servicing terms').click(),202);await until('/terms',x=>x.delivery?.state==='delivered');
 const acceptedProof=await proof((await get('/evidence/requirements')).requirements.find(x=>x.requirement.code==='acceptance-proof').requirement);
 const acceptedLocal=await page.evaluate(value=>{const date=new Date(value);const local=new Date(date.getTime()-date.getTimezoneOffset()*60000).toISOString().slice(0,19);return local.endsWith(':00')?local.slice(0,-3):local;},f.clockNow);
 await field('Accepted by').fill('Fictional commercial business customer');await field('Acceptance received at').fill(acceptedLocal);await field('Reviewed acceptance proof').selectOption(acceptedProof);
 await command('/acceptances',()=>button(renewal?'Record renewal acceptance':'Record servicing acceptance').click(),201);const accepted=await get('/terms');assert.equal(accepted.acceptanceApplicable,true);assert.equal(Date.parse(accepted.acceptance.acceptedAt),Date.parse(f.clockNow));
 await field('Issue reason').fill('Issue the accepted fictional commercial adjustment');await field('I confirm the accepted changes and effective dates.').check();
 const receipt=await command('/issue',()=>button(renewal?'Issue accepted renewal':'Issue accepted adjustment').click(),201,false);
 await page.getByRole('article',{name:renewal?'Renewal issue receipt':'Adjustment issue receipt',exact:true}).waitFor();assert.deepEqual(receipt.midIntentIds,[]);
 const response=await page.request.get(f.apiOrigin+`/api/v1/policies/${policy.id}/terms/${receipt.termId}/versions/${receipt.versionIds.at(-1)}`);assert.equal(response.status(),200,await response.text());const issued=await response.json();
 const validate=await underwritingResponseValidator('IssuedPolicyView');assert.ok(validate(issued),JSON.stringify(validate.errors));
 assert.equal(issued.snapshot.snapshotFormat,'issued-commercial-servicing-1');assert.equal(issued.transactionId,receipt.transactionId);assert.ok(issued.commercialExposureDecisionId);assert.equal(issued.financials.fee,renewal?'45.00':'25.00');
 const old=await (await page.request.get(f.apiOrigin+`/api/v1/policies/${policy.id}/terms/${policy.termId}/versions/${policy.versionId}`)).json();assert.equal(old.contentHash,policy.contentHash);assert.deepEqual(old.snapshot,policy.snapshot);
 for(const width of [1480,390]){await page.setViewportSize({width,height:980});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);await page.getByRole('article',{name:renewal?'Renewal issue receipt':'Adjustment issue receipt',exact:true}).scrollIntoViewIfNeeded();await page.screenshot({path:f.output+`/commercial-servicing-issue-${width}.png`});}
 await page.setViewportSize({width:1480,height:980});await page.goto(f.webOrigin+`/policies/${policy.id}?termId=${receipt.termId}&versionId=${receipt.versionIds.at(-1)}`);
 await writeFile(f.output+'/commercial-servicing-issue.json',JSON.stringify({draftId,receipt,issued},null,2));
 checks.push((renewal?'Commercial renewal: ':'Commercial adjustment: ')+'actual UI rate, reviewed commercial proof, carrier permission, internal decisions, signed terms, fictional delivery, acceptance and issue; immutable prior policy, new exposure decision, one product-specific fee, no MID, desktop/mobile receipt');
}
if(process.argv[1]?.endsWith('verify-commercial-servicing-issue-browser.mjs')){process.argv.push('--stage','issue','--servicing-issue');await import('./verify-commercial-capture-browser.mjs');}
