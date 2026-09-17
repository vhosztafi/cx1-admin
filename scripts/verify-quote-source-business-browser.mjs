import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';

const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const password = process.env.COVER_DEMO_PASSWORD ?? (await readFile('.local/demo-password.txt', 'utf8')).trim();
const output = '.local/browser-evidence/quote-source-business'; await mkdir(output, { recursive: true });
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
const questionRows = JSON.parse(await readFile('contracts/quote-question-catalogue.json', 'utf8')).mappings;
const referenceSource = JSON.parse(await readFile('contracts/reference-data/motor-trade-capture.json', 'utf8'));
try {
  await page.goto(`${origin}/quotes/new`); await page.waitForURL('**/login');
  await field('Email address').fill('servicing@cover.example'); await field('Password').fill(password); await button('Sign in').click(); await page.waitForURL(`${origin}/`);
  const relationshipId = '51000000-0000-4000-8000-000000000003';
  const products = (await (await page.request.get(`${origin}/api/v1/quote-products?relationshipId=${relationshipId}`)).json()).items.filter(product => product.captureEligible);
  for (const product of products) {
    const token = (await (await page.request.get(`${origin}/api/v1/auth/csrf`)).json()).requestToken;
    const created = await page.request.post(`${origin}/api/v1/quotes`, { headers: { 'X-CSRF-Token': token, 'Idempotency-Key': crypto.randomUUID() }, data: { relationshipId, productVersionId: product.productVersionId } });
    assert.equal(created.status(), 201); const id = (await created.json()).id;
    await page.goto(`${origin}/quotes/${id}/edit`);
    const expected = [];
    for (const stage of [2, 3]) {
      await button(stage === 2 ? '2 Proposer' : '3 Trade activities').click();
      const rows = questionRows.filter(row => row.questionId && row.products.includes(product.productCode) && (row.stages?.some(s => s.startsWith('Motor Trade') && s.endsWith(`:step-${stage}`)) || (stage === 3 && row.questionId === 'MTS-02-Q01')));
      for (const row of rows) {
        const label = row.label ?? 'Where are you trading from?'; let value;
        if (row.answerKind === 'boolean') { value = false; await field(label).selectOption('false'); }
        else if (row.answerKind === 'text') { value = `Fictional detail: ${label}`; await field(label).fill(value); }
        else {
          const binding = referenceSource.bindings.find(x => x.questionId === row.questionId);
          const collection = binding.collections[0]; const option = referenceSource.collections[collection][0];
          value = { collection, version: referenceSource.version, value: option.value, label: option.text };
          await field(label).selectOption({ label: option.text });
        }
        expected.push({ questionId: row.questionId, kind: row.answerKind, value });
      }
      await save(id);
    }
    let stored = await read(id); assert.equal(expected.length, 15);
    for (const answer of expected) assert.deepEqual(stored.proposal.risk.business.responses.answers.find(x => x.questionId === answer.questionId), answer);
    await page.reload(); await button('2 Proposer').click();
    assert.equal(await field('Main or normal occupation').inputValue(), 'Fictional detail: Main or normal occupation');
    assert.ok(stored.readiness.issues.some(x => x.code === 'inactive-main-occupation-retained'));
    await field('Full time or part time motor trader').selectOption({ label: 'Part time' });
    await save(id); await field('Full time or part time motor trader').selectOption({ label: 'Full time' });
    stored = await save(id); assert.ok(stored.readiness.issues.some(x => x.code === 'inactive-main-occupation-retained'));
    await field('Main or normal occupation').fill(''); stored = await save(id);
    assert.equal(stored.proposal.risk.business.responses.answers.some(x => x.questionId === 'prototype.quote-value.3fd9edd7e66e'), false);
    await page.getByRole('group', { name: 'Motor trader details', exact: true }).screenshot({ path: `${output}/${product.productCode}-desktop.png` });
    await button('3 Trade activities').click(); await field('Import or export of vehicles').selectOption('true');
    await field('Details of any Yes answers').fill(''); stored = await save(id);
    assert.ok(stored.readiness.issues.some(x => x.questionId === 'prototype.quote-value.909e1c6eff8c'));
    await field('Details of any Yes answers').fill('Fictional vehicle import activity'); stored = await save(id);
    assert.equal(stored.readiness.issues.some(x => x.questionId === 'prototype.quote-value.909e1c6eff8c'), false);
    await page.setViewportSize({ width: 390, height: 844 }); assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.evaluate(() => { document.activeElement?.blur(); window.scrollTo(0, 0); });
    await page.screenshot({ path: `${output}/${product.productCode}-mobile.png`, fullPage: true });
    const route = `${origin}/api/v1/quotes/${id}`;
    await page.route(route, async intercepted => { const response = await intercepted.fetch(); const body = await response.json(); body.captureVersions.questionSetVersion = 'future'; await intercepted.fulfill({ response, json: body }); });
    await page.reload(); await page.getByRole('alert').filter({ hasText: 'Source business questions need' }).waitFor();
    assert.equal(await field('Full time or part time motor trader').count(), 0); assert.deepEqual((await read(id)).proposal, stored.proposal);
    await page.unroute(route); report.push({ id, reference: stored.reference, productCode: product.productCode, revisionNumber: stored.revisionNumber });
    await page.setViewportSize({ width: 1560, height: 1000 });
  }
  assert.equal(report.length, 2); assert.deepEqual(errors, []);
  await writeFile(`${output}/report.json`, JSON.stringify(report, null, 2)); console.log(JSON.stringify(report, null, 2));
} catch (failure) { await page.screenshot({ path: `${output}/failure.png`, fullPage: true }); await writeFile(`${output}/failure.txt`, await page.locator('main').innerText()); throw failure; }
finally { await browser.close(); }
