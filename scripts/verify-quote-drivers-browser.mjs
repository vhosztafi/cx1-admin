import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';

const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const password = (await readFile('.local/demo-password.txt', 'utf8')).trim();
const output = '.local/browser-evidence/quote-drivers'; await mkdir(output, { recursive: true });
const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage({ viewport: { width: 1560, height: 1000 } }); page.setDefaultTimeout(20000);
const errors = []; page.on('pageerror', error => errors.push(error.message)); const report = [];
const field = name => page.getByLabel(name, { exact: true });
const button = name => page.getByRole('button', { name, exact: true });
const csrf = async () => (await (await page.request.get(`${origin}/api/v1/auth/csrf`)).json()).requestToken;
async function read(id) { const response = await page.request.get(`${origin}/api/v1/quotes/${id}`); assert.equal(response.status(),200); return { data: await response.json(), etag: response.headers().etag }; }
async function save(id) {
  const before = await read(id); await button('Save draft').click();
  await page.getByText(`Draft saved · Revision ${before.data.revisionNumber + 1}`, { exact: true }).waitFor(); return (await read(id)).data;
}
async function openDriver(index) {
  const details = page.locator('.quote-driver-section > details').nth(index);
  if (!(await details.getAttribute('open'))) { if (!await details.evaluate(node => node.open)) await details.locator(':scope > summary').click(); }
}
try {
  await page.goto(`${origin}/quotes/new`); await page.waitForURL('**/login');
  await field('Email address').fill('servicing@cover.example'); await field('Password').fill(password); await button('Sign in').click(); await page.waitForURL(`${origin}/`);
  const relationshipId = '51000000-0000-4000-8000-000000000003';
  const products = (await (await page.request.get(`${origin}/api/v1/quote-products?relationshipId=${relationshipId}`)).json()).items;
  for (const product of products) {
    const fixture = JSON.parse(await readFile(`contracts/examples/quote-capture-${product.productCode}.json`,'utf8')).proposal;
    const created = await page.request.post(`${origin}/api/v1/quotes`, { headers: { 'X-CSRF-Token': await csrf(), 'Idempotency-Key': crypto.randomUUID() }, data: { relationshipId, productVersionId: product.productVersionId, proposal: fixture } });
    assert.equal(created.status(),201,await created.text()); const id = (await created.json()).id;
    const driverStage = product.productCode === 'motor-trade-combined' ? 5 : 4;
    const drivers = () => button(`${driverStage} Drivers`).click();
    const history = () => button(`${driverStage + 1} Claims & convictions`).click();
    await page.goto(`${origin}/quotes/${id}/edit`); await drivers();
    await button('Add driver').click(); await openDriver(1);
    await field('Driver 2 · Full name').fill('Taylor Demo'); await field('Driver 2 · First Name').fill('Taylor'); await field('Driver 2 · Surname').fill('Demo');
    await field('Driver 2 · Date of Birth').fill('1990-01-01'); await field('Driver 2 · Driving licence number').fill('FICTIONAL-DRIVER');
    await field('Driver 2 · Driving test date').fill('2008-01-01'); await field('Driver 2 · Driving Licence Issue').fill('2008-02-01');
    let stored = await save(id); const addedId = stored.proposal.risk.drivers[1].id; const originalId = stored.proposal.risk.drivers[0].id;
    await button('Move driver 2 up').click(); stored = await save(id);
    assert.deepEqual(stored.proposal.risk.drivers.map(row=>row.id),[addedId,originalId]);
    await page.reload(); await drivers(); await openDriver(0);
    assert.equal(await field('Driver 1 · Driving test date').inputValue(),'2008-01-01');
    assert.equal(await field('Driver 1 · Driving Licence Issue').inputValue(),'2008-02-01');
    const groups = [
      ['occupations','Additional occupations','Additional Occupation','reference'],
      ['convictions','Motoring convictions','Fine Amount','money'],
      ['losses','Accidents and claims','Total Cost','money'],
      ['criminalConvictions','Criminal convictions','Sentence Length (months)','count'],
      ['countyCourtJudgments','County court judgments','Amount','money'],
    ];
    const retained = {};
    for (const [key,label,edit,kind] of groups) {
      if (key === 'occupations') await drivers(); else await history(); await openDriver(0);
      await button(`Add ${label.toLowerCase()} for driver 1`).click();
      const name = row => `Driver 1 · ${label} ${row} · ${edit}`;
      if (kind === 'reference') await field(name(1)).selectOption('0'); else await field(name(1)).fill('0');
      if (key === 'occupations') await field('Driver 1 · Additional occupations 1 · Business Use Required').selectOption('false');
      await button(`Add ${label.toLowerCase()} for driver 1`).click();
      if (kind === 'reference') await field(name(2)).selectOption('1'); else await field(name(2)).fill('2');
      stored = await save(id); const first = stored.proposal.risk.drivers[0][key][0].id; const second = stored.proposal.risk.drivers[0][key][1].id;
      await button(`Move driver 1 · ${label.toLowerCase()} 2 up`).click();
      if (kind === 'money') await field(name(2)).fill('12.34');
      stored = await save(id); assert.deepEqual(stored.proposal.risk.drivers[0][key].map(row=>row.id),[second,first]);
      if (kind === 'money') assert.equal(stored.proposal.risk.drivers[0][key][1][key === 'convictions' ? 'fine' : 'amount'],'12.34');
      await button(`Remove driver 1 · ${label.toLowerCase()} 1`).click(); stored = await save(id);
      assert.equal(stored.proposal.risk.drivers[0][key][0].id,first); retained[key] = first;
    }
    await page.reload(); await history(); await openDriver(0);
    for (const [key,id] of Object.entries(retained)) assert.equal((await read(stored.id)).data.proposal.risk.drivers[0][key][0].id,id);
    await field('Driver 1 · Motoring convictions 1 · Fine Amount').fill('1.234'); assert.equal(await button('Save draft').isDisabled(),true);
    await drivers(); await history(); assert.equal(await field('Driver 1 · Motoring convictions 1 · Fine Amount').inputValue(),'1.234');
    await field('Driver 1 · Motoring convictions 1 · Fine Amount').fill('12.34'); assert.equal(await button('Save draft').isEnabled(),true);
    await field('Driver 1 · Motoring convictions 1 · Conviction Date').fill('2026-08-01'); stored = await save(id);
    const review = button('Review Motoring convictions in the last five years or pending prosecutions');
    await review.click(); await page.waitForFunction(() => document.activeElement?.getAttribute('aria-label') === 'Motoring convictions in the last five years or pending prosecutions');
    await field('Motoring convictions in the last five years or pending prosecutions').selectOption('true'); stored = await save(id);
    assert.equal(stored.proposal.risk.business.responses.answers.find(row=>row.questionId==='prototype.quote.ef70e80708bb').value,true);
    const current = await read(id); const invalid = structuredClone(current.data.proposal);
    invalid.risk.drivers[0].losses[0].riskItemId = crypto.randomUUID();
    const rejected = await page.request.put(`${origin}/api/v1/quotes/${id}/proposal`,{headers:{'X-CSRF-Token':await csrf(),'Idempotency-Key':crypto.randomUUID(),'If-Match':current.etag},data:{proposal:invalid}});
    assert.equal(rejected.status(),422); assert.equal((await read(id)).data.revisionNumber,current.data.revisionNumber);
    await drivers(); await openDriver(0); await field('Driver 1 · Full name').fill('Taylor Retained');
    const pending = []; let firstAttempt = true;
    await page.route(`**/api/v1/quotes/${id}/proposal`,async route => {
      pending.push({body:route.request().postData(),key:route.request().headers()['idempotency-key'],etag:route.request().headers()['if-match']});
      if (firstAttempt) { firstAttempt=false; const response=await route.fetch();assert.equal(response.status(),200);await route.abort('failed'); } else await route.continue();
    });
    await button('Save draft').click(); await button('Retry same save').waitFor(); assert.equal(await field('Driver 1 · Full name').isDisabled(),true);
    await button('Retry same save').click(); await page.getByText(`Draft saved · Revision ${current.data.revisionNumber + 1}`,{exact:true}).waitFor();
    await page.unroute(`**/api/v1/quotes/${id}/proposal`); assert.equal(pending.length,2);assert.deepEqual(pending[0],pending[1]);
    const beforeConflict=await read(id); await field('Driver 1 · Full name').fill('Local driver edit');
    beforeConflict.data.proposal.risk.drivers[0].fullName='Concurrent driver edit';
    const concurrent=await page.request.put(`${origin}/api/v1/quotes/${id}/proposal`,{headers:{'X-CSRF-Token':await csrf(),'Idempotency-Key':crypto.randomUUID(),'If-Match':beforeConflict.etag},data:{proposal:beforeConflict.data.proposal}});assert.equal(concurrent.status(),200);
    await button('Save draft').click(); await button('Load saved comparison').click();
    await page.getByRole('cell',{name:/Concurrent driver edit/}).waitFor(); assert.equal(await field('Driver 1 · Full name').inputValue(),'Local driver edit');
    await button('Discard my edits and load saved revision').click(); assert.equal(await field('Driver 1 · Full name').inputValue(),'Concurrent driver edit');
    await page.locator('.quote-create-rail').evaluate(node=>{if(Math.round(node.getBoundingClientRect().width)!==314)throw new Error('Expected314pxrail');});
    await page.evaluate(()=>window.scrollTo(0,0));
    await page.screenshot({path:`${output}/${product.productCode}-desktop.png`,fullPage:true});
    await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
    await page.evaluate(()=>window.scrollTo(0,0));
    await page.screenshot({path:`${output}/${product.productCode}-mobile.png`,fullPage:true});
    await page.screenshot({path:`${output}/${product.productCode}-mobile-viewport.png`});
    await button('Remove driver 1').click();stored=await save(id);assert.deepEqual(stored.proposal.risk.drivers.map(row=>row.id),[originalId]);
    report.push({id,reference:stored.reference,productCode:product.productCode,revision:stored.revisionNumber,checks:['stable driver and all five history identities','reorder edit remove reload','money/zero/false and invalid buffers','test date separate from issue date','history declaration field focus','orphan reference rejected without revision','lost response exact retry','stale comparison retains driver edits','314px rail and390px layout']});
    await page.setViewportSize({width:1560,height:1000});
  }
  assert.equal(report.length,2);assert.deepEqual(errors,[]);await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log(JSON.stringify(report,null,2));
} catch(error) { await page.screenshot({path:`${output}/failure.png`,fullPage:true});await writeFile(`${output}/failure.txt`,await page.locator('main').innerText());throw error; } finally { await browser.close(); }
