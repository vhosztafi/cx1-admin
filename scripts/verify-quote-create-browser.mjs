import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';

const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const password = process.env.COVER_DEMO_PASSWORD ?? (await readFile('.local/demo-password.txt', 'utf8')).trim();
const output = '.local/browser-evidence/quote-create';
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ channel: 'chrome', headless: true });
const context = await browser.newContext({ viewport: { width: 1560, height: 1000 } });
const page = await context.newPage(); page.setDefaultTimeout(20000);
const errors = []; page.on('pageerror', error => errors.push(error.message));
const report = { created: [], checks: [] };
async function signIn(target, email) {
  await target.goto(`${origin}/quotes/new`); await target.waitForURL('**/login');
  await target.getByLabel('Email address', { exact: true }).fill(email);
  await target.getByLabel('Password', { exact: true }).fill(password);
  await target.getByRole('button', { name: 'Sign in', exact: true }).click(); await target.waitForURL(`${origin}/`);
}
async function selectProduct(name) {
  await page.goto(`${origin}/quotes`);
  await page.locator('main').getByRole('link', { name: 'New quote', exact: true }).click();
  await page.getByLabel('Search clients', { exact: true }).fill('CL-DEMO-QUOTES');
  await page.getByRole('button', { name: 'Search', exact: true }).click();
  await page.getByRole('button', { name: /Alex Example \(fictional\)/ }).click();
  await page.getByRole('radio', { name: /Fictional Quote Demonstration Agency/ }).check();
  await page.getByRole('radio', { name: new RegExp(name) }).and(page.locator(':enabled')).check();
  assert.equal(await page.getByRole('button', { name: 'Create quote draft', exact: true }).isEnabled(), true);
}
async function verifySaved(expectedId, productCode) {
  await page.waitForURL(/\/quotes\/[0-9a-f-]{36}$/);
  const id = page.url().split('/').at(-1); if (expectedId) assert.equal(id, expectedId);
  const response = await page.request.get(`${origin}/api/v1/quotes/${id}`); assert.equal(response.status(), 200);
  const quote = await response.json(); assert.equal(quote.productCode, productCode); assert.equal(quote.revisionNumber, 1);
  assert.equal(quote.relationshipId, '51000000-0000-4000-8000-000000000003'); assert.equal(quote.readiness.ready, false);
  await page.getByRole('heading', { name: quote.reference, exact: true }).waitFor();
  await page.reload(); await page.getByRole('heading', { name: quote.reference, exact: true }).waitFor();
  report.created.push({ id, reference: quote.reference, productCode, revisionNumber: quote.revisionNumber });
}
try {
  await signIn(page, 'servicing@cover.example');
  await selectProduct('Road Risks');
  const rail = await page.locator('.quote-create-rail').boundingBox(); assert.equal(rail.width, 314);
  await page.screenshot({ path: `${output}/selection-desktop.png`, fullPage: true });
  const attempts = []; let committedId;
  await page.route('**/api/v1/quotes', async route => {
    if (route.request().method() !== 'POST') return route.continue();
    attempts.push({ body: route.request().postData(), key: route.request().headers()['idempotency-key'] });
    if (attempts.length === 1) {
      const response = await route.fetch(); assert.equal(response.status(), 201);
      committedId = (await response.json()).id; return route.abort('failed');
    }
    // A later denial cannot resolve an earlier uncertain committed outcome.
    if (attempts.length === 2) return route.fulfill({ status: 403, contentType: 'application/json', body: '{}' });
    return route.continue();
  });
  await page.getByRole('button', { name: 'Create quote draft', exact: true }).click();
  await page.getByRole('button', { name: 'Retry same creation', exact: true }).waitFor();
  assert.equal(await page.getByRole('button', { name: 'Change client', exact: true }).isDisabled(), true);
  await page.getByRole('link', { name: 'Back to quotes', exact: true }).click(); assert.ok(page.url().endsWith('/quotes/new'));
  await page.evaluate(() => history.back());
  await page.getByRole('alert').filter({ hasText: 'Confirm the pending creation' }).waitFor(); assert.ok(page.url().endsWith('/quotes/new'));
  await page.getByRole('button', { name: 'Retry same creation', exact: true }).click();
  await page.getByRole('alert').filter({ hasText: 'Your current access' }).waitFor();
  assert.equal(await page.getByRole('button', { name: 'Change client', exact: true }).isDisabled(), true);
  await page.getByRole('button', { name: 'Retry same creation', exact: true }).click();
  await verifySaved(committedId, 'motor-trade-road-risks');
  assert.equal(attempts.length, 3); assert.deepEqual(attempts[0], attempts[1]); assert.deepEqual(attempts[0], attempts[2]);
  await page.unroute('**/api/v1/quotes'); report.checks.push('lost response, identical replay, retained recovery after denial, link/history navigation guards, reload');
  await page.screenshot({ path: `${output}/saved-desktop.png`, fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  await selectProduct('Combined');
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  const main = await page.locator('.quote-create-panels').boundingBox(); const mobileRail = await page.locator('.quote-create-rail').boundingBox();
  assert.ok(mobileRail.y >= main.y + main.height);
  await page.screenshot({ path: `${output}/selection-mobile.png`, fullPage: true });
  await page.getByRole('button', { name: 'Create quote draft', exact: true }).click();
  await verifySaved(undefined, 'motor-trade-combined');
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  await page.screenshot({ path: `${output}/saved-mobile.png`, fullPage: true });
  report.checks.push('both products persist; 314px desktop rail; 390px layout');
  const deniedContext = await browser.newContext(); const denied = await deniedContext.newPage();
  await signIn(denied, 'system-admin@cover.example');
  for (const path of ['/quotes', '/quotes/new', `/quotes/${report.created[0].id}`]) {
    await denied.goto(origin + path); await denied.getByRole('heading', { name: 'Access restricted', exact: true }).waitFor();
  }
  assert.equal((await denied.request.get(`${origin}/api/v1/quotes/${report.created[0].id}`)).status(), 403);
  await deniedContext.close(); report.checks.push('server page and API role denials');
  assert.deepEqual(errors, []); await writeFile(`${output}/report.json`, JSON.stringify(report, null, 2));
  console.log(JSON.stringify(report, null, 2));
} finally { await browser.close(); }
