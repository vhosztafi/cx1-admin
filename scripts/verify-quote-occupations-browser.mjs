import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';

const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const password = process.env.COVER_DEMO_PASSWORD ?? (await readFile('.local/demo-password.txt', 'utf8')).trim();
const output = '.local/browser-evidence/quote-occupations'; await mkdir(output, { recursive: true });
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
    await page.goto(`${origin}/quotes/${id}/edit`); await button('3 Trade activities').click(); await button('Add occupation').click();
    const first = await page.locator('.quote-occupation').getAttribute('data-activity-id');
    let stored = await save(id); assert.deepEqual(stored.proposal.risk.business.activities, [{ id: first }]);
    await field('Occupation 1').selectOption({ label: 'Mechanical / Servicing / Overhauls - Vehicles' });
    await field('Occupation 1 turnover share (%)').fill('33.33'); await button('Add occupation').click();
    const second = await page.locator('.quote-occupation').nth(1).getAttribute('data-activity-id'); assert.notEqual(first, second);
    await field('Occupation 2').selectOption({ label: 'Valeting - at Premises Only' }); await field('Occupation 2 turnover share (%)').fill('66.67');
    stored = await save(id); assert.deepEqual(stored.proposal.risk.business.activities.map(row => row.id), [first, second]);
    assert.deepEqual(stored.proposal.risk.business.activities.map(row => row.turnoverBasisPoints), [3333, 6667]);
    assert.equal(stored.proposal.risk.business.activities[0].code.version, product.referenceDataVersion);
    await button('Move occupation 2 up').click(); await field('Occupation 1 turnover share (%)').fill('66.667');
    assert.equal(await button('Save draft').isDisabled(), true); await button('Move occupation 1 down').click();
    assert.equal(await field('Occupation 2 turnover share (%)').inputValue(), '66.667');
    assert.equal(await page.locator('.quote-occupation').nth(1).getAttribute('data-activity-id'), second);
    await field('Occupation 2 turnover share (%)').fill('66.67'); await button('Move occupation 2 up').click();
    stored = await save(id); assert.deepEqual(stored.proposal.risk.business.activities.map(row => row.id), [second, first]);
    await page.reload(); await button('3 Trade activities').click();
    assert.deepEqual(await page.locator('.quote-occupation').evaluateAll(rows => rows.map(row => row.getAttribute('data-activity-id'))), [second, first]);
    await page.locator('.quote-occupations').screenshot({ path: `${output}/${product.productCode}-desktop.png` });
    await page.setViewportSize({ width: 390, height: 844 }); assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.locator('.quote-occupations').screenshot({ path: `${output}/${product.productCode}-mobile.png` });
    await field('Occupation 1').selectOption({ label: 'Car Jockey' });
    await page.getByRole('alert').filter({ hasText: 'must be the only occupation' }).waitFor();
    stored = await save(id); assert.ok(stored.readiness.issues.some(issue => issue.code === 'car-jockey-must-be-only-activity'));
    await field('Occupation 2').selectOption({ label: 'Car Jockey' });
    await page.getByRole('alert').filter({ hasText: 'selected more than once' }).waitFor();
    stored = await save(id); assert.ok(stored.readiness.issues.some(issue => issue.code === 'duplicate-business-activity'));
    await button('Remove occupation 1').click(); await field('Occupation 1 turnover share (%)').fill('100');
    stored = await save(id); assert.equal(stored.proposal.risk.business.activities[0].id, first);
    assert.equal(stored.proposal.risk.business.activities.length, 1); assert.equal(stored.proposal.risk.business.activities[0].turnoverBasisPoints, 10000);
    assert.equal(stored.readiness.issues.some(issue => ['duplicate-business-activity', 'car-jockey-must-be-only-activity', 'activity-total-must-equal-100-percent'].includes(issue.code)), false);
    await button('Add occupation').click(); await field('Occupation 2 turnover share (%)').fill('1.234'); assert.equal(await button('Save draft').isDisabled(), true);
    await button('Remove occupation 2').click(); assert.equal(await button('Save draft').isEnabled(), true);
    const route = `${origin}/api/v1/quotes/${id}`;
    await page.route(route, async intercepted => { const response = await intercepted.fetch(); const body = await response.json(); body.captureVersions.referenceDataVersion = 'future'; await intercepted.fulfill({ response, json: body }); });
    await page.reload(); await button('3 Trade activities').click(); await page.getByRole('alert').filter({ hasText: 'Occupation editing needs' }).waitFor();
    assert.equal(await button('Add occupation').count(), 0); assert.deepEqual((await read(id)).proposal.risk.business.activities, stored.proposal.risk.business.activities);
    await page.unroute(route); report.push({ id, reference: stored.reference, productCode: product.productCode, revisionNumber: stored.revisionNumber });
    await page.setViewportSize({ width: 1560, height: 1000 });
  }
  assert.equal(report.length, 2); assert.deepEqual(errors, []);
  await writeFile(`${output}/report.json`, JSON.stringify(report, null, 2)); console.log(JSON.stringify(report, null, 2));
} catch (failure) { await page.screenshot({ path: `${output}/failure.png`, fullPage: true }); await writeFile(`${output}/failure.txt`, await page.locator('main').innerText()); throw failure; }
finally { await browser.close(); }
