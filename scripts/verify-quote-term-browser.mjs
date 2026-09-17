import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';

const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const password = process.env.COVER_DEMO_PASSWORD ?? (await readFile('.local/demo-password.txt', 'utf8')).trim();
const output = '.local/browser-evidence/quote-term'; await mkdir(output, { recursive: true });
const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage({ viewport: { width: 1560, height: 1000 } }); page.setDefaultTimeout(20000);
const pageErrors = []; page.on('pageerror', error => pageErrors.push(error.message));
const report = [];
async function read(id) { const response = await page.request.get(`${origin}/api/v1/quotes/${id}`); assert.equal(response.status(), 200); return response.json(); }
async function save(id) {
  const previous = await read(id); await page.getByRole('button', { name: 'Save draft', exact: true }).click();
  await page.getByText(`Draft saved · Revision ${previous.revisionNumber + 1}`, { exact: true }).waitFor();
  return read(id);
}
async function termStage() { await page.getByRole('button', { name: '1 Agency & product', exact: true }).click(); }
const field = name => page.getByLabel(name, { exact: true });
try {
  await page.goto(`${origin}/quotes/new`); await page.waitForURL('**/login');
  await field('Email address').fill('servicing@cover.example'); await field('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(`${origin}/`);
  const relationshipId = '51000000-0000-4000-8000-000000000003';
  const products = (await (await page.request.get(`${origin}/api/v1/quote-products?relationshipId=${relationshipId}`)).json()).items.filter(product => product.captureEligible);
  for (const product of products) {
    const token = (await (await page.request.get(`${origin}/api/v1/auth/csrf`)).json()).requestToken;
    const response = await page.request.post(`${origin}/api/v1/quotes`, { headers: { 'X-CSRF-Token': token, 'Idempotency-Key': crypto.randomUUID() }, data: { relationshipId, productVersionId: product.productVersionId, proposal: { schemaVersion: '1.0', productCode: product.productCode } } });
    assert.equal(response.status(), 201); const id = (await response.json()).id;
    await page.goto(`${origin}/quotes/${id}/edit`); await termStage();
    await field('Term type').selectOption('annual');
    const partial = await save(id); assert.deepEqual(partial.proposal.termIntent, { kind: 'annual', timeZone: 'Europe/London' });
    await field('Requested start date').fill('2024-02-29'); await field('Requested start time').fill('12:00');
    await page.getByRole('status').filter({ hasText: 'Annual anniversary: 2025-02-28' }).waitFor();
    let saved = await save(id); assert.deepEqual(saved.readiness.issues.filter(issue => issue.path.startsWith('/termIntent')), []);
    await field('Requested start date').fill('2026-03-29'); await field('Requested start time').fill('01:30');
    assert.equal(await page.getByRole('button', { name: 'Save draft', exact: true }).isDisabled(), true);
    await page.getByRole('alert').filter({ hasText: 'Start time is not a valid London' }).waitFor();
    await field('Requested start date').fill('2025-10-25');
    await field('Annual end clock offset').selectOption('0');
    saved = await save(id); assert.equal(saved.proposal.termIntent.endUtcOffsetMinutes, 0);
    assert.deepEqual(saved.readiness.issues.filter(issue => issue.path.startsWith('/termIntent')), []);
    await field('Term type').selectOption('short-period');
    await field('Requested start date').fill('2026-10-25'); await field('Requested start time').fill('01:45');
    assert.equal(await page.getByRole('button', { name: 'Save draft', exact: true }).isDisabled(), true);
    await field('Start clock offset').selectOption('60');
    await field('Requested end date').fill('2026-10-25'); await field('Requested end time').fill('01:15'); await field('End clock offset').selectOption('60');
    await page.getByRole('alert').filter({ hasText: 'end must be later' }).waitFor();
    await field('End clock offset').selectOption('0');
    saved = await save(id); assert.deepEqual(saved.readiness.issues.filter(issue => issue.path.startsWith('/termIntent')), []);
    assert.equal(saved.proposal.termIntent.utcOffsetMinutes, 60); assert.equal(saved.proposal.termIntent.endUtcOffsetMinutes, 0);
    await page.reload(); await termStage(); assert.equal(await field('Start clock offset').inputValue(), '60'); assert.equal(await field('End clock offset').inputValue(), '0');
    assert.equal((await page.locator('.quote-create-rail').boundingBox()).width, 314);
    await page.locator('main').focus(); await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({ path: `${output}/${product.productCode}-desktop.png`, fullPage: true });
    await page.setViewportSize({ width: 390, height: 844 }); await page.evaluate(() => window.scrollTo(0, 0));
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.screenshot({ path: `${output}/${product.productCode}-mobile.png`, fullPage: true });
    await field('Term type').selectOption('annual');
    assert.equal(await page.getByRole('button', { name: 'Save draft', exact: true }).isDisabled(), true);
    assert.equal(await field('Requested end date').inputValue(), '2026-10-25');
    await page.getByRole('button', { name: 'Remove retained short-period end fields', exact: true }).click();
    saved = await save(id); assert.equal(saved.proposal.termIntent.localEndDate, undefined); assert.equal(saved.proposal.termIntent.localEndTime, undefined);
    assert.deepEqual(saved.readiness.issues.filter(issue => issue.path.startsWith('/termIntent')), []); assert.equal(saved.readiness.ready, false);
    report.push({ id, reference: saved.reference, productCode: saved.productCode, revisionNumber: saved.revisionNumber });
    await page.setViewportSize({ width: 1560, height: 1000 });
  }
  assert.equal(report.length, 2); assert.deepEqual(pageErrors, []);
  await writeFile(`${output}/report.json`, JSON.stringify(report, null, 2)); console.log(JSON.stringify(report, null, 2));
} catch (failure) { await page.screenshot({ path: `${output}/failure.png`, fullPage: true }); await writeFile(`${output}/failure.txt`, await page.locator('main').innerText()); throw failure; }
finally { await browser.close(); }
