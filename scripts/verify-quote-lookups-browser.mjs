import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
const api=JSON.parse(await readFile('contracts/openapi.json','utf8')),ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);
const lookupSchema=JSON.parse(JSON.stringify({$defs:{QuoteLookupView:api.components.schemas.QuoteLookupView,QuoteLookupCandidate:api.components.schemas.QuoteLookupCandidate},$ref:'#/$defs/QuoteLookupView'}).replaceAll('#/components/schemas/','#/$defs/'));
const validLookup=ajv.compile(lookupSchema);
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const password=(await readFile('.local/demo-password.txt','utf8')).trim(),output='.local/browser-evidence/quote-lookups';await mkdir(output,{recursive:true});
const browser=await chromium.launch({channel:'chrome',headless:true}),page=await browser.newPage({viewport:{width:1560,height:1000}});page.setDefaultTimeout(25000);
const errors=[];page.on('pageerror',error=>errors.push(error.message));const report=[];
const field=name=>page.getByLabel(name,{exact:true}),button=name=>page.getByRole('button',{name,exact:true,includeHidden:true});
const csrf=async()=>(await(await page.request.get(`${origin}/api/v1/auth/csrf`)).json()).requestToken;
async function read(id){const response=await page.request.get(`${origin}/api/v1/quotes/${id}`);assert.equal(response.status(),200);return{data:await response.json(),etag:response.headers().etag};}
async function outcome(id,scenario,kind='address',scope='insured'){
 for(let n=0;n<120;n++){
  const response=await page.request.get(`${origin}/api/v1/quotes/${id}/lookups`);assert.equal(response.status(),200);
  const item=(await response.json()).items.find(item=>item.scenario===scenario&&item.kind===kind&&item.scope===scope);
  if(item){assert.ok(validLookup(item),JSON.stringify(validLookup.errors));if(item.state!=='pending')return item;}
  await page.waitForTimeout(250);
 }throw new Error('Lookup did not reach a terminal outcome.');
}
async function request(label,scenario){await field(`${label} · Demo outcome`).selectOption(scenario);await button(`Look up ${label.toLowerCase()}`).click();await page.getByText('Lookup queued. Check the result before making a decision.',{exact:true}).waitFor();}
try{
 await page.goto(`${origin}/quotes/new`);await page.waitForURL('**/login');await field('Email address').fill('servicing@cover.example');await field('Password').fill(password);await button('Sign in').click();await page.waitForURL(`${origin}/`);
 const relationshipId='51000000-0000-4000-8000-000000000003';const products=(await(await page.request.get(`${origin}/api/v1/quote-products?relationshipId=${relationshipId}`)).json()).items.filter(product => product.captureEligible);
 for(const product of products){
  const proposal=JSON.parse(await readFile(`contracts/examples/quote-capture-${product.productCode}.json`,'utf8')).proposal;
  proposal.insured.address={...proposal.insured.address,postcode:'AB1 2CD'};
  proposal.risk.drivers[0].licence.number='DEMO123456789';
  proposal.risk.drivers[0].address={postcode:'AB1 2CD'};
  const created=await page.request.post(`${origin}/api/v1/quotes`,{headers:{'X-CSRF-Token':await csrf(),'Idempotency-Key':crypto.randomUUID()},data:{relationshipId,productVersionId:product.productVersionId,proposal}});assert.equal(created.status(),201,await created.text());const id=(await created.json()).id;
  await page.goto(`${origin}/quotes/${id}/edit`);
  await request('Proposer address','multiple');const multiple=await outcome(id,'multiple');assert.equal(multiple.candidates.length,2);
  await button('Use proposer address match 1').click();await page.getByText('Lookup decision recorded · Revision 2',{exact:true}).waitFor();
  assert.equal((await read(id)).data.proposal.insured.address.street,'Fictional Demo Street');
  await page.reload();await page.getByText('This decision has been recorded in quote history.',{exact:true}).waitFor();
  await request('Proposer address','no-match');assert.equal((await outcome(id,'no-match')).state,'no-match');
  await field('Proposer address · Manual decision reason').fill('Fictional address checked manually for this demo');
  const intercepted=[];let first=true;
  await page.route(`**/api/v1/quotes/${id}/lookup-selections`,async route=>{
   intercepted.push({body:route.request().postData(),key:route.request().headers()['idempotency-key'],etag:route.request().headers()['if-match']});
   if(first){first=false;const response=await route.fetch();assert.equal(response.status(),200);await route.abort('failed');}else await route.continue();
  });
  await button('Record manual proposer address decision').click();await button('Retry same lookup').click();await page.getByText('Lookup decision recorded · Revision 3',{exact:true}).waitFor();await page.unroute(`**/api/v1/quotes/${id}/lookup-selections`);assert.deepEqual(intercepted[0],intercepted[1]);
  for(const scenario of ['reject','fail-once','timeout-after-success']){
   await request('Proposer address',scenario);
   let retainedStreet;
   if(scenario==='fail-once'){
    const pending=(await(await page.request.get(`${origin}/api/v1/quotes/${id}/lookups`)).json()).items.find(item=>item.scenario===scenario);
    assert.equal(pending.state,'pending');retainedStreet=await field('Street').inputValue();await field('Street').fill('Local edit while lookup pending');
    assert.equal(await button('Look up proposer address').isDisabled(),true);
   }
   const result=await outcome(id,scenario);
   assert.equal(result.state,scenario==='reject'?'rejected':'succeeded');if(scenario!=='reject')assert.equal(result.attempts,2);
   if(retainedStreet!==undefined){assert.equal((await read(id)).data.proposal.insured.address.street,retainedStreet);await field('Street').fill(retainedStreet);}
  }
  await page.reload();await button('Use proposer address match 1').waitFor();
  await field('Street').fill('Unsaved address edit');assert.equal(await button('Use proposer address match 1').isDisabled(),true);assert.equal(await button('Look up proposer address').isDisabled(),true);
  await button('Save draft').click();await page.getByText('Draft saved · Revision 4',{exact:true}).waitFor();assert.equal(await button('Use proposer address match 1').isDisabled(),true);
  await page.getByText('The saved inputs have changed. Request a lookup for the current revision.',{exact:true}).waitFor();
  const combined=product.productCode==='motor-trade-combined';
  await button(combined?'7 Vehicles & trade plates':'6 Vehicles & trade plates').click();
  await request('Vehicle 1','success');await outcome(id,'success','vehicle','vehicle');
  await button('Use vehicle 1 match 1').click();await page.getByText('Lookup decision recorded · Revision 5',{exact:true}).waitFor();
  assert.equal((await read(id)).data.proposal.risk.vehicles[0].make,'Demo Motors');
  assert.equal((await read(id)).data.readiness.issues.some(issue=>issue.code==='vehicle-capture-context-required'),false);
  await button(combined?'5 Drivers':'4 Drivers').click();
  await request('Driver 1 licence','success');await outcome(id,'success','licence','driver');
  await button('Use driver 1 licence match 1').click();await page.getByText('Lookup decision recorded · Revision 6',{exact:true}).waitFor();
  await request('Driver 1 address','success');await outcome(id,'success','address','driver');
  await button('Use driver 1 address match 1').click();await page.getByText('Lookup decision recorded · Revision 7',{exact:true}).waitFor();
  if(combined){
   await button('4 Premises').click();await request('Premises 1 address','success');await outcome(id,'success','address','premises');
   await button('Use premises 1 address match 1').click();await page.getByText('Lookup decision recorded · Revision 8',{exact:true}).waitFor();
  }
  await button('2 Proposer').click();
  assert.equal(await page.locator('.quote-create-rail').evaluate(node=>Math.round(node.getBoundingClientRect().width)),314);
  await page.screenshot({path:`${output}/${product.productCode}-desktop.png`,fullPage:true});await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:`${output}/${product.productCode}-mobile.png`,fullPage:true});await page.setViewportSize({width:1560,height:1000});
  const saved=(await read(id)).data;report.push({id,reference:saved.reference,productCode:product.productCode,revision:saved.revisionNumber,checks:['multiple selection and reload','no-match manual decision','lost selection response exact replay','rejection retained','fail-once and timeout recovery','unsaved/stale input cannot apply candidates','vehicle and driver licence/address selection','premises address selection for Combined','actual responses match API schema','314px rail and390px containment']});
 }
 assert.equal(report.length,2);assert.deepEqual(errors,[]);await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}catch(error){await page.screenshot({path:`${output}/failure.png`,fullPage:true}).catch(()=>{});await writeFile(`${output}/failure.txt`,await page.locator('main').innerText());throw error;}finally{await browser.close();}
