import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir, open, unlink } from 'node:fs/promises';
import { chromium } from 'playwright';
import { openDemoJournal } from './demo-command-journal.mjs';

const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const directory = '.local/servicing-lapse-demo-v1'; await mkdir(directory, { recursive: true });
const lock = await open(directory + '/running.lock', 'wx'); let browser;
try {
 await lock.writeFile(String(process.pid));
 let fixtures;
 try { fixtures = JSON.parse(await readFile(directory + '/fixtures.json', 'utf8')); }
 catch (error) {
  if (error.code !== 'ENOENT') throw error;
  fixtures = JSON.parse(await readFile('.local/browser-evidence/renewal-issue/report.json', 'utf8')).journeys;
  await writeFile(directory + '/fixtures.json', JSON.stringify(fixtures, null, 2), { flag: 'wx' });
 }
 assert.equal(new Set(fixtures.map(x => x.productCode)).size, 2);
 const journal = await openDemoJournal(directory + '/commands.json', origin);
 browser = await chromium.launch({ channel: 'chrome', headless: true });
 const page = await browser.newPage({ viewport: { width: 1560, height: 1000 } });
 await page.goto(origin + '/login'); await page.getByLabel('Email address', { exact: true }).fill('underwriter@cover.example');
 await page.getByLabel('Password', { exact: true }).fill((await readFile('.local/demo-password.txt', 'utf8')).trim());
 await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(origin + '/');
 async function get(path) { const response = await page.request.get(origin + path); assert.equal(response.status(), 200, path); return { data: await response.json(), etag: response.headers().etag }; }
 const results = [];
 for (const fixture of fixtures) {
  const root = '/api/v1/policies/' + fixture.policyId;
  const before = (await get(root)).data, termId = fixture.receipt.termId;
  assert.notEqual(termId, before.termId, 'This demo records non-renewal of the retained future term, preserving current cover.');
  const lifecyclePath = `/api/v1/terms/${termId}/renewal-lifecycle`;
  await journal.command(fixture.productCode + ':lapse', async () => {
   const view = await get(lifecyclePath); assert.equal(view.data.canLapse, true);
   return { etag: view.etag, reason: 'Fictional future-term non-renewal: preserve all issued cover through its original expiry' };
  }, async (request, key) => {
   const token = (await get('/api/v1/auth/csrf')).data.requestToken;
   const response = await page.request.post(origin + `/api/v1/terms/${termId}/lapse`, { headers: { 'X-CSRF-Token': token, 'Idempotency-Key': key, 'If-Match': request.etag }, data: { reason: request.reason } });
   assert.ok(response.ok(), `Manual lapse returned HTTP ${response.status()}`); return response.json();
  });
  let lifecycle;
  for (let attempt = 0; attempt < 120; attempt++) {
   lifecycle = (await get(lifecyclePath)).data;
   if (lifecycle.notificationState === 'succeeded') break;
   await new Promise(resolve => setTimeout(resolve, 250));
  }
  assert.equal(lifecycle.state, 'lapsed'); assert.equal(lifecycle.lapseMode, 'manual');
  assert.equal(lifecycle.notificationState, 'succeeded'); assert.ok(lifecycle.notificationAttempts.length > 0);
  const after = (await get(root)).data;
  assert.equal(after.versionId, before.versionId); assert.equal(after.contentHash, before.contentHash);
  assert.deepEqual(after.snapshot, before.snapshot); assert.deepEqual(after.financials, before.financials);
  await page.goto(origin + `/policies/${fixture.policyId}?termId=${termId}&versionId=${fixture.receipt.versionId}`);
  const panel = page.getByRole('region', { name: 'Renewal lifecycle', exact: true });
  await panel.getByText('Renewal lapsed', { exact: true }).waitFor();
  await panel.getByText('Existing cover ends at its original expiry. Lapse creates no new policy term.', { exact: true }).waitFor();
  await panel.screenshot({ path: `${directory}/${fixture.productCode}-desktop.png` });
  await page.setViewportSize({ width: 390, height: 844 }); assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  await panel.screenshot({ path: `${directory}/${fixture.productCode}-mobile.png` }); await page.setViewportSize({ width: 1560, height: 1000 });
  results.push({ productCode: fixture.productCode, policyId: fixture.policyId, termId, lapseEventId: lifecycle.lapseEventId,
   effectiveAt: lifecycle.timeline.expiringEnd, currentCoverPreserved: true, demoNotification: lifecycle.notificationState });
 }
 await writeFile(directory + '/report.json', JSON.stringify({ completedAt: new Date().toISOString(), results }, null, 2));
 console.log('Both future-term manual lapse examples retain current cover and have successful persisted demo notifications.');
} finally { await browser?.close(); await lock.close(); await unlink(directory + '/running.lock'); }
