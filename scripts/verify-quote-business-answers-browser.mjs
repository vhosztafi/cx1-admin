import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';

const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const password = process.env.COVER_DEMO_PASSWORD ?? (await readFile('.local/demo-password.txt', 'utf8')).trim();
const output = '.local/browser-evidence/quote-business-answers'; await mkdir(output, { recursive: true });
const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage({ viewport: { width: 1560, height: 1000 } }); page.setDefaultTimeout(20000);
const errors = []; page.on('pageerror', error => errors.push(error.message)); const report = [];
const field = name => page.getByLabel(name, { exact: true });
const button = name => page.getByRole('button', { name, exact: true });
async function read(id) { const response = await page.request.get(`${origin}/api/v1/quotes/${id}`); assert.equal(response.status(), 200); return response.json(); }
async function save(id) {
  const previous = await read(id); await button('Save draft').click();
  await page.getByText(`Draft saved · Revision ${previous.revisionNumber + 1}`, { exact: true }).waitFor(); return read(id);
}
try {
  await page.goto(`${origin}/quotes/new`); await page.waitForURL('**/login');
  await field('Email address').fill('servicing@cover.example'); await field('Password').fill(password); await button('Sign in').click(); await page.waitForURL(`${origin}/`);
  const relationshipId = '51000000-0000-4000-8000-000000000003';
  const products = (await (await page.request.get(`${origin}/api/v1/quote-products?relationshipId=${relationshipId}`)).json()).items;
  for (const product of products) {
    const token = (await (await page.request.get(`${origin}/api/v1/auth/csrf`)).json()).requestToken;
    const created = await page.request.post(`${origin}/api/v1/quotes`, { headers: { 'X-CSRF-Token': token, 'Idempotency-Key': crypto.randomUUID() }, data: { relationshipId, productVersionId: product.productVersionId } });
    assert.equal(created.status(), 201); const id = (await created.json()).id;
    await page.goto(`${origin}/quotes/${id}/edit`); await button('3 Trade activities').click();
    const membership = field('Are you a member of any trade associations?');
    const vat = field('Is the company VAT registered?');
    const vehicles = field('Number of vehicles handled per year');
    const limit = field('Motor Insurance Policy Database (MIPD) vehicle limit');
    assert.equal(await membership.inputValue(), '');
    await membership.selectOption('true'); await vat.selectOption('false'); await vehicles.fill('0'); await limit.fill('12');
    let stored = await save(id);
    assert.ok(stored.readiness.issues.some(x => x.questionId === 'MTS-03-Q05' && x.code === 'conditional-answer-required'));
    assert.ok(stored.readiness.issues.some(x => x.code === 'vehicles-handled-minimum'));
    await field('Trade association name').fill('Fictional Trade Association'); await vehicles.fill('25');
    await vat.selectOption('true'); await field('VAT number').fill('FICTIONAL-123');
    stored = await save(id);
    const answer = (data, question) => data.proposal.risk.business.responses.answers.find(x => x.questionId === question)?.value;
    assert.equal(answer(stored, 'MTS-03-Q05'), 'Fictional Trade Association'); assert.equal(answer(stored, 'MTS-03-Q08'), 25);
    assert.equal(stored.proposal.risk.business.responses.questionSetVersion, product.questionSetVersion);
    await page.reload(); await button('3 Trade activities').click(); assert.equal(await limit.inputValue(), '12');
    await vehicles.fill('1.5'); assert.equal(await button('Save draft').isDisabled(), true);
    await button('2 Proposer').click(); await button('3 Trade activities').click(); assert.equal(await vehicles.inputValue(), '1.5');
    await vehicles.fill('25'); await membership.selectOption('false'); await vat.selectOption('false');
    stored = await save(id); assert.equal(answer(stored, 'MTS-03-Q04'), false); assert.equal(answer(stored, 'MTS-03-Q07'), 'FICTIONAL-123');
    assert.ok(stored.readiness.issues.some(x => x.questionId === 'MTS-03-Q05' && x.code === 'inactive-answer-retained'));
    await field('Trade association name').fill(''); await field('VAT number').fill(''); await limit.fill('0');
    stored = await save(id); assert.equal(answer(stored, 'MTS-03-Q05'), undefined); assert.equal(answer(stored, 'MTS-03-Q07'), undefined); assert.equal(answer(stored, 'MTS-03-Q09'), 0);
    await page.setViewportSize({ width: 390, height: 844 }); assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.getByRole('group', { name: 'Business details', exact: true }).screenshot({ path: `${output}/${product.productCode}-mobile.png` });
    const route = `${origin}/api/v1/quotes/${id}`;
    await page.route(route, async intercepted => { const response = await intercepted.fetch(); const body = await response.json(); body.captureVersions.questionSetVersion = 'future'; await intercepted.fulfill({ response, json: body }); });
    await page.reload(); await button('3 Trade activities').click(); await page.getByRole('alert').filter({ hasText: 'Business answers need' }).waitFor();
    assert.equal(await membership.count(), 0); assert.deepEqual((await read(id)).proposal, stored.proposal);
    await page.unroute(route); report.push({ id, reference: stored.reference, productCode: product.productCode, revisionNumber: stored.revisionNumber });
    await page.setViewportSize({ width: 1560, height: 1000 });
  }
  assert.equal(report.length, 2); assert.deepEqual(errors, []);
  await writeFile(`${output}/report.json`, JSON.stringify(report, null, 2)); console.log(JSON.stringify(report, null, 2));
} catch (failure) { await page.screenshot({ path: `${output}/failure.png`, fullPage: true }); await writeFile(`${output}/failure.txt`, await page.locator('main').innerText()); throw failure; }
finally { await browser.close(); }
