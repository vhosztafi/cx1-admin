import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const password=(await readFile('.local/demo-password.txt','utf8')).trim(),output='.local/browser-evidence/quote-vehicles';await mkdir(output,{recursive:true});
const browser=await chromium.launch({channel:'chrome',headless:true}),page=await browser.newPage({viewport:{width:1560,height:1000}});page.setDefaultTimeout(20000);
const errors=[];page.on('pageerror',error=>errors.push(error.message));const report=[];
const field=name=>page.getByLabel(name,{exact:true}),button=name=>page.getByRole('button',{name,exact:true});
const csrf=async()=>(await(await page.request.get(`${origin}/api/v1/auth/csrf`)).json()).requestToken;
async function read(id){const response=await page.request.get(`${origin}/api/v1/quotes/${id}`);assert.equal(response.status(),200);return{data:await response.json(),etag:response.headers().etag};}
async function save(id){const before=await read(id);await button('Save draft').click();await page.getByText(`Draft saved · Revision ${before.data.revisionNumber+1}`,{exact:true}).waitFor();return(await read(id)).data;}
async function openVehicle(n){const card=page.locator('.quote-driver-section > details').nth(n-1);if(!await card.evaluate(node=>node.open))await card.locator(':scope > summary').click();}
async function openSection(name){const summary=page.getByText(name,{exact:true}).filter({hasNot:page.locator('option')});const details=summary.locator('..');if(!await details.evaluate(node=>node.open))await summary.click();}
try{
 await page.goto(`${origin}/quotes/new`);await page.waitForURL('**/login');await field('Email address').fill('servicing@cover.example');await field('Password').fill(password);await button('Sign in').click();await page.waitForURL(`${origin}/`);
 const relationshipId='51000000-0000-4000-8000-000000000003';const products=(await(await page.request.get(`${origin}/api/v1/quote-products?relationshipId=${relationshipId}`)).json()).items.filter(product => product.captureEligible);
 for(const product of products){
  const proposal=JSON.parse(await readFile(`contracts/examples/quote-capture-${product.productCode}.json`,'utf8')).proposal;
  const created=await page.request.post(`${origin}/api/v1/quotes`,{headers:{'X-CSRF-Token':await csrf(),'Idempotency-Key':crypto.randomUUID()},data:{relationshipId,productVersionId:product.productVersionId,proposal}});assert.equal(created.status(),201,await created.text());const id=(await created.json()).id;
  const stage=product.productCode==='motor-trade-combined'?7:6;const vehicles=()=>button(`${stage} Vehicles & trade plates`).click();
  await page.goto(`${origin}/quotes/${id}/edit`);await vehicles();await button('Add vehicle').click();await openVehicle(2);
  await field('Vehicle 2 · Vehicle Registration').fill('XZ12 DEM');await field('Vehicle 2 · Make').fill('Fictional Motors');await field('Vehicle 2 · Model').fill('Demo Vehicle');
  await field('Vehicle 2 · Vehicle register').selectOption({label:'held for sale'});await field('Vehicle 2 · Ownership purpose').selectOption({label:'stock for sale'});
  await field('Vehicle 2 · Vehicle Value').fill('50000.01');await field('Vehicle 2 · Purchase price').fill('12345.67');await field('Vehicle 2 · Purchase date').fill('2026-09-01');
  await field('Vehicle 2 · Gross vehicle weight (kg)').fill('3500');await field('Vehicle 2 · Engine capacity (cc)').fill('1998');
  await field('Vehicle 2 · Year manufactured').fill('2020');await field('Vehicle 2 · Registration Year').fill('2021');await field('Vehicle 2 · First registration date').fill('2021-03-01');
  await field('Vehicle 2 · Length of Lease (years)').fill('2.5');await field('Vehicle 2 · Report to the motor insurance database').selectOption('false');
  await field('Are specified vehicles required?').selectOption('true');await field('Vehicle 2 · Specifically insured').check();
  let stored=await save(id);const added=stored.proposal.risk.vehicles[1].id,original=stored.proposal.risk.vehicles[0].id;
  assert.equal(stored.proposal.risk.vehicles[1].value,'50000.01');assert.equal(stored.proposal.risk.vehicles[1].leaseLengthYears,2.5);assert.equal(stored.proposal.risk.vehicles[1].engineCc,1998);assert.deepEqual(stored.proposal.risk.specifiedVehicleIds,[added]);
  await button('Move vehicle 2 up').click();stored=await save(id);assert.deepEqual(stored.proposal.risk.vehicles.map(v=>v.id),[added,original]);await page.reload();await vehicles();
  assert.equal(await field('Vehicle 1 · Purchase price').inputValue(),'12345.67');assert.equal(await field('Vehicle 1 · Registration Year').inputValue(),'2021');assert.equal(await field('Vehicle 1 · Year manufactured').inputValue(),'2020');
  await button('Remove vehicle 1').click();await page.getByText('Clear the specified selection before removing this vehicle.',{exact:true}).waitFor();await field('Vehicle 1 · Specifically insured').uncheck();
  await field('Vehicle 1 · Purchase price').fill('12.345');assert.equal(await button('Save draft').isDisabled(),true);await button('2 Proposer').click();await vehicles();assert.equal(await field('Vehicle 1 · Purchase price').inputValue(),'12.345');await field('Vehicle 1 · Purchase price').fill('0');
  await button('Add modification for vehicle 1').click();await field('Vehicle 1 · Modification 1 · What modifications have been made?').selectOption('0');stored=await save(id);const modification=stored.proposal.risk.vehicles[0].modifications[0].id;
  await page.reload();await vehicles();assert.equal((await read(id)).data.proposal.risk.vehicles[0].modifications[0].id,modification);await button('Remove modification 1 for vehicle 1').click();await save(id);
  await openSection('Vehicle portfolio and proportions');await field('Vehicle portfolio · Percentage of Standard Cars').fill('99.99');stored=await save(id);assert.ok(stored.readiness.issues.some(i=>i.code==='portfolio-total-must-equal-100-percent'));
  await field('Vehicle portfolio · Percentage of Standard Cars').fill('100');await save(id);
  await openSection('Trade plates');await button('Add held trade plates').click();await field('Held trade plates 1 · Plate number').fill('123 AB');await button('Add covered trade plates').click();await field('Covered trade plates 1 · Plate number').fill('123AB');
  stored=await save(id);const held=stored.proposal.risk.heldTradePlates[0].id,covered=stored.proposal.risk.tradePlates[0].id;assert.notEqual(held,covered);
  await page.reload();await vehicles();await openSection('Trade plates');assert.equal(await field('Held trade plates 1 · Plate number').inputValue(),'123 AB');await button('Remove covered trade plates 1').click();stored=await save(id);assert.equal(stored.proposal.risk.tradePlates.length,0);assert.equal(stored.proposal.risk.heldTradePlates[0].id,held);
  await button('Review Vehicle 1 · ABI Code').first().click();await page.waitForFunction(()=>document.activeElement?.getAttribute('aria-label')==='Vehicle 1 · ABI Code');await field('Vehicle 1 · ABI Code').fill('DEMO-ABI');await field('Vehicle 1 · Declared body category').selectOption('0');await save(id);
  await button(`${stage-2} Drivers`).click();await field('Driver 1 · Does this driver require cover for Personally Owned Vehicles?').selectOption('true');await save(id);await vehicles();
  await field('Vehicle 1 · Vehicle Owner').selectOption({label:'Named driver'});await field('Vehicle 1 · Named driver owner').selectOption({index:1});stored=await save(id);assert.equal(stored.proposal.risk.vehicles[0].ownerDriverId,stored.proposal.risk.drivers[0].id);
  await button(`${stage-2} Drivers`).click();await button('Remove driver 1').click();await page.getByText('Update the retained vehicle owner before removing this driver.',{exact:true}).waitFor();await field('Driver 1 · Full name').fill('Jamie Owner Demo');await save(id);await vehicles();
  await field('Vehicle 1 · Named driver owner').selectOption('');await field('Vehicle 1 · Vehicle Owner').selectOption({index:1});await save(id);
  const before=await read(id);await field('Vehicle 1 · Make').fill('Saved after lost response');const pending=[];let first=true;
  await page.route(`**/api/v1/quotes/${id}/proposal`,async route=>{pending.push({body:route.request().postData(),key:route.request().headers()['idempotency-key'],etag:route.request().headers()['if-match']});if(first){first=false;const response=await route.fetch();assert.equal(response.status(),200);await route.abort('failed');}else await route.continue();});
  await button('Save draft').click();await button('Retry same save').click();await page.getByText(`Draft saved · Revision ${before.data.revisionNumber+1}`,{exact:true}).waitFor();await page.unroute(`**/api/v1/quotes/${id}/proposal`);assert.deepEqual(pending[0],pending[1]);
  const concurrent=await read(id);await field('Vehicle 1 · Make').fill('Local vehicle make');concurrent.data.proposal.risk.vehicles[0].make='Concurrent vehicle make';
  const response=await page.request.put(`${origin}/api/v1/quotes/${id}/proposal`,{headers:{'X-CSRF-Token':await csrf(),'Idempotency-Key':crypto.randomUUID(),'If-Match':concurrent.etag},data:{proposal:concurrent.data.proposal}});assert.equal(response.status(),200);
  await button('Save draft').click();await button('Load saved comparison').click();await page.getByRole('cell',{name:/Concurrent vehicle make/}).waitFor();assert.equal(await field('Vehicle 1 · Make').inputValue(),'Local vehicle make');await button('Discard my edits and load saved revision').click();assert.equal(await field('Vehicle 1 · Make').inputValue(),'Concurrent vehicle make');
  assert.equal(await page.locator('.quote-create-rail').evaluate(node=>Math.round(node.getBoundingClientRect().width)),314);
  await page.evaluate(()=>window.scrollTo(0,0));await page.screenshot({path:`${output}/${product.productCode}-desktop.png`,fullPage:true});await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:`${output}/${product.productCode}-mobile.png`});await page.setViewportSize({width:1560,height:1000});
  await button('Remove vehicle 1').click();stored=await save(id);assert.deepEqual(stored.proposal.risk.vehicles.map(v=>v.id),[original]);
  report.push({id,reference:stored.reference,productCode:product.productCode,revision:stored.revisionNumber,checks:['manual vehicle fields and exact units/money','independent specified/register/ownership declarations','stable modification and plate identities','reorder edit remove reload','invalid buffers retained','portfolio total','exact readiness focus and retained owner removal guard','lost response exact replay','stale comparison and explicit discard','314px rail and390px containment']});
 }
 assert.equal(report.length,2);assert.deepEqual(errors,[]);await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}catch(error){await page.screenshot({path:`${output}/failure.png`,fullPage:true}).catch(()=>{});await writeFile(`${output}/failure.txt`,await page.locator('main').innerText());throw error;}finally{await browser.close();}
