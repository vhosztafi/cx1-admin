import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const password=(await readFile('.local/demo-password.txt','utf8')).trim(),refs=JSON.parse(await readFile('contracts/reference-data/motor-trade-capture.json','utf8'));
const output='.local/browser-evidence/quote-driver-options';await mkdir(output,{recursive:true});
const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1560,height:1000}});page.setDefaultTimeout(20000);
const errors=[];page.on('pageerror',error=>errors.push(error.message));const report=[];
const field=name=>page.getByLabel(name,{exact:true}),button=name=>page.getByRole('button',{name,exact:true});
const csrf=async()=>(await(await page.request.get(`${origin}/api/v1/auth/csrf`)).json()).requestToken;
async function read(id){const response=await page.request.get(`${origin}/api/v1/quotes/${id}`);assert.equal(response.status(),200);return{data:await response.json(),etag:response.headers().etag};}
async function save(id){const before=await read(id);await button('Save draft').click();await page.getByText(`Draft saved · Revision ${before.data.revisionNumber+1}`,{exact:true}).waitFor();return(await read(id)).data;}
const answer=(p,n)=>p.risk.drivers[0].responses.answers.find(row=>row.questionId===`MTS-06-Q${n}`)?.value;
const reference=(collection,value)=>({collection,value,version:refs.version,label:refs.collections[collection].find(row=>row.value===value).text});
try{
 await page.goto(`${origin}/quotes/new`);await page.waitForURL('**/login');await field('Email address').fill('servicing@cover.example');await field('Password').fill(password);await button('Sign in').click();await page.waitForURL(`${origin}/`);
 const relationshipId='51000000-0000-4000-8000-000000000003';const products=(await(await page.request.get(`${origin}/api/v1/quote-products?relationshipId=${relationshipId}`)).json()).items;
 for(const product of products){
  const proposal=JSON.parse(await readFile(`contracts/examples/quote-capture-${product.productCode}.json`,'utf8')).proposal;
  const created=await page.request.post(`${origin}/api/v1/quotes`,{headers:{'X-CSRF-Token':await csrf(),'Idempotency-Key':crypto.randomUUID()},data:{relationshipId,productVersionId:product.productVersionId,proposal}});assert.equal(created.status(),201,await created.text());const id=(await created.json()).id;
  const stage=product.productCode==='motor-trade-combined'?5:4;const drivers=()=>button(`${stage} Drivers`).click();
  await page.goto(`${origin}/quotes/${id}/edit`);await drivers();
  await field('Driver 1 · Date of Birth').fill('2008-09-15');await field('Driver 1 · Driving Licence Issue').fill('2025-09-15');
  const excess='Driver 1 · Remove the age-related additional excess',indemnity='Driver 1 · Young driver indemnity limit',cc='Driver 1 · Young driver engine capacity limit';
  await field(excess).selectOption('false');await field(indemnity).selectOption({label:'£10,000'});await field(cc).selectOption({label:'1600cc'});
  let stored=await save(id);assert.equal(answer(stored.proposal,58),false);assert.equal(answer(stored.proposal,59).collection,'youngDriverConfiguration/0/indemnities');assert.equal(answer(stored.proposal,60).collection,'youngDriverConfiguration/0/cCs');
  assert.equal(stored.readiness.issues.some(issue=>issue.code==='driver-option-answer-required'),false);
  await page.reload();await drivers();assert.equal(await field(cc).inputValue(),'1');
  await field('Driver 1 · Date of Birth').fill('2004-09-15');assert.equal(await field(cc).inputValue(),'-1');assert.equal(await field(indemnity).inputValue(),'-1');
  stored=await save(id);assert.equal(answer(stored.proposal,60).collection,'youngDriverConfiguration/0/cCs');
  assert.ok(stored.readiness.issues.some(issue=>issue.code==='reference-collection-mismatch'&&issue.questionId==='MTS-06-Q60'));
  await button(`Review ${cc}`).click();await page.waitForFunction(label=>document.activeElement?.getAttribute('aria-label')===label,cc);
  await field(cc).selectOption({label:'1800cc'});await field(indemnity).selectOption({label:'£10,000'});stored=await save(id);assert.equal(answer(stored.proposal,60).collection,'youngDriverConfiguration/2/cCs');
  await field('Driver 1 · Date of Birth').fill('2001-09-15');assert.equal(await field(cc).count(),0);stored=await save(id);assert.ok(answer(stored.proposal,60));
  await button(`Review Clear ${cc}`).first().click();await page.waitForFunction(label=>document.activeElement?.getAttribute('aria-label')===`Clear ${label}`,cc);
  for(const label of [excess,indemnity,cc])await button(`Clear ${label}`).click();stored=await save(id);for(const n of [58,59,60])assert.equal(answer(stored.proposal,n),undefined);
  await field('Driver 1 · Driving Licence Issue').fill('2026-09-14');
  const experience='Driver 1 · Inexperienced driver excess',engine='Driver 1 · Inexperienced driver engine capacity limit';
  await field(experience).selectOption('0');await field(engine).selectOption('0');stored=await save(id);assert.equal(answer(stored.proposal,61).collection,'driverExperienceBasedExcesses');
  await field('Driver 1 · Driving Licence Issue').fill('2025-09-15');stored=await save(id);assert.ok(stored.readiness.issues.some(issue=>issue.code==='inactive-driver-option-retained'&&issue.questionId==='MTS-06-Q61'));
  await button(`Clear ${experience}`).click();await button(`Clear ${engine}`).click();await save(id);
  const plan='Driver basis · Driver Plan';await field(plan).selectOption({label:refs.collections.driverPlans.find(row=>row.value===3).text});
  await field('Driver basis · Number of Drivers').fill('2');
  const restrictions=['Minimum Driver Age','Maximum Driver Age','Maximum vehicle grouping','Maximum Gross Vehicle Weight Handled','Maximum Motorcycle CC Handled'];for(const label of restrictions)await field(`Driver basis · ${label}`).selectOption('0');
  stored=await save(id);const driverId=stored.proposal.risk.drivers[0].id;assert.ok(stored.readiness.issues.some(issue=>issue.code==='inactive-named-drivers-retained'));
  await field(plan).selectOption({label:refs.collections.driverPlans.find(row=>row.value===2).text});stored=await save(id);assert.equal(stored.proposal.risk.drivers[0].id,driverId);assert.equal(stored.readiness.issues.some(issue=>issue.code==='inactive-named-drivers-retained'),false);
  await field(plan).selectOption({label:refs.collections.driverPlans.find(row=>row.value===1).text});stored=await save(id);assert.equal(stored.readiness.issues.filter(issue=>issue.code==='inactive-any-driver-answer-retained').length,6);
  await field('Driver basis · Number of Drivers').fill('');for(const label of restrictions)await field(`Driver basis · ${label}`).selectOption('');await save(id);
  const latest=await read(id);latest.data.proposal.cover.responses.answers.find(row=>row.questionId==='MTS-05-Q02').value=reference('indemnityOwnVehicles',2);latest.data.proposal.risk.drivers[0].dateOfBirth='2008-09-15';
  const lower=await page.request.put(`${origin}/api/v1/quotes/${id}/proposal`,{headers:{'X-CSRF-Token':await csrf(),'Idempotency-Key':crypto.randomUUID(),'If-Match':latest.etag},data:{proposal:latest.data.proposal}});assert.equal(lower.status(),200);
  await page.reload();await drivers();assert.equal(await field(indemnity).count(),0);assert.ok(await field(cc).count());
  await page.evaluate(()=>window.scrollTo(0,0));await page.screenshot({path:`${output}/${product.productCode}-desktop.png`,fullPage:true});
  await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:`${output}/${product.productCode}-mobile.png`});
  stored=(await read(id)).data;report.push({id,reference:stored.reference,productCode:product.productCode,revision:stored.revisionNumber,checks:['live per-driver age bands','exact pinned option families and limits','retained stale selections and exact field focus','explicit clear after age25','licence anniversary eligibility','named/additional/any-driver populations','empty eligible indemnity family','390px containment']});await page.setViewportSize({width:1560,height:1000});
 }
 assert.equal(report.length,2);assert.deepEqual(errors,[]);await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}catch(error){await page.screenshot({path:`${output}/failure.png`,fullPage:true});await writeFile(`${output}/failure.txt`,await page.locator('main').innerText());throw error;}finally{await browser.close();}
