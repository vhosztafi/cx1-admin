import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';

const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const password = process.env.COVER_DEMO_PASSWORD ?? (await readFile('.local/demo-password.txt', 'utf8')).trim();
const output = '.local/browser-evidence/quote-proposer'; await mkdir(output, { recursive: true });
const browser = await chromium.launch({ channel: 'chrome', headless: true });
const page = await browser.newPage({ viewport: { width: 1560, height: 1000 } }); page.setDefaultTimeout(20000);
const errors = []; page.on('pageerror', error => errors.push(error.message)); const report = [];
const field = name => page.getByLabel(name, { exact: true });
const question = (quote, id) => quote.proposal.insured.responses.answers.find(answer => answer.questionId === id);
async function read(id) { const response = await page.request.get(`${origin}/api/v1/quotes/${id}`); assert.equal(response.status(), 200); return response.json(); }
async function save(id) {
  const previous = await read(id); await page.getByRole('button', { name: 'Save draft', exact: true }).click();
  await page.getByText(`Draft saved · Revision ${previous.revisionNumber + 1}`, { exact: true }).waitFor(); return read(id);
}
try {
  await page.goto(`${origin}/quotes/new`); await page.waitForURL('**/login');
  await field('Email address').fill('servicing@cover.example'); await field('Password').fill(password);
  await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(`${origin}/`);
  const relationshipId = '51000000-0000-4000-8000-000000000003';
  const products = (await (await page.request.get(`${origin}/api/v1/quote-products?relationshipId=${relationshipId}`)).json()).items;
  for (const product of products) {
    const token = (await (await page.request.get(`${origin}/api/v1/auth/csrf`)).json()).requestToken;
    const created = await page.request.post(`${origin}/api/v1/quotes`, { headers: { 'X-CSRF-Token': token, 'Idempotency-Key': crypto.randomUUID() }, data: { relationshipId, productVersionId: product.productVersionId } });
    assert.equal(created.status(), 201); const id = (await created.json()).id; const route = `${origin}/api/v1/quotes/${id}`;
    await page.goto(`${origin}/quotes/${id}/edit`);
    await field('First name').fill('Alex');
    await field('Proposer title').selectOption({ label: 'Dr' });
    await field('Company category').selectOption({ label: 'Private Limited Company' });
    await field('Consent to collect quotation data').selectOption('false');
    await page.getByRole('checkbox', { name: 'Direct Marketing', exact: true }).check();
    await page.getByRole('checkbox', { name: 'Email', exact: true }).check();
    await page.getByRole('checkbox', { name: 'SMS', exact: true }).check();
    let stored = await save(id);
    assert.deepEqual(stored.captureVersions, { schemaVersion: '1.0', questionSetVersion: product.questionSetVersion, referenceDataVersion: product.referenceDataVersion });
    assert.deepEqual(stored.proposal.insured.title, { collection: 'proposerTitles', version: product.referenceDataVersion, value: 5, label: 'Dr' });
    assert.equal(stored.proposal.insured.declaredCompanyType.value, 3); assert.equal(stored.proposal.insured.declaredCompanyType.label, 'Private Limited Company');
    assert.equal(question(stored, 'MTS-01-Q01').value, false); assert.equal(question(stored, 'MTS-01-Q02').value[0].value, 1);
    assert.deepEqual(question(stored, 'MTS-01-Q03').value.map(item => item.value).sort(), [1, 2]);
    await page.reload(); await field('Proposer title').waitFor(); assert.equal(await field('Proposer title').inputValue(), '4');
    assert.equal(await field('Consent to collect quotation data').inputValue(), 'false');
    assert.equal(await page.getByRole('checkbox', { name: 'Email', exact: true }).isChecked(), true);
    await page.getByRole('button', { name: 'Record no marketing consent', exact: true }).click();
    stored = await save(id); assert.deepEqual(question(stored, 'MTS-01-Q02').value, []);
    assert.equal(question(stored, 'MTS-01-Q03').value.length, 2); // Explicitly retained, never silently removed.
    await page.getByRole('button', { name: 'Clear marketing consent answer', exact: true }).click();
    stored = await save(id); assert.equal(question(stored, 'MTS-01-Q02'), undefined);
    assert.equal(question(stored, 'MTS-01-Q01').value, false); assert.equal(stored.proposal.insured.firstName, 'Alex');
    const beforeMismatch = stored.proposal;
    await page.route(route, async intercepted => {
      const response = await intercepted.fetch(); const body = await response.json();
      body.captureVersions.referenceDataVersion = 'future-version'; await intercepted.fulfill({ response, json: body });
    });
    await page.reload(); await page.getByRole('alert').filter({ hasText: 'catalogue matching this saved quote' }).waitFor();
    assert.equal(await field('Proposer title').count(), 0); assert.equal(await page.getByRole('checkbox', { name: 'Email', exact: true }).count(), 0);
    assert.deepEqual((await read(id)).proposal, beforeMismatch);
    await page.unroute(route); await page.reload(); await field('Proposer title').waitFor();
    await page.locator('main').focus(); await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({ path: `${output}/${product.productCode}-desktop.png`, fullPage: true });
    await page.setViewportSize({ width: 390, height: 844 }); await page.evaluate(() => window.scrollTo(0, 0));
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.screenshot({ path: `${output}/${product.productCode}-mobile.png`, fullPage: true });
    report.push({ id, reference: stored.reference, productCode: product.productCode, revisionNumber: stored.revisionNumber });
    await page.setViewportSize({ width: 1560, height: 1000 });
  }
  assert.equal(report.length, 2); assert.deepEqual(errors, []);
  await writeFile(`${output}/report.json`, JSON.stringify(report, null, 2)); console.log(JSON.stringify(report, null, 2));
} catch (failure) { await page.screenshot({ path: `${output}/failure.png`, fullPage: true }); await writeFile(`${output}/failure.txt`, await page.locator('main').innerText()); throw failure; }
finally { await browser.close(); }
