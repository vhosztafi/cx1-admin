import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
const api=JSON.parse(await readFile('contracts/openapi.json','utf8')),ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);
const schema=JSON.parse(JSON.stringify({$defs:Object.fromEntries(['QuoteEvidenceFile','QuoteCaptureEvidence','QuoteEvidenceRequirement','QuoteEvidenceSnapshot'].map(name=>[name,api.components.schemas[name]])),$ref:'#/$defs/QuoteEvidenceSnapshot'}).replaceAll('#/components/schemas/','#/$defs/'));
const valid=ajv.compile(schema),origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const password=(await readFile('.local/demo-password.txt','utf8')).trim(),output='.local/browser-evidence/quote-evidence';await mkdir(output,{recursive:true});
const browser=await chromium.launch({channel:'chrome',headless:true}),page=await browser.newPage({viewport:{width:1560,height:1000}});page.setDefaultTimeout(25000);
const errors=[],report=[];page.on('pageerror',error=>errors.push(error.message));
const field=name=>page.getByLabel(name,{exact:true}),button=name=>page.getByRole('button',{name,exact:true});
const csrf=async()=>(await(await page.request.get(`${origin}/api/v1/auth/csrf`)).json()).requestToken;
async function quote(id){const response=await page.request.get(`${origin}/api/v1/quotes/${id}`);assert.equal(response.status(),200);return{data:await response.json(),etag:response.headers().etag};}
async function evidence(id){const response=await page.request.get(`${origin}/api/v1/quotes/${id}/evidence`);assert.equal(response.status(),200);const data=await response.json();assert.ok(valid(data),JSON.stringify(valid.errors));return data;}
async function settled(){await page.getByText('Evidence action recorded. Saved requirements refreshed.',{exact:true}).waitFor();await button('Upload evidence file').isEnabled();await page.getByText('Loading saved evidence…',{exact:true}).waitFor({state:'hidden'});}
try {
 await page.goto(`${origin}/quotes/new`);await page.waitForURL('**/login');await field('Email address').fill('servicing@cover.example');await field('Password').fill(password);await button('Sign in').click();await page.waitForURL(`${origin}/`);
 const relationshipId='51000000-0000-4000-8000-000000000003';const products=(await(await page.request.get(`${origin}/api/v1/quote-products?relationshipId=${relationshipId}`)).json()).items;
 for(const product of products){
  const proposal=JSON.parse(await readFile(`contracts/examples/quote-capture-${product.productCode}.json`,'utf8')).proposal;
  const response=await page.request.post(`${origin}/api/v1/quotes`,{headers:{'X-CSRF-Token':await csrf(),'Idempotency-Key':crypto.randomUUID()},data:{relationshipId,productVersionId:product.productVersionId,proposal}});assert.equal(response.status(),201,await response.text());const id=(await response.json()).id;
  await page.goto(`${origin}/quotes/${id}/edit`);const initial=await evidence(id);const business=initial.requirements.find(item=>item.code==='motor-trader-proof');assert.equal(business.state,'missing');
  const bytes=Buffer.from(`Fictional Motor Trade evidence for ${product.productCode}.\r\n`),filename='fictional-trade-proof.txt';
  await field('Evidence file').setInputFiles({name:filename,mimeType:'text/plain',buffer:bytes});
  const attempts=[];let first=true;await page.route(`**/api/v1/quotes/${id}/evidence-files`,async route=>{
   if(route.request().method()!=='POST')return route.continue();
   attempts.push({key:route.request().headers()['idempotency-key'],etag:route.request().headers()['if-match'],contains:route.request().postDataBuffer().includes(bytes)});
   if(first){first=false;const result=await route.fetch();assert.equal(result.status(),201,await result.text());await route.abort('failed');}else await route.continue();
  });
  await button('Upload evidence file').click();await button('Retry same evidence action').click();await settled();await page.unroute(`**/api/v1/quotes/${id}/evidence-files`);assert.equal(attempts.length,2);assert.deepEqual(attempts[0],attempts[1]);assert.equal(attempts[0].contains,true);
  const files=(await(await page.request.get(`${origin}/api/v1/quotes/${id}/evidence-files`)).json()).items;assert.equal(files.length,1);assert.equal(files[0].sha256,createHash('sha256').update(bytes).digest('hex'));
  await field(`${business.label} · Saved document`).selectOption(files[0].id);await field(`${business.label} · Attachment reason`).fill('Fictional proof reviewed for business demo');
  const attachAttempts=[];first=true;await page.route(`**/api/v1/quotes/${id}/evidence`,async route=>{
   if(route.request().method()!=='POST')return route.continue();attachAttempts.push({body:route.request().postData(),key:route.request().headers()['idempotency-key'],etag:route.request().headers()['if-match']});
   if(first){first=false;const result=await route.fetch();assert.equal(result.status(),201,await result.text());await route.abort('failed');}else await route.continue();
  });
  await button('Attach business proof').click();await button('Retry same evidence action').click();await settled();await page.unroute(`**/api/v1/quotes/${id}/evidence`);assert.deepEqual(attachAttempts[0],attachAttempts[1]);assert.equal(attachAttempts.length,2);
  let snapshot=await evidence(id);assert.equal(snapshot.items.length,1);assert.equal(snapshot.items[0].state,'current');assert.equal((await quote(id)).data.readiness.issues.some(item=>item.code==='evidence-missing-motor-trader-proof'),false);
  await page.reload();await page.getByText('Current evidence attached',{exact:true}).waitFor();
  const downloadPromise=page.waitForEvent('download');await page.locator('[data-evidence-id]').getByRole('link',{name:filename,exact:true}).click();const download=await downloadPromise;assert.equal(download.suggestedFilename(),filename);assert.deepEqual(await readFile(await download.path()),bytes);
  const downloaded=await page.request.get(`${origin}/api/v1/quotes/${id}/evidence-files/${files[0].id}/content`);assert.equal(downloaded.headers()['x-content-type-options'],'nosniff');assert.match(downloaded.headers()['cache-control'],/no-store/);
  await field(`Withdrawal reason for ${filename}`).fill('Replace this fictional demonstration proof');await button('Withdraw attachment').click();await settled();snapshot=await evidence(id);assert.equal(snapshot.items[0].state,'withdrawn');assert.equal(snapshot.requirements.find(item=>item.code===business.code).state,'missing');
  await field(`${business.label} · Saved document`).selectOption(files[0].id);await field(`${business.label} · Attachment reason`).fill('Reattached for input-change demonstration');await button('Attach business proof').click();await settled();
  await button('2 Proposer').click();await field('First name').fill('Revised fictional proposer');assert.equal(await button('Attach business proof').isDisabled(),true);assert.equal(await button('Upload evidence file').isDisabled(),true);
  await button('Save draft').click();await page.getByText('Draft saved · Revision 2',{exact:true}).waitFor();snapshot=await evidence(id);assert.ok(snapshot.items.some(item=>item.state==='stale'));assert.ok(snapshot.items.some(item=>item.state==='withdrawn'));
  await page.getByText('The relevant saved answers changed. Review and attach proof against the current requirement.',{exact:true}).waitFor();
  assert.equal(await page.locator('.quote-create-rail').evaluate(node=>Math.round(node.getBoundingClientRect().width)),314);
  await page.getByTestId('quote-evidence').screenshot({path:`${output}/${product.productCode}-desktop.png`});await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.getByTestId('quote-evidence').screenshot({path:`${output}/${product.productCode}-mobile.png`});await page.setViewportSize({width:1560,height:1000});
  const saved=(await quote(id)).data;report.push({id,reference:saved.reference,productCode:product.productCode,revision:saved.revisionNumber,checks:['real bytes upload and SHA256','lost upload and attachment response replay','current evidence clears missing requirement only','reload and protected attachment download','withdrawal reason and retained history','saved input change makes proof stale','unsaved input disables evidence writes','live snapshot matches closed API schema','314px rail and390px containment']});
 }
 assert.equal(report.length,2);assert.deepEqual(errors,[]);await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}catch(error){await page.screenshot({path:`${output}/failure.png`,fullPage:true}).catch(()=>{});await writeFile(`${output}/failure.txt`,await page.locator('main').innerText());throw error;}finally{await browser.close();}
