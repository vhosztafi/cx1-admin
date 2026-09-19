import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { createHash } from 'node:crypto';
import { chromium } from 'playwright';

const [mode, evidence] = process.argv.slice(2);
assert.ok(['capture', 'compare'].includes(mode) && evidence, 'Usage: node scripts/verify-servicing-restart.mjs capture|compare EVIDENCE_DIRECTORY');
const directory = resolve(evidence), origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const baselineFile = join(directory, 'policy-graph-before.json');
const prior = mode === 'compare' ? JSON.parse(await readFile(baselineFile, 'utf8')) : null;
if (mode === 'capture') await mkdir(directory, { recursive: false });
if (prior) assert.equal(prior.origin, origin);
const cutoff = prior?.cutoff ?? new Date().toISOString();
const fixtures = prior?.policies ?? JSON.parse(await readFile(process.env.COVER_POLICY_FIXTURES??'.local/browser-evidence/underwriting-issue/report.json', 'utf8')).journeys;
assert.equal(new Set(fixtures.map(x => x.productCode)).size, 2);
const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
 const page = await browser.newPage();
 await page.goto(origin + '/login'); await page.getByLabel('Email address', { exact: true }).fill('underwriter@cover.example');
 await page.getByLabel('Password', { exact: true }).fill((await readFile('.local/demo-password.txt', 'utf8')).trim());
 await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(origin + '/');
 async function get(path) {
  const response = await page.request.get(origin + path);
  assert.equal(response.status(), 200, path); assert.match(response.headers()['cache-control'] ?? '', /no-store/);
  return response.json();
 }
 const policies = [];
 for (const fixture of fixtures) {
  const root = `/api/v1/policies/${fixture.policyId}`;
  const history = await get(`${root}/history?effectiveAt=${encodeURIComponent(cutoff)}&knownAt=${encodeURIComponent(cutoff)}`);
  assert.ok(history.versions.length > 0);
  const versions = [];
  for (const version of history.versions) {
   const data = await get(`${root}/terms/${version.termId}/versions/${version.id}`);
   assert.equal(data.contentHash, version.contentHash);
   // Explicit-version reads report their request time; it is not stored policy
   // data. History above pins both cutoffs. Retain every other response field.
   for (const field of ['effectiveCutoff', 'knownCutoff']) {
    assert.ok(Number.isFinite(Date.parse(data[field])), `Missing ${field}`);
    delete data[field];
   }
   versions.push(data);
  }
  policies.push({ policyId: fixture.policyId, productCode: fixture.productCode, history, versions });
 }
 const result = { origin, cutoff, policies };
 if (prior) assert.deepEqual(result, prior, 'Exact retained versions, history, decisions and financial readback changed.');
 await writeFile(mode === 'capture' ? baselineFile : join(directory, 'policy-graph-after.json'), JSON.stringify(result, null, 2));
 await writeFile(join(directory, `${mode}-report.json`), JSON.stringify({ completedAt: new Date().toISOString(), mode, passed: true,
  policies: policies.length, versions: policies.reduce((sum, p) => sum + p.versions.length, 0),
  sha256: createHash('sha256').update(JSON.stringify(result)).digest('hex'),
  note: 'This checks fresh-login exact API readback. Record the separately verified process restart and SQL fingerprints alongside it.' }, null, 2));
 console.log(`${mode}: ${policies.length} policy graphs verified with a fresh web login.`);
} finally { await browser.close(); }
