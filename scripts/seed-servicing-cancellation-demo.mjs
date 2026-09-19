import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir, open, unlink } from 'node:fs/promises';
import { chromium } from 'playwright';
import { openDemoJournal } from './demo-command-journal.mjs';

const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const directory = '.local/servicing-cancellation-demo-v1'; await mkdir(directory, { recursive: true });
const lock = await open(directory + '/running.lock', 'wx'); let browser;
try {
 await lock.writeFile(String(process.pid)); browser = await chromium.launch({ channel: 'chrome', headless: true });
 const journal = await openDemoJournal(directory + '/commands.json', origin);
 const fixtures = JSON.parse(await readFile('.local/servicing-policy-bases-v1/policies.json', 'utf8')).policies;
 assert.equal(fixtures.length, 2);
 const page = await browser.newPage({ viewport: { width: 1560, height: 1000 } });
 await page.goto(origin + '/login'); await page.getByLabel('Email address', { exact: true }).fill('underwriter@cover.example');
 await page.getByLabel('Password', { exact: true }).fill((await readFile('.local/demo-password.txt', 'utf8')).trim());
 await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(origin + '/');
 async function get(path) {
  const response = await page.request.get(origin + path); assert.equal(response.status(), 200, path);
  return { data: await response.json(), etag: response.headers().etag };
 }
 async function command(name, path, data, etagPath, lease, method = 'POST', file) {
  return journal.command(name, async () => ({ path, data, method, lease, file, etag: (await get(etagPath)).etag }), async (request, key) => {
   const csrf = (await get('/api/v1/auth/csrf')).data.requestToken;
   const response = await page.request.fetch(origin + request.path, { method: request.method, headers: {
    'X-CSRF-Token': csrf, 'Idempotency-Key': key, 'If-Match': request.etag, ...(request.lease ? { 'X-Edit-Lease': request.lease } : {}),
   }, ...(request.file ? { multipart: { fileName: request.file.name, contentType: 'text/plain', purpose: 'cancellation-request',
    file: { name: request.file.name, mimeType: 'text/plain', buffer: Buffer.from(request.file.content) } } } : { data: request.data }) });
   assert.ok(response.ok(), `${request.path}: HTTP ${response.status()}`); return response.json();
  });
 }
 const results = [];
 for (const fixture of fixtures) {
  const policy = (await get('/api/v1/policies/' + fixture.policyId)).data;
  const date = new Date(Date.parse(policy.snapshot.term.startsAt) + 30 * 86400000).toISOString().slice(0, 10);
  const prefix = fixture.productCode, drafts = `/api/v1/terms/${policy.termId}/drafts`;
  const created = await command(prefix + ':create', drafts, { kind: 'cancellation', baseVersionId: policy.versionId,
   commonEffectiveIntent: { localDate: date, localTime: '09:00', timeZone: 'Europe/London' }, reason: 'Fictional requested cancellation credit demonstration' }, drafts);
  const route = '/api/v1/drafts/' + created.id;
  if ((await get(route)).data.state !== 'issued') {
   const acquired = await command(prefix + ':lease', route + '/lease', { mode: 'acquire' }, route);
   const lease = acquired.lease.leaseToken;
   const proposal = structuredClone((await get(route)).data.proposal); proposal.cancellationReasonCode = 'insured-request';
   await command(prefix + ':proposal', route + '/proposal', proposal, route, lease, 'PUT');
   const uploaded = await command(prefix + ':upload', route + '/cancellation-evidence/uploads', undefined, route, lease, 'POST',
    { name: 'fictional-cancellation-request.txt', content: 'Fictional insured request for the exact dated cancellation. Demonstration only.' });
   await command(prefix + ':review', route + `/cancellation-evidence/${uploaded.resourceId}/reviews`,
    { outcome: 'accepted', reason: 'Reviewed fictional insured cancellation request and effective date' }, route, lease);
   let preview = (await get(route + '/cancellation-preview')).data; assert.deepEqual(preview.blockers, []);
   assert.ok(Number(preview.amounts.posting.invoiceDue) < 0, 'Expected an actual signed cancellation credit.');
   await command(prefix + ':prepare', route + '/cancellation-preview', { previewHash: preview.previewHash }, route, lease);
   preview = (await get(route + '/cancellation-preview')).data;
   await command(prefix + ':approve', route + '/cancellation-approvals', { previewId: preview.previewId, previewHash: preview.previewHash,
    reason: 'Approve the exact reviewed fictional cancellation and return premium' }, route, lease);
   preview = (await get(route + '/cancellation-preview')).data;
   await command(prefix + ':issue', route + '/cancellation-issue', { previewId: preview.previewId, approvalId: preview.approvalId,
    previewHash: preview.previewHash, reason: 'Issue the approved fictional cancellation credit with no cash payment' }, route, lease);
  }
  const issued = (await get(route + '/cancellation-issue')).data;
  assert.ok(Number(issued.netAmount) < 0); assert.equal((await get(route)).data.state, 'issued');
  const retained = (await get(`/api/v1/policies/${fixture.policyId}/terms/${issued.termId}/versions/${issued.versionId}`)).data;
  assert.equal(retained.financials.amountDue, issued.netAmount);
  const pennies = amount => BigInt(amount.replace('.', ''));
  assert.equal(retained.financials.lines.reduce((sum, line) => sum + (line.side === 'debit' ? 1n : -1n) * pennies(line.amount), 0n), 0n);
  await page.goto(origin + `/drafts/${created.id}`); await page.getByRole('heading', { name: 'Issued cancellation', exact: true }).waitFor();
  await page.screenshot({ path: `${directory}/${fixture.productCode}-desktop.png`, fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 }); assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  await page.screenshot({ path: `${directory}/${fixture.productCode}-mobile.png`, fullPage: true }); await page.setViewportSize({ width: 1560, height: 1000 });
  results.push({ ...fixture, draftId: created.id, versionId: issued.versionId, transactionId: issued.transactionId, netAmount: issued.netAmount });
 }
 await writeFile(directory + '/report.json', JSON.stringify({ completedAt: new Date().toISOString(), results }, null, 2));
 console.log('Both dedicated demo policies have actual approved cancellation credits and balanced posted journals.');
} finally { await browser?.close(); await lock.close(); await unlink(directory + '/running.lock'); }
