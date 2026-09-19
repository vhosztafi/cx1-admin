import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { chromium } from 'playwright';

const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['127.0.0.1', 'localhost'].includes(new URL(origin).hostname));
const fixtures = JSON.parse(await readFile(process.argv[2] ?? '.local/servicing-demo-drafts.json', 'utf8'));
assert.equal(fixtures.length, 6);
assert.equal(new Set(fixtures.map(x => x.DraftId)).size, 6);
const output = `.local/servicing-demo-readback/${new Date().toISOString().replaceAll(/[:.]/g, '-')}`;
await mkdir(output, { recursive: true });
const browser = await chromium.launch({ channel: 'chrome', headless: true });
try {
 async function login(role) {
  const page = await browser.newPage({ viewport: { width: 1560, height: 1000 } });
  await page.goto(origin + '/login'); await page.getByLabel('Email address', { exact: true }).fill(role + '@cover.example');
  await page.getByLabel('Password', { exact: true }).fill((await readFile('.local/demo-password.txt', 'utf8')).trim());
  await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(origin + '/');
  return page;
 }
 const underwriter = await login('underwriter'), observer = await login('servicing');
 const results = [];
 for (const fixture of fixtures) {
  const page = fixture.Scenario === 'leased' ? observer : underwriter;
  const root = `/api/v1/drafts/${fixture.DraftId}`;
  const response = await page.request.get(origin + root); assert.equal(response.status(), 200);
  const draft = await response.json(); assert.equal(draft.policyId, fixture.PolicyId); assert.equal(draft.state, 'draft');
  assert.equal(draft.proposal.reason, `Fictional servicing demo v1: ${fixture.Scenario}`);
  const previewResponse = await page.request.get(origin + root + '/cancellation-preview'); assert.equal(previewResponse.status(), 200);
  const preview = await previewResponse.json();
  assert.equal(preview.blockers.includes('servicing-base-stale'), fixture.Scenario === 'historical-base');
  await page.goto(origin + `/drafts/${fixture.DraftId}`);
  if (fixture.Scenario === 'leased') {
   assert.equal(draft.lease.active, true); assert.ok(Date.parse(draft.lease.expiresAt) > Date.now(), 'Run fresh seeded-lease verification before it expires.');
   await page.getByText('Another editor holds this draft. Your local edits are retained.', { exact: true }).waitFor();
   assert.equal(await page.getByRole('button', { name: 'Acquire editing lease', exact: true }).isDisabled(), true);
  } else {
   assert.equal(draft.lease, null);
   await page.getByRole('button', { name: 'Acquire editing lease', exact: true }).waitFor();
  }
  await page.screenshot({ path: `${output}/${fixture.DraftId}-desktop.png`, fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  await page.screenshot({ path: `${output}/${fixture.DraftId}-mobile.png`, fullPage: true });
  await page.setViewportSize({ width: 1560, height: 1000 });
  results.push({ ...fixture, blockers: preview.blockers, verified: true });
 }
 await writeFile(`${output}/report.json`, JSON.stringify({ completedAt: new Date().toISOString(), results }, null, 2));
 console.log(`Six persisted demo drafts and stale-base blockers verified: ${output}/report.json`);
} finally { await browser.close(); }
