import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {randomUUID} from 'node:crypto';
import {underwritingResponseValidator} from './validate-underwriting-response.mjs';

export async function commercialServicingJourney({page,f,policy,checks}) {
 const validateDraft=await underwritingResponseValidator('ServicingDraft'),validateEditor=await underwritingResponseValidator('ServicingEditor');
 const button=name=>page.getByRole('button',{name,exact:true}),field=name=>page.getByLabel(name,{exact:true});
 const group=name=>page.getByRole('navigation',{name:'Commercial change groups'}).getByRole('button',{name,exact:true}).click();
 const exposureUrl=f.apiOrigin+`/api/v1/policies/${policy.id}/commercial-exposure?`+new URLSearchParams({effectiveAt:policy.snapshot.term.startsAt,knownAt:f.clockNow});
 const exposureBefore=await (await page.request.get(exposureUrl)).json();
 await page.goto(f.webOrigin+`/policies/${policy.id}`);await button('Make a policy change').click();
 await field('Draft type').waitFor();assert.deepEqual(await field('Draft type').locator('option').evaluateAll(xs=>xs.map(x=>x.value)),['adjustment','renewal','cancellation']);
 await field('Requested effective date').fill('2026-10-01');await field('Reason for draft').fill('Fictional commercial browser adjustment');
 const created=page.waitForResponse(r=>r.url().endsWith(`/terms/${policy.termId}/drafts`)&&r.request().method()==='POST');
 await button('Create servicing draft').click();assert.equal((await created).status(),201);
 await page.waitForURL(/\/drafts\/[0-9a-f-]{36}$/);const draftId=new URL(page.url()).pathname.split('/').at(-1),url=`/api/v1/drafts/${draftId}`;
 const read=async()=>{const r=await page.request.get(f.apiOrigin+url);assert.equal(r.status(),200,await r.text());const data=await r.json();assert.ok(validateDraft(data),JSON.stringify(validateDraft.errors));return {data,etag:r.headers().etag};};
 const editor=async()=>{const r=await page.request.get(f.apiOrigin+url+'/editor');assert.equal(r.status(),200,await r.text());const data=await r.json();assert.ok(validateEditor(data),JSON.stringify(validateEditor.errors));return data;};
 async function acquire(){
  // This navigation is rendered only after the hydrated editor has loaded its
  // current base. Do not start the response deadline while the page is loading.
  await page.getByRole('navigation',{name:'Commercial change groups'}).waitFor();
  await button('Acquire editing lease').and(page.locator(':enabled')).waitFor();
  const [ack]=await Promise.all([
   page.waitForResponse(r=>r.url().endsWith(url+'/lease')&&r.request().method()==='POST'),
   button('Acquire editing lease').click(),
  ]);
  assert.equal(ack.status(),200);await field('Reason for change').and(page.locator(':enabled')).waitFor();
 }
 async function save(){const ack=page.waitForResponse(r=>r.url().endsWith(url+'/proposal')&&r.request().method()==='PUT');await button('Save draft').click();const r=await ack;assert.equal(r.status(),200,await r.text());const saved=await r.json();await button('Save draft').and(page.locator(':enabled')).waitFor();const e=await editor();assert.equal(e.revisionId,saved.revisionId);assert.ok(e.assessment.slices.length);assert.deepEqual(e.assessment.slices.at(-1).proposed,e.assessment.proposed);return e.assessment.proposed;}
 async function command(method,suffix,view,body,key=randomUUID()) {const csrf=await (await page.request.get(f.apiOrigin+'/api/v1/auth/csrf')).json();return page.request.fetch(f.apiOrigin+url+suffix,{method,headers:{'X-CSRF-Token':csrf.requestToken,'Idempotency-Key':key,'If-Match':view.etag,'X-Edit-Lease':view.data.lease.leaseToken},...(body===undefined?{}:{data:body})});}
 await acquire();assert.equal((await read()).data.context.productCode,'commercial-combined');
 await group('Business & proposer');await field('Trading as').fill('Fictional servicing trade');await field('Business or trade description').fill('Revised fictional commercial warehouse');await field('VAT registered').selectOption('no');
 let proposed=await save();assert.equal(proposed.insured.tradingName,'Fictional servicing trade');assert.equal(proposed.risk.business.vatRegistered,false);
 await group('Claims & losses');await button('Add loss').click();await field('Loss date').fill('2025-06-01');await field('Loss type').selectOption('1');await field('Loss location').selectOption(policy.snapshot.risk.locations[0].id);await field('Loss amount').fill('10');await field('Paid amount').fill('0');await field('Outstanding reserve').fill('10');await field('Loss status').selectOption('open');await field('Loss circumstances').fill('Fictional servicing loss');await field('Was it insured').selectOption('no');await button('Apply loss details').click();
 proposed=await save();assert.equal(proposed.risk.losses[0].amount,'10.00');
 await group('Claims & losses');await button('Edit loss 1').click();await field('Loss circumstances').fill('Revised fictional servicing loss');await button('Apply loss details').click();assert.equal((await save()).risk.losses[0].description,'Revised fictional servicing loss');
 await group('Claims & losses');page.once('dialog',x=>x.accept());await button('Remove loss 1').click();assert.deepEqual((await save()).risk.losses,[]);
 await group('Locations');await button('Edit location 1').click();await field('Contents sum insured').fill('27111.12');await button('Apply location details').click();proposed=await save();assert.equal(proposed.risk.locations[0].contents,'27111.12');assert.equal(proposed.risk.locations[1].contents,policy.snapshot.risk.locations[1].contents);
 await group('Locations');await button('Add location').click();await field('Location reference').fill('Temporary proposed location');await field('Location address line 1').fill('3 Fictional servicing road');await field('Location town').fill('London');await field('Location postcode').fill('SW1A 1AA');await field('Buildings sum insured').fill('0');await field('Contents sum insured').fill('0');await field('Stock sum insured').fill('0');await button('Apply location details').click();assert.equal((await save()).risk.locations.length,3);
 await group('Locations');page.once('dialog',x=>x.accept());await button('Remove location 3').click();assert.equal((await save()).risk.locations.length,2);
 const questions=JSON.parse(await readFile('contracts/quote-question-catalogue.json','utf8')).deferredQuestions;
 for(const [name,stage] of [['Construction & protections',5],['Flood & subsidence',6],['Health & safety',9]]) {
  await group(name);const q=questions.find(q=>q.stages.includes(`Commercial Combined:step-${stage}`)&&!q.targetContainer.includes('[]')&&['reference','boolean'].includes(q.kind));
  assert.ok(q,`${name} question`);const options=await field(q.label).locator('option').evaluateAll(xs=>xs.map(x=>x.value).filter(Boolean));await field(q.label).selectOption(options.at(-1));await save();await page.reload();await acquire();await group(name);assert.equal(await field(q.label).inputValue(),options.at(-1));
 }
 await group('Property & BI');await field('Business interruption sum insured').fill('123456.78');assert.equal((await save()).risk.businessInterruption.sumInsured,'123456.78');
 await group('Property & BI');await field('Business interruption required').selectOption('no');page.once('dialog',x=>x.accept());await button('Clear retained BI details').click();proposed=await save();assert.ok(!proposed.risk.businessInterruption||Object.keys(proposed.risk.businessInterruption).length===0);
 await group('Liability & wages');await field('Public liability limit').selectOption('2000000.00');await button('Edit wage category 1').click();await field('Employee wages').fill('77777.77');await button('Apply wage details').click();proposed=await save();assert.equal(proposed.risk.liability.publicLimit,'2000000.00');assert.equal(proposed.risk.wages[0].employees,'77777.77');
 await group('Liability & wages');await button('Add wage category').click();await field('Wage category').selectOption('clerical');await field('Employee wages').fill('0');await button('Apply wage details').click();assert.equal((await save()).risk.wages.length,3);
 await group('Liability & wages');page.once('dialog',x=>x.accept());await button('Remove wage category 3').click();assert.equal((await save()).risk.wages.length,2);
 await group('Cover & declarations');await field('Contract works sum insured').fill('15000.01');await field('Other material facts').fill('Fictional proposed material fact');proposed=await save();assert.equal(proposed.cover.contractWorks.sumInsured,'15000.01');assert.equal(proposed.risk.materialFacts,'Fictional proposed material fact');
 await group('Cover & declarations');await field('Contract works required').selectOption('no');page.once('dialog',x=>x.accept());await button('Clear retained contract works details').click();assert.equal((await save()).cover.contractWorks.sumInsured,undefined);
 await page.reload();await acquire();await group('Business & proposer');assert.equal(await field('Trading as').inputValue(),'Fictional servicing trade');
 // Actual HTTP ownership/schema/date negatives and exact optimistic-concurrency failure.
 let current=await read();const bad=structuredClone(current.data.proposal);bad.changes[0].riskItemId=randomUUID();assert.equal((await command('PUT','/proposal',current,bad)).status(),422);
 const motor=structuredClone(current.data.proposal);motor.changes=[{changeId:randomUUID(),riskItemId:randomUUID(),kind:'driver',operation:'update',payload:{}}];assert.equal((await command('PUT','/proposal',current,motor)).status(),422);
 const duplicate=structuredClone(current.data.proposal);duplicate.changes.push({...structuredClone(duplicate.changes[0]),changeId:randomUUID()});assert.equal((await command('PUT','/proposal',current,duplicate)).status(),422);
 const stale=current;const remote=structuredClone(current.data.proposal);remote.reason='Another saved fictional browser revision';assert.equal((await command('PUT','/proposal',current,remote)).status(),200);assert.equal((await command('PUT','/proposal',stale,remote)).status(),412);
 await page.reload();await acquire();await field('Reason for change').fill('Local edits survive a competing revision');current=await read();const competing=structuredClone(current.data.proposal);competing.reason='Concurrent saved revision to review';assert.equal((await command('PUT','/proposal',current,competing)).status(),200);
 await page.getByRole('alert').filter({hasText:'Another revision was saved'}).waitFor();assert.equal(await field('Reason for change').inputValue(),'Local edits survive a competing revision');assert.equal(await button('Save draft').isDisabled(),true);
 page.once('dialog',x=>x.accept());await button('Discard local changes').click();assert.equal(await field('Reason for change').inputValue(),competing.reason);
 await field('Reason for change').fill('Local edits survive a revoked lease');current=await read();assert.equal((await command('DELETE','/lease',current)).status(),200);await page.getByRole('heading',{name:'Read-only draft',exact:true}).waitFor();assert.equal(await field('Reason for change').inputValue(),'Local edits survive a revoked lease');assert.equal(await button('Save draft').isDisabled(),true);await acquire();await save();
 await field('Reason for change').fill('Fictional committed save with a lost response');
 const intercepted=[];const pattern='**'+url+'/proposal';
 await page.route(pattern,async route=>{intercepted.push({body:route.request().postData(),key:route.request().headers()['idempotency-key'],etag:route.request().headers()['if-match']});if(intercepted.length===1){const committed=await route.fetch({url:f.apiOrigin+url+'/proposal'});assert.equal(committed.status(),200,await committed.text());await route.abort('failed');}else if(intercepted.length===2)await route.fulfill({status:403,contentType:'application/problem+json',body:JSON.stringify({status:403,title:'Current access denied',code:'servicing-draft-denied'})});else await route.fallback();});
 await button('Save draft').click();await button('Retry same action').waitFor();await button('Retry same action').click();await page.waitForFunction(()=>!Array.from(document.querySelectorAll('button')).find(x=>x.textContent==='Retry same action')?.disabled);assert.equal(intercepted.length,2);await button('Retry same action').click();await button('Retry same action').waitFor({state:'hidden'});assert.equal(intercepted.length,3);assert.deepEqual(intercepted[1],intercepted[0]);assert.deepEqual(intercepted[2],intercepted[0]);await page.unroute(pattern);
 current=await read();assert.equal(current.data.proposal.reason,'Fictional committed save with a lost response');const e=await editor();assert.equal(e.revisionId,current.data.revisionId);
 const retained=await (await page.request.get(f.apiOrigin+`/api/v1/policies/${policy.id}`)).json();assert.deepEqual(retained,policy);assert.deepEqual(await (await page.request.get(exposureUrl)).json(),exposureBefore);
 for(const width of [1480,390]) {await page.setViewportSize({width,height:980});await group('Locations');assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);await page.screenshot({path:f.output+`/commercial-servicing-${width}.png`,fullPage:true});await page.evaluate(()=>scrollTo(0,0));await page.screenshot({path:f.output+`/commercial-servicing-${width}-viewport.png`});}
 await page.setViewportSize({width:1480,height:980});await writeFile(f.output+'/commercial-servicing.json',JSON.stringify({draftId,proposal:current.data.proposal,assessment:e.assessment,issuedHash:policy.contentHash},null,2));
 if(f.servicingIssue){const {commercialServicingIssueJourney}=await import('./verify-commercial-servicing-issue-browser.mjs');await commercialServicingIssueJourney({page,f,policy,draftId,checks});}
 checks.push('Actual CC adjustment creation; nine editor groups; stable location/wage/loss add-edit-remove and explicit BI/cover clearing; exact saved slices; foreign/motor/stale HTTP rejection; retained local edits after concurrent revision and lease revocation; committed lost-response retry preserves exact body/key/ETag; issued snapshot and district exposure unchanged');
}
if(process.argv[1]?.endsWith('verify-commercial-servicing-editor-browser.mjs')) {process.argv.push('--stage','issue','--servicing');await import('./verify-commercial-capture-browser.mjs');}
