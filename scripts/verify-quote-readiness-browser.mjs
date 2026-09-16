import { readinessTarget } from '../apps/backoffice/lib/quote-readiness.ts';
import { sourceBusinessQuestions } from '../apps/backoffice/lib/quote-source-questions.ts';
import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';

const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const password = process.env.COVER_DEMO_PASSWORD ?? (await readFile('.local/demo-password.txt', 'utf8')).trim();
const output = '.local/browser-evidence/quote-readiness'; await mkdir(output, { recursive: true });
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
const mappings = JSON.parse(await readFile('contracts/quote-question-catalogue.json', 'utf8')).mappings;
const refs = JSON.parse(await readFile('contracts/reference-data/motor-trade-capture.json', 'utf8'));
const questions = sourceBusinessQuestions(mappings, refs.bindings, refs.collections);
try {
  await page.goto(`${origin}/quotes/new`); await page.waitForURL('**/login');
  await field('Email address').fill('servicing@cover.example'); await field('Password').fill(password); await button('Sign in').click(); await page.waitForURL(`${origin}/`);
  const relationshipId = '51000000-0000-4000-8000-000000000003';
  const products = (await (await page.request.get(`${origin}/api/v1/quote-products?relationshipId=${relationshipId}`)).json()).items;
  for (const product of products) {
    const token = (await (await page.request.get(`${origin}/api/v1/auth/csrf`)).json()).requestToken;
    const created = await page.request.post(`${origin}/api/v1/quotes`, { headers: { 'X-CSRF-Token': token, 'Idempotency-Key': crypto.randomUUID() }, data: { relationshipId, productVersionId: product.productVersionId } });
    assert.equal(created.status(), 201); const id = (await created.json()).id;
    await page.goto(`${origin}/quotes/${id}/edit`);
    const stored = await read(id); const targets = stored.readiness.issues.map(issue => readinessTarget(issue, stored.proposal, questions)).filter(Boolean);
    const unique = [...new Map(targets.map(target => [target.label, target])).values()]; assert.ok(unique.length > 10);
    for (const target of unique) {
      await button(`Review ${target.label}`).first().click();
      const control = target.label === 'Add occupation' ? button(target.label) : field(target.label);
      await page.waitForFunction(element => element === document.activeElement, await control.elementHandle());
      assert.equal(await control.evaluate(element => element === document.activeElement), true, target.label);
    }
    await button('2 Proposer').click(); await field('Business description').fill('Fictional readiness demonstration');
    assert.equal(await button('Review First name').first().isDisabled(), true);
    await field('Business start date').fill('2020-01-01'); await save(id);
    await button('Review First name').first().click(); await page.waitForFunction(element => element === document.activeElement, await field('First name').elementHandle()); assert.equal(await field('First name').evaluate(el => el === document.activeElement), true);
    await field('First name').fill('Fictional'); await save(id); assert.equal(await button('Review First name').count(), 0);
    await page.reload(); assert.equal(await field('Business start date').inputValue(), '2020-01-01');
    await button('3 Trade activities').click(); assert.equal(await field('Business start date').count(), 0);
    await page.setViewportSize({ width: 390, height: 844 }); assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.evaluate(() => { document.activeElement?.blur(); window.scrollTo(0, 0); });
    await page.screenshot({ path: `${output}/${product.productCode}-mobile.png`, fullPage: true });
    assert.equal((await read(id)).readiness.ready, false);
    report.push({ id, reference: stored.reference, productCode: product.productCode, targetsChecked: unique.length });
    await page.setViewportSize({ width: 1560, height: 1000 });
  }
  assert.equal(report.length, 2); assert.deepEqual(errors, []);
  await writeFile(`${output}/report.json`, JSON.stringify(report, null, 2)); console.log(JSON.stringify(report, null, 2));
} catch (failure) { await page.screenshot({ path: `${output}/failure.png`, fullPage: true }); await writeFile(`${output}/failure.txt`, await page.locator('main').innerText()); throw failure; }
finally { await browser.close(); }
