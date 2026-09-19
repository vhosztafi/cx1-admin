import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { chromium } from 'playwright';
import Ajv from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';

const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const output = '.local/servicing-context/' + new Date().toISOString().replaceAll(/[:.]/g, '-');
await mkdir(output, { recursive: true });
const fixtures = JSON.parse(await readFile('.local/servicing-underwriting-demo-v1/report.json', 'utf8')).results;
assert.equal(new Set(fixtures.map(x => x.productCode)).size, 2);
const schema = JSON.parse(await readFile('contracts/schemas/servicing.schema.json', 'utf8'));
const ajv = new Ajv({ strict: false }); addFormats(ajv); ajv.addSchema(schema);
const validate = ajv.compile({ $ref: schema.$id + '#/$defs/ServicingDraft' });
const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
 const page = await browser.newPage({ viewport: { width: 1560, height: 1000 } });
 const errors = []; page.on('pageerror', error => errors.push(error.message));
 await page.goto(origin + '/login'); await page.getByLabel('Email address', { exact: true }).fill('servicing@cover.example');
 await page.getByLabel('Password', { exact: true }).fill((await readFile('.local/demo-password.txt', 'utf8')).trim());
 await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(origin + '/');
 async function get(path) { const response = await page.request.get(origin + path); assert.equal(response.status(), 200); return response.json(); }
 const results = [];
 for (const fixture of fixtures) {
  const draft = await get('/api/v1/drafts/' + fixture.draftId); assert.ok(validate(draft), JSON.stringify(validate.errors));
  const policy = await get(`/api/v1/policies/${draft.policyId}/terms/${draft.baseTermId}/versions/${draft.baseVersionId}`);
  assert.equal(draft.context.policyReference, policy.reference);
  assert.equal(draft.context.baseTermPremium, policy.snapshot.premium.termPremium);
  const rating = await get('/api/v1/drafts/' + fixture.draftId + '/ratings');
  const amount = BigInt(draft.context.baseTermPremium.replace('.', '')) + BigInt(rating.current.result.premium.replace('.', ''));
  assert.ok(amount >= 0n);
  const revised = `${amount / 100n}.${String(amount % 100n).padStart(2, '0')}`;
  const insured = policy.snapshot.insured;
  const name = insured.legalName ?? [insured.firstName, insured.surname].filter(Boolean).join(' '); assert.ok(name);
  await page.goto(origin + '/drafts/' + fixture.draftId);
  const panel = page.locator('[aria-label="Draft context"]');
  await panel.getByRole('link', { name: policy.reference, exact: true }).waitFor();
  await panel.getByText(name, { exact: true }).waitFor();
  await panel.getByText(draft.context.preparedBy.label, { exact: true }).waitFor();
  const total = page.locator('dl.underwriting-provenance > div').filter({ has: page.getByText('Proposed revised term premium', { exact: true }) });
  await total.getByText(new Intl.NumberFormat('en-GB', { style: 'currency', currency: 'GBP' }).format(Number(revised)), { exact: true }).waitFor();
  await panel.screenshot({ path: `${output}/${fixture.productCode}-desktop.png` });
  await page.setViewportSize({ width: 390, height: 844 });
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  await panel.screenshot({ path: `${output}/${fixture.productCode}-mobile.png` });
  await page.setViewportSize({ width: 1560, height: 1000 });
  const after = await get('/api/v1/drafts/' + fixture.draftId); assert.equal(after.revisionId, draft.revisionId);
  results.push({ productCode: fixture.productCode, draftId: draft.id, policyReference: policy.reference, originalPreparer: draft.context.preparedBy.id, revisedTermPremium: revised });
 }
 assert.deepEqual(errors, []);
 await writeFile(output + '/report.json', JSON.stringify({ completedAt: new Date().toISOString(), results }, null, 2));
 console.log('Both saved draft headers match authorised policy, issued insured and original preparer; contract and mobile checks passed.');
} finally { await browser.close(); }
