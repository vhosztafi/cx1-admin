import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const password=(await readFile('.local/demo-password.txt','utf8')).trim(),output='.local/browser-evidence/quote-cover';await mkdir(output,{recursive:true});
const browser=await chromium.launch({channel:'chrome',headless:true}),page=await browser.newPage({viewport:{width:1560,height:1000}});page.setDefaultTimeout(20000);
const errors=[];page.on('pageerror',error=>errors.push(error.message));const report=[];
const field=name=>page.getByLabel(name,{exact:true}),button=name=>page.getByRole('button',{name,exact:true,includeHidden:true});
const csrf=async()=>(await(await page.request.get(`${origin}/api/v1/auth/csrf`)).json()).requestToken;
async function read(id){const response=await page.request.get(`${origin}/api/v1/quotes/${id}`);assert.equal(response.status(),200);return{data:await response.json(),etag:response.headers().etag};}
async function save(id){const before=await read(id);await button('Save draft').click();await page.getByText(`Draft saved · Revision ${before.data.revisionNumber+1}`,{exact:true}).waitFor();return(await read(id)).data;}
async function openVehicle(n){const card=page.locator('.quote-driver-section > details').nth(n-1);if(!await card.evaluate(node=>node.open))await card.locator(':scope > summary').click();}
async function openSection(name){const summary=page.getByText(name,{exact:true}).filter({hasNot:page.locator('option')});const details=summary.locator('..');if(!await details.evaluate(node=>node.open))await summary.click();}
try{
 await page.goto(`${origin}/quotes/new`);await page.waitForURL('**/login');await field('Email address').fill('servicing@cover.example');await field('Password').fill(password);await button('Sign in').click();await page.waitForURL(`${origin}/`);
 const relationshipId='51000000-0000-4000-8000-000000000003';const products=(await(await page.request.get(`${origin}/api/v1/quote-products?relationshipId=${relationshipId}`)).json()).items;
 for(const product of products){
  const proposal=JSON.parse(await readFile(`contracts/examples/quote-capture-${product.productCode}.json`,'utf8')).proposal;
  const created=await page.request.post(`${origin}/api/v1/quotes`,{headers:{'X-CSRF-Token':await csrf(),'Idempotency-Key':crypto.randomUUID()},data:{relationshipId,productVersionId:product.productVersionId,proposal}});assert.equal(created.status(),201,await created.text());const id=(await created.json()).id;

  const combined=product.productCode==='motor-trade-combined';
  const cover=()=>button('8 Cover & excess').click();
  const premises=()=>button(combined?'4 Premises':'3 Trade activities').click();
  await page.goto(`${origin}/quotes/${id}/edit`);await premises();
  await button('Add premises').click();
  const count=await page.getByRole('button',{name:/^Remove premises /,includeHidden:true}).count();
  const row=page.locator('details').filter({has:button(`Remove premises ${count}`)});if(!await row.evaluate(node=>node.open))await row.locator(':scope > summary').click();
  await field(`Premises ${count} · Postcode`).fill('SW1A 1AA');await field(`Premises ${count} · Street`).fill('Fictional Demo Street');
  if(combined){await field(`Premises ${count} · Buildings sum insured`).fill('125000.01');await field(`Premises ${count} · Contents sum insured`).fill('25000.50');await field(`Premises ${count} · Declared premises use`).selectOption({index:1});await field(`Premises ${count} · Security`).selectOption({index:1});}
  let stored=await save(id);const added=stored.proposal.risk.premises.at(-1).id;
  await page.reload();await premises();assert.equal((await read(id)).data.proposal.risk.premises.at(-1).id,added);
  if(count>1){if(!await row.evaluate(node=>node.open))await row.locator(':scope > summary').click();await button(`Move premises ${count} up`).click();stored=await save(id);assert.equal(stored.proposal.risk.premises[count-2].id,added);await button(`Remove premises ${count-1}`).click();}else await button('Remove premises 1').click();stored=await save(id);assert.deepEqual(stored.proposal.risk.premises,proposal.risk.premises??[]);
  if(combined)await cover();else await button('7 Previous insurance & NCD').click();
  await field('Previous insurance · Previous policy number').fill('DEMO-SECTION-001');await field('Previous insurance · Previous policyholder name').fill('Alex Example');await field('Previous insurance · Previous policy expiry').fill('2027-01-31');
  stored=await save(id);assert.equal(stored.proposal.risk.previousInsurance.policyNumber,'DEMO-SECTION-001');
  await page.reload();if(combined)await cover();else await button('7 Previous insurance & NCD').click();assert.equal(await field('Previous insurance · Previous policy number').inputValue(),'DEMO-SECTION-001');
  await cover();await field('Cover · Is loss of use cover required?').selectOption('true');stored=await save(id);assert.ok(stored.proposal.cover.responses.answers.some(a=>a.questionId==='MTS-11-Q07'&&a.value===true));
  await button('Add annual european vehicle').click();const annualCount=await page.getByRole('button',{name:/^Remove annual european vehicle /,includeHidden:true}).count();
  const annual=page.locator('details').filter({has:button(`Remove annual european vehicle ${annualCount}`)});if(!await annual.evaluate(node=>node.open))await annual.locator(':scope > summary').click();await field(`Annual European vehicle ${annualCount} · Vehicle Registration`).fill('XZ12 DEM');
  await button('Add european trip').click();const tripCount=await page.getByRole('button',{name:/^Remove european trip /,includeHidden:true}).count();const trip=page.locator('details').filter({has:button(`Remove european trip ${tripCount}`)});if(!await trip.evaluate(node=>node.open))await trip.locator(':scope > summary').click();
  await field(`European trip ${tripCount} · Vehicle Registration`).fill('XZ12 DEM');await field(`European trip ${tripCount} · Start Date of Trip`).fill('2026-10-10');await field(`European trip ${tripCount} · End Date of Trip`).fill('2026-10-08');await field(`European trip ${tripCount} · Trip driver 1`).check();
  stored=await save(id);assert.equal(stored.proposal.cover.temporaryEuropeanCover.at(-1).driverIds[0],stored.proposal.risk.drivers[0].id);const tripId=stored.proposal.cover.temporaryEuropeanCover.at(-1).id;
  await button(`Review European trip ${tripCount} · End Date of Trip`).first().click();await page.waitForFunction(()=>document.activeElement?.getAttribute('aria-label')?.endsWith(' · End Date of Trip'));await field(`European trip ${tripCount} · Start Date of Trip`).fill('2026-10-01');stored=await save(id);
  assert.equal(stored.readiness.ready,false);assert.ok(stored.readiness.issues.some(i=>i.category==='evidence'));await page.getByText('Documentary evidence that the proposer is a motor trader is missing. Evidence attachment is not yet available.',{exact:true}).waitFor();
  await page.reload();await cover();assert.equal((await read(id)).data.proposal.cover.temporaryEuropeanCover.at(-1).id,tripId);await button(`Remove european trip ${tripCount}`).click();await button(`Remove annual european vehicle ${annualCount}`).click();await save(id);
  await button('9 Declarations & review').click();await field('Declarations · Material facts and additional information').fill('Fictional business demo. No additional material facts declared.');stored=await save(id);assert.equal(stored.proposal.risk.materialFacts,'Fictional business demo. No additional material facts declared.');
  const before=await read(id);await field('Declarations · Material facts and additional information').fill('Saved after lost response');const pending=[];let first=true;
  await page.route(`**/api/v1/quotes/${id}/proposal`,async route=>{pending.push({body:route.request().postData(),key:route.request().headers()['idempotency-key'],etag:route.request().headers()['if-match']});if(first){first=false;const response=await route.fetch();assert.equal(response.status(),200);await route.abort('failed');}else await route.continue();});
  await button('Save draft').click();await button('Retry same save').click();await page.getByText(`Draft saved · Revision ${before.data.revisionNumber+1}`,{exact:true}).waitFor();await page.unroute(`**/api/v1/quotes/${id}/proposal`);assert.deepEqual(pending[0],pending[1]);
  const concurrent=await read(id);await field('Declarations · Material facts and additional information').fill('Local declaration');concurrent.data.proposal.risk.materialFacts='Concurrent declaration';
  const response=await page.request.put(`${origin}/api/v1/quotes/${id}/proposal`,{headers:{'X-CSRF-Token':await csrf(),'Idempotency-Key':crypto.randomUUID(),'If-Match':concurrent.etag},data:{proposal:concurrent.data.proposal}});assert.equal(response.status(),200);
  await button('Save draft').click();await button('Load saved comparison').click();await page.getByRole('cell',{name:/Concurrent declaration/}).waitFor();assert.equal(await field('Declarations · Material facts and additional information').inputValue(),'Local declaration');await button('Discard my edits and load saved revision').click();assert.equal(await field('Declarations · Material facts and additional information').inputValue(),'Concurrent declaration');
  assert.equal(await page.locator('.quote-create-rail').evaluate(node=>Math.round(node.getBoundingClientRect().width)),314);await page.evaluate(()=>window.scrollTo(0,0));await page.screenshot({path:`${output}/${product.productCode}-desktop.png`,fullPage:true});await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:`${output}/${product.productCode}-mobile.png`});await page.setViewportSize({width:1560,height:1000});
  stored=(await read(id)).data;report.push({id,reference:stored.reference,productCode:product.productCode,revision:stored.revisionNumber,checks:['persisted premises and exact sums','stable annual and temporary trip children and actual driver IDs','accessible previous insurance in both products','declaration reload and field-linked trip errors','evidence missing and progression closed','lost response exact replay','stale comparison explicit discard','314px rail and390px containment']});
 }
 assert.equal(report.length,2);assert.deepEqual(errors,[]);await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
}catch(error){await page.screenshot({path:`${output}/failure.png`,fullPage:true}).catch(()=>{});await writeFile(`${output}/failure.txt`,await page.locator('main').innerText());throw error;}finally{await browser.close();}
