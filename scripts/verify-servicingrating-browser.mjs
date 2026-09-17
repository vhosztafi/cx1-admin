import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {chromium} from 'playwright';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['127.0.0.1','localhost'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/servicing-rating';await mkdir(output,{recursive:true});
const fixtures=JSON.parse(await readFile('.local/browser-evidence/underwriting-issue/report.json','utf8')).journeys;
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1560,height:1000}});page.setDefaultTimeout(30000);
const errors=[];page.on('pageerror',error=>errors.push(error.message));const report={startedAt:new Date().toISOString(),journeys:[]};
const button=name=>page.getByRole('button',{name,exact:true}),field=name=>page.getByLabel(name,{exact:true});
async function get(path){const r=await page.request.get(origin+path);assert.equal(r.status(),200,await r.text());assert.match(r.headers()['cache-control']??'',/no-store/);return r.json();}
async function save(){await button('Save draft').click();await page.getByText('Draft action saved.',{exact:true}).waitFor();}
try{
 await page.goto(origin+'/login');await field('Email address').fill('servicing@cover.example');await field('Password').fill(password);await button('Sign in').click();await page.waitForURL(origin+'/');
 for(const fixture of fixtures){
  await page.setViewportSize({width:1560,height:1000});const before=await get(`/api/v1/policies/${fixture.policyId}`);
  await page.goto(origin+`/policies/${fixture.policyId}`);await field('Draft type').selectOption('adjustment');await field('Requested effective date').fill('2026-10-01');await field('Reason for draft').fill('Fictional rating browser adjustment');await button('Create servicing draft').click();await page.waitForURL(/\/drafts\//);
  const draftId=page.url().split('/').at(-1),path=`/api/v1/drafts/${draftId}`;
  await writeFile(`${output}/active.json`,JSON.stringify({draftId,policyId:fixture.policyId,reason:'Fictional rating browser adjustment'}));
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await field('Cover to change').selectOption('tools-equipment');await button('Amend cover').click();
  await field('Tools and equipment choice').selectOption('true');await field('Tools and equipment limit (£)').fill('5000.00');await field('Tools and equipment excess (£)').fill('250.00');await button('Apply to draft').click();
  assert.equal(await button('Rate saved adjustment').isDisabled(),true);
  await field('Effective date basis').selectOption('per-cover-change');await field('Use an individual cover date').check();await field('Cover change 1 date (London)').fill('2026-10-15');
  // Add the common-date full cover state; the later change must remain a distinct cumulative slice.
  await field('Cover to change').selectOption('all');await button('Amend cover').click();await field('Tools and equipment choice').selectOption('false');await button('Apply to draft').click();await save();
  const editor=await get(path+'/editor');assert.deepEqual(editor.assessment.readinessIssues,[]);
  await button('Rate saved adjustment').click();await page.getByText('Rating requested. Results will appear when processing finishes.',{exact:true}).waitFor();await page.getByText('Rated',{exact:true}).waitFor();
  let history=await get(path+'/ratings');assert.equal(history.current.applicable,true);assert.equal(history.current.result.fee,'15.00');assert.equal(history.items.length,1);assert.equal(history.current.result.slices.length,2);
  const cents=value=>Math.round(Number(value)*100),amount=history.current.result;
  assert.equal(cents(amount.grossPayable),cents(amount.premium)+cents(amount.tax)+1500);
  assert.equal(cents(amount.premium),amount.slices.reduce((total,slice)=>total+cents(slice.premium),0));
  assert.equal(cents(amount.tax),amount.slices.reduce((total,slice)=>total+cents(slice.tax),0));
  const first=history.current.id;
  let firstRequest;
  await page.route('**'+path+'/rate',async route=>{
   firstRequest={body:route.request().postData(),key:route.request().headers()['idempotency-key'],etag:route.request().headers()['if-match']};
   const committed=await route.fetch();assert.equal(committed.status(),202);await route.abort('failed');
  },{times:1});
  await button('Re-rate saved adjustment').click();await button('Retry same action').waitFor();
  assert.equal(await button('Save draft').isDisabled(),true);
  const retried=page.waitForRequest(request=>request.url().endsWith(path+'/rate'));
  await button('Retry same action').click();const retryRequest=await retried;
  assert.equal(retryRequest.postData(),firstRequest.body);assert.equal(retryRequest.headers()['idempotency-key'],firstRequest.key);assert.equal(retryRequest.headers()['if-match'],firstRequest.etag);
  await page.getByText('Rating requested. Results will appear when processing finishes.',{exact:true}).waitFor();
  for(let i=0;i<100;i++){history=await get(path+'/ratings');if(history.current.id!==first&&history.current.applicable)break;await page.waitForTimeout(100);}
  assert.notEqual(history.current.id,first);assert.equal(history.current.applicable,true);assert.equal(history.items.length,2);assert.equal(history.items.find(x=>x.id===first).state,'superseded');
  assert.equal(history.current.result.fee,'15.00');
  await page.reload();await page.getByText('Rated',{exact:true}).waitFor();await page.getByRole('region',{name:'Dated rating slices',exact:true}).first().waitFor();
  await page.screenshot({path:`${output}/${fixture.productCode}-desktop.png`,fullPage:true});
  await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
  assert.equal(await page.getByRole('region',{name:'Dated rating slices',exact:true}).first().evaluate(el=>el.scrollWidth>el.clientWidth),true);await page.screenshot({path:`${output}/${fixture.productCode}-mobile.png`,fullPage:true});
  await page.locator('section.panel').filter({has:page.getByRole('heading',{name:'Review & rate',exact:true})}).screenshot({path:`${output}/${fixture.productCode}-rating-panel.png`});
  assert.deepEqual((await get(`/api/v1/policies/${fixture.policyId}`)).snapshot,before.snapshot);
  // Close only the draft created by this journey; all cycles remain durable history.
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await field('Takeover or abandonment reason').fill('Fictional rating browser journey complete');await field('I confirm this draft should be abandoned.').check();await button('Abandon draft').click();await page.getByText('Abandoned',{exact:true}).waitFor();
  history=await get(path+'/ratings');assert.equal(history.current,null);assert.ok(history.items.every(x=>!x.applicable));
  report.journeys.push({productCode:fixture.productCode,policyId:fixture.policyId,draftId,cycleIds:history.items.map(x=>x.id),checks:['dirty proposal blocked','actual UI rate and rerate','lost response retries identical command once','SQL-backed current result','two cumulative dated slices','one fee and component totals','superseded history','reload persistence','unchanged issued snapshot','mobile containment','abandon preserves history']});
 }
 assert.deepEqual(errors,[]);report.completedAt=new Date().toISOString();await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log('Both Motor Trade servicing rating browser journeys passed.');
}finally{await browser.close();}
