import {chromium} from 'playwright';
import assert from 'node:assert/strict';
import {readFile, mkdir, writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const output='.local/browser-evidence/quote-ready';await mkdir(output,{recursive:true});
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const browser=await chromium.launch({channel:'chrome',headless:true}),page=await browser.newPage({viewport:{width:1560,height:1000}});
page.setDefaultTimeout(25000);const report=[],errors=[];page.on('pageerror',e=>errors.push(e.message));
async function get(path){const r=await page.request.get(origin+path);assert.equal(r.status(),200,await r.text());return {data:await r.json(),etag:r.headers().etag};}
async function post(path,data,etag,multipart){const csrf=(await get('/api/v1/auth/csrf')).data.requestToken;const r=await page.request.post(origin+path,{headers:{'X-CSRF-Token':csrf,'Idempotency-Key':crypto.randomUUID(),...(etag?{'If-Match':etag}:{})},...(multipart?{multipart}:{data})});assert.ok(r.ok(),await r.text());return await r.json();}
try{
 await page.goto(`${origin}/login`);await page.getByLabel('Email address').fill('underwriter@cover.example');await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(`${origin}/`);
 const relationshipId='51000000-0000-4000-8000-000000000003',products=(await get(`/api/v1/quote-products?relationshipId=${relationshipId}`)).data.items;assert.equal(products.length,2);
 for(const product of products){
  const proposal=JSON.parse(await readFile(`contracts/examples/quote-capture-${product.productCode}.json`,'utf8')).proposal;
  const created=await post('/api/v1/quotes',{relationshipId,productVersionId:product.productVersionId,proposal});const route=`/api/v1/quotes/${created.id}`;
  let saved=await get(route);assert.equal(saved.data.readiness.ready,false);const originalRevisionId=saved.data.revisionId;
  for(const vehicle of proposal.risk.vehicles){
   const requested=await post(route+'/lookups',{revisionId:saved.data.revisionId,kind:'vehicle',scope:'vehicle',riskItemId:vehicle.id,scenario:'no-match'},saved.etag);
   let lookup;const deadline=Date.now()+25000;
   do {lookup=(await get(route+'/lookups')).data.items.find(x=>x.id===requested.id);if(lookup?.state==='no-match')break;await new Promise(resolve=>setTimeout(resolve,200));}while(Date.now()<deadline);
   assert.equal(lookup?.state,'no-match');
   await post(route+'/lookup-selections',{lookupId:lookup.id,revisionId:saved.data.revisionId,inputFingerprint:lookup.inputFingerprint,manualReason:'Fictional vehicle details reviewed for complete capture'},saved.etag);saved=await get(route);
  }
  const bytes=Buffer.from(`Fictional readiness proof for ${product.productCode}\n`),file=await post(route+'/evidence-files',undefined,saved.etag,{fileName:'fictional-proof.txt',contentType:'text/plain',file:{name:'fictional-proof.txt',mimeType:'text/plain',buffer:bytes}});
  saved=await get(route);const requirements=(await get(route+'/evidence')).data.requirements;assert.ok(requirements.length>0);
  for(const requirement of requirements){await post(route+'/evidence',{revisionId:saved.data.revisionId,requirementCode:requirement.code,...(requirement.riskItemId?{riskItemId:requirement.riskItemId}:{}),fileId:file.id,inputFingerprint:requirement.inputFingerprint,reason:'Fictional evidence reviewed for capture'},saved.etag);saved=await get(route);}
  assert.equal(saved.data.readiness.ready,true,JSON.stringify(saved.data.readiness.issues));assert.deepEqual(saved.data.readiness.issues,[]);
  await page.goto(`${origin}/quotes/${created.id}/edit`);await page.getByText('Capture checks passed.',{exact:true}).waitFor();
  await page.screenshot({path:`${output}/${product.productCode}-desktop.png`,fullPage:true});await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.getByText('Capture checks passed.',{exact:true}).scrollIntoViewIfNeeded();await page.screenshot({path:`${output}/${product.productCode}-mobile.png`});await page.setViewportSize({width:1560,height:1000});
  const downloaded=await page.request.get(origin+route+`/evidence-files/${file.id}/content`);assert.equal(downloaded.status(),200);assert.deepEqual(await downloaded.body(),bytes);
  await page.goto(`${origin}/quotes/${created.id}`);
  for(const tab of ['Risk details','Cover','Drivers','Vehicles']){await page.getByRole('tab',{name:tab,exact:true}).click();await page.getByRole('heading',{name:tab==='Risk details'?'Saved risk details':`Saved ${tab.toLowerCase()}`,exact:true}).waitFor();assert.ok(!(await page.locator('main').innerText()).includes(proposal.risk.vehicles[0].id));}
  await page.screenshot({path:`${output}/${product.productCode}-saved-vehicles-desktop.png`,fullPage:true});await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:`${output}/${product.productCode}-saved-vehicles-mobile.png`,fullPage:true});await page.setViewportSize({width:1560,height:1000});
  const history=(await get(route+'/revisions')).data;assert.ok(history.items.some(x=>x.id===originalRevisionId));
  report.push({id:created.id,reference:saved.data.reference,productCode:product.productCode,revisionId:saved.data.revisionId,originalRevisionId,fileId:file.id,evidenceSha256:createHash('sha256').update(bytes).digest('hex'),lookupIds:(await get(route+'/lookups')).data.items.map(x=>x.id),ready:true});
 }
 assert.deepEqual(errors,[]);await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}catch(error){await page.screenshot({path:`${output}/failure.png`,fullPage:true}).catch(()=>{});throw error;}finally{await browser.close();}
