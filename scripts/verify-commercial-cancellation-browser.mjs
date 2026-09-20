import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';
import {chromium} from 'playwright';
import {underwritingResponseValidator} from './validate-underwriting-response.mjs';

if(!process.argv.includes('--worker')){
 process.argv.push('--stage','issue','--cancellation');await import('./verify-commercial-capture-browser.mjs');
}else{
 const f=JSON.parse(process.env.COVER_COMMERCIAL_BROWSER_FIXTURE);
 for(const origin of [f.apiOrigin,f.webOrigin])assert.ok(['localhost','127.0.0.1','[::1]'].includes(new URL(origin).hostname));
 assert.notEqual(new URL(f.apiOrigin).port,'5000');assert.ok(f.cancellationPolicyId);
 const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1480,height:980}});
 page.setDefaultTimeout(60000);await page.clock.setFixedTime(new Date(f.clockNow));
 const errors=[],commands=[],routes=new Set();let closing=false;
 page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/api/v1/**',route=>{
  const work=(async()=>{try{const url=new URL(route.request().url());const response=await route.fetch({url:f.apiOrigin+url.pathname+url.search,timeout:60000});await route.fulfill({response});}catch(error){if(!closing)errors.push(String(error.message).split('Call log:')[0]);}})();
  routes.add(work);work.finally(()=>routes.delete(work));return work;
 });
 const button=name=>page.getByRole('button',{name,exact:true}),field=name=>page.getByLabel(name,{exact:true});
 async function action(path,method,click,status=201){
  const response=page.waitForResponse(r=>r.url().endsWith(path)&&r.request().method()===method);
  await click();const result=await response;commands.push({path,status:result.status()});
  await writeFile(f.output+'/commercial-cancellation-commands.json',JSON.stringify(commands,null,2));
  assert.equal(result.status(),status);return result.json();
 }
 try{
  await page.goto(f.webOrigin+'/login');await field('Email address').fill('senior-underwriter@cover.example');await field('Password').fill(process.env.COVER_COMMERCIAL_BROWSER_PASSWORD);
  await button('Sign in').click();await page.waitForURL(f.webOrigin+'/');
  const policyResponse=await page.request.get(f.apiOrigin+`/api/v1/policies/${f.cancellationPolicyId}`);assert.equal(policyResponse.status(),200);
  const policy=await policyResponse.json();const original=JSON.stringify(policy.snapshot);
  await page.goto(f.webOrigin+`/policies/${policy.id}`);await button('Make a policy change').click();
  await field('Draft type').selectOption('cancellation');await field('Requested effective date').fill('2026-10-15');await field('Reason for draft').fill('Fictional commercial insured cancellation browser request');
  const created=await action(`/terms/${policy.termId}/drafts`,'POST',()=>button('Create servicing draft').click());
  const draftId=created.id;await page.waitForURL(`**/drafts/${draftId}`);await page.getByRole('heading',{name:'Commercial policy cancellation',exact:true}).waitFor();
  await action('/lease','POST',()=>button('Acquire editing lease').click(),200);
  await field('Cancellation reason').selectOption('insured-request');await action('/proposal','PUT',()=>button('Save draft').click(),200);
  assert.equal(await page.getByRole('heading',{name:'Commercial risk changes',exact:true}).count(),0);
  await field('Cancellation evidence file').setInputFiles({name:'fictional-commercial-cancellation.txt',mimeType:'text/plain',buffer:Buffer.from('Fictional insured request to end commercial cover.')});
  const uploaded=await action('/cancellation-evidence/uploads','POST',()=>button('Upload cancellation evidence').click());
  await field('Cancellation evidence to review').selectOption(uploaded.resourceId);await field('Cancellation evidence review reason').fill('Reviewed the fictional insured commercial cancellation request');
  await action(`/cancellation-evidence/${uploaded.resourceId}/reviews`,'POST',()=>button('Save cancellation evidence review').click());
  await action('/cancellation-preview','POST',()=>button('Retain reviewed preview').click());
  await field('Cancellation approval reason').fill('Approve the reviewed fictional commercial cancellation');
  await action('/cancellation-approvals','POST',()=>button('Approve cancellation').click());
  await field('Cancellation issue reason').fill('Issue commercial cancellation with reviewed credit obligation');
  await page.getByRole('checkbox',{name:'I confirm the cancellation effective time and reviewed financial movement.'}).check();
  const issued=await action('/cancellation-issue','POST',()=>button('Issue approved cancellation').click());
  assert.equal(issued.cashPaid,'0.00');
  await page.getByText('Cancellation issued',{exact:true}).waitFor();
  await page.getByText('Demo delivery recorded',{exact:true}).waitFor();
  assert.equal(await page.getByRole('rowheader',{name:'MID removal',exact:true}).count(),0);
  assert.equal(await page.getByText('Servicing base stale',{exact:true}).count(),0);
  for(const width of [1480,390]){await page.setViewportSize({width,height:980});await page.getByRole('heading',{name:'Issued cancellation',exact:true}).scrollIntoViewIfNeeded();assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1));await page.locator('section.panel').filter({has:page.getByRole('heading',{name:'Issued cancellation',exact:true})}).screenshot({path:f.output+`/commercial-cancellation-${width}.png`});}
  const current=await (await page.request.get(f.apiOrigin+`/api/v1/policies/${policy.id}`)).json();assert.equal(current.versionId,policy.versionId);assert.equal(JSON.stringify(current.snapshot),original);
  const selectedResponse=await page.request.get(f.apiOrigin+`/api/v1/policies/${policy.id}/terms/${policy.termId}/versions/${issued.versionId}`);assert.equal(selectedResponse.status(),200);
  const selected=await selectedResponse.json(),valid=await underwritingResponseValidator('IssuedPolicyView');assert.ok(valid(selected),JSON.stringify(valid.errors));
  assert.equal(selected.snapshot.snapshotFormat,'issued-commercial-cancellation-1');assert.ok(selected.commercialExposureDecisionId);
  for(const key of ['risk','cover','premium','term','insured'])assert.deepEqual(selected.snapshot[key],policy.snapshot[key]);
  await page.getByRole('link',{name:'View cancellation transaction',exact:true}).click();await page.getByRole('heading',{name:'Cancellation transaction',exact:true}).waitFor();
  assert.deepEqual(errors,[]);await writeFile(f.output+'/report.json',JSON.stringify({policyId:policy.id,draftId,versionId:issued.versionId,commands,checks:['Actual CC cancellation reason/evidence/review/approval/issue','Persisted demo notice delivery','Desktop1480/mobile390 receipt','Original cover remains before effective time','Closed cancellation API source and credit history, no MID']},null,2));
 }catch(error){await writeFile(f.output+'/commercial-cancellation-failure.json',JSON.stringify({error:String(error.message).split('Call log:')[0],url:page.url(),alerts:await page.getByRole('alert').allTextContents(),commands},null,2));await page.screenshot({path:f.output+'/commercial-cancellation-failure.png'}).catch(()=>{});throw error;}
 finally{closing=true;await Promise.allSettled([...routes]);await browser.close();}
}
