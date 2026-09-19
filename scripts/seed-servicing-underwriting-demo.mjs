import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir, open, unlink } from 'node:fs/promises';
import { chromium } from 'playwright';
import { openDemoJournal } from './demo-command-journal.mjs';

const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const directory = '.local/servicing-underwriting-demo-v1'; await mkdir(directory, { recursive: true });
const lock = await open(directory + '/running.lock', 'wx'); let browser;
try {
 await lock.writeFile(String(process.pid)); browser = await chromium.launch({ channel: 'chrome', headless: true });
 const journal = await openDemoJournal(directory + '/commands.json', origin);
 let fixtures;
 try { fixtures = JSON.parse(await readFile(directory + '/fixtures.json', 'utf8')); }
 catch (error) {
  if (error.code !== 'ENOENT') throw error;
  fixtures = JSON.parse(await readFile('.local/browser-evidence/underwriting-issue/report.json', 'utf8')).journeys;
  await writeFile(directory + '/fixtures.json', JSON.stringify(fixtures, null, 2), { flag: 'wx' });
 }
 assert.equal(new Set(fixtures.map(x => x.productCode)).size, 2);
 const page = await browser.newPage({ viewport: { width: 1560, height: 1000 } });
 await page.goto(origin + '/login'); await page.getByLabel('Email address', { exact: true }).fill('underwriter@cover.example');
 await page.getByLabel('Password', { exact: true }).fill((await readFile('.local/demo-password.txt', 'utf8')).trim());
 await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(origin + '/');
 async function get(path) { const response = await page.request.get(origin + path); assert.equal(response.status(), 200, path); return { data: await response.json(), etag: response.headers().etag }; }
 async function command(name, path, data, etagPath, lease, method = 'POST', file) {
  return journal.command(name, async () => ({ path, data, lease, method, file, etag: (await get(etagPath)).etag }), async (request, key) => {
   const csrf = (await get('/api/v1/auth/csrf')).data.requestToken;
   const response = await page.request.fetch(origin + request.path, { method: request.method, headers: {
    'X-CSRF-Token': csrf, 'Idempotency-Key': key, 'If-Match': request.etag, ...(request.lease ? { 'X-Edit-Lease': request.lease } : {}),
   }, ...(request.file ? { multipart: { fileName: request.file.name, contentType: 'text/plain', file: {
    name: request.file.name, mimeType: 'text/plain', buffer: Buffer.from(request.file.content),
   } } } : { data: request.data }) });
   assert.ok(response.ok(), `${request.path}: HTTP ${response.status()} ${response.ok() ? '' : await response.text()}`); return response.json();
  });
 }
 async function until(path, accept) {
  for (let attempt = 0; attempt < 180; attempt++) { const data = (await get(path)).data; if (accept(data)) return data; await new Promise(resolve => setTimeout(resolve, 300)); }
  throw Error('Expected persisted demo outcome did not arrive: ' + path);
 }
 const results = [];
 for (const fixture of fixtures) {
  const prefix = fixture.productCode, policyPath = '/api/v1/policies/' + fixture.policyId;
  const before = (await get(policyPath)).data, history = (await get(policyPath + '/history')).data;
  const basis = history.versions.find(x => x.termId === before.termId); assert.ok(basis);
  const localDate = new Date(basis.effectiveAt).toLocaleDateString('en-CA', { timeZone: 'Europe/London' });
  const termPath = `/api/v1/terms/${basis.termId}/drafts`;
  const created = await command(prefix + ':create', termPath, { kind: 'adjustment', baseVersionId: basis.id,
   commonEffectiveIntent: { localDate, localTime: '12:00', timeZone: 'Europe/London' }, reason: 'Fictional missing evidence and conditional carrier permission demonstration' }, termPath);
  const route = '/api/v1/drafts/' + created.id;
  let acquired = await command(prefix + ':lease', route + '/lease', { mode: 'acquire' }, route);
  const retainedCommands = JSON.parse(await readFile(directory + '/commands.json', 'utf8')).commands;
  for (const [name, entry] of Object.entries(retainedCommands)) {
   if (name.startsWith(prefix + ':reacquire/') && entry.result) acquired = entry.result;
  }
  const currentDraft = (await get(route)).data;
  if (Date.parse(currentDraft.lease?.expiresAt ?? '') < Date.now()) {
   acquired = await command(prefix + ':reacquire/' + Date.parse(currentDraft.lease.expiresAt), route + '/lease', { mode: 'acquire' }, route);
  }
  const lease = acquired.lease.leaseToken;
  const capture = (await get(route + '/editor')).data.assessment.base;
  const proposal = structuredClone((await get(route)).data.proposal), business = structuredClone(capture.risk.business), cover = structuredClone(capture.cover);
  business.startedOn = '2025-01-01';
  const tools = cover.requestedSections.find(x => x.code === 'tools-equipment'); assert.ok(tools);
  Object.assign(tools, { selected: true, limit: '15000.00', excess: '250.00' });
  proposal.changes = [
   { changeId: crypto.randomUUID(), riskItemId: fixture.policyId, kind: 'business', operation: 'update', payloadMode: 'replace', payload: business },
   { changeId: crypto.randomUUID(), riskItemId: fixture.policyId, kind: 'cover', operation: 'update', payloadMode: 'replace', payload: cover },
  ];
  // v1 was rejected with 422: issued cover contains derived fields absent from
  // the capture schema. Preserve that request and save corrected capture v2.
  await command(prefix + ':proposal-v3', route + '/proposal', proposal, route, lease, 'PUT');
  await command(prefix + ':rate', route + '/rate', { revisionId: (await get(route)).data.revisionId, reason: 'Assess saved fictional trading-history and tools changes' }, route, lease);
  const ratings = await until(route + '/ratings', x => x.current?.result?.outcome === 'rated');
  const refs = (await get(route + '/referrals')).data, referral = refs.items.find(x => x.ruleCode === 'cover-tools-equipment');
  assert.ok(referral); assert.ok(refs.items.some(x => x.ruleCode === 'UW-22' && !x.decisionReady));
  const cycleId = refs.cycleId;
  const carrier = await command(prefix + ':case', route + '/capacity', { cycleId, referralId: referral.id, referralEtag: referral.etag,
   reason: 'Request fictional tools capacity for the exact saved adjustment' }, route, lease);
  const casePath = route + '/capacity/' + carrier.id;
  let detail = (await get(casePath)).data;
  const scenario = detail.scenarios.find(x => /query/i.test(x.label)); assert.ok(scenario);
  await command(prefix + ':submit', casePath + '/submissions', { cycleId, caseEtag: detail.case.etag, body: 'Fictional tools schedule requires GBP15000 capacity',
   reason: 'Submit fictional schedule to the deterministic carrier adapter', evidenceAssociationIds: [], scenarioVersionId: scenario.id }, route, lease);
  detail = await until(casePath, x => x.case.state === 'queried' || x.case.state === 'conditional');
  if (detail.case.state !== 'conditional') {
   const required = (await get(route + '/evidence/requirements')).data.requirements.find(x => x.requirement.code === 'capacity-response').requirement;
   const file = await command(prefix + ':upload', route + '/evidence/uploads', undefined, route, lease, 'POST',
    { name: 'fictional-carrier-permission.txt', content: 'Fictional supplied carrier permission: tools limit GBP15000, subject to reviewed trading history.' });
   const attached = await command(prefix + ':attach', route + '/evidence', { cycleId, fileId: file.id, requirementCode: required.code,
    inputFingerprint: required.inputFingerprint, ...(required.riskItemId ? { riskItemId: required.riskItemId } : {}), reason: 'Attach the supplied fictional carrier permission' }, route, lease);
   const associations = (await get(route + `/evidence?cycleId=${cycleId}`)).data;
   const association = associations.items.find(x => x.id === attached.id); assert.ok(association);
   await command(prefix + ':review', route + `/evidence/${attached.id}/reviews`, { cycleId, associationEtag: association.etag,
    expectedFingerprint: required.inputFingerprint, outcome: 'accepted', reason: 'Review fictional response against this exact carrier case and schedule' }, route, lease);
   detail = (await get(casePath)).data;
   await command(prefix + ':response-v3', casePath + '/responses', { cycleId, caseEtag: detail.case.etag, submissionId: detail.case.currentSubmissionId,
    evidenceAssociationId: attached.id, definition: { outcome: 'approve-with-conditions', validFrom: new Date(Date.now() - 60000).toISOString(),
     validTo: '2027-12-31T23:00:00Z', authorisedLimits: [{ dimension: 'tools-limit', maximumAmount: '15000.00' }],
     conditions: [{ definition: { code: 'provide-trading-history' }, effectiveDates: ratings.current.result.slices.map(x => x.effectiveAt) }] },
    body: 'Fictional tools permission remains conditional on reviewed trading history', providerUnderwriter: 'Fictional carrier underwriter', providerReference: 'DEMO-PENDING-TRADING-PROOF',
    receivedAt: new Date().toISOString(), reason: 'Retain supplied conditional carrier permission for the business demonstration' }, route, lease);
  }
  detail = (await get(casePath)).data; assert.equal(detail.case.state, 'conditional'); assert.equal(detail.ready, false);
  assert.ok(detail.conditions.some(x => !x.satisfied));
  const missing = (await get(route + '/evidence/requirements')).data.requirements.filter(x => !x.satisfied);
  assert.ok(missing.some(x => x.requirement.code === 'trading-history'));
  const after = (await get(policyPath)).data; assert.equal(after.contentHash, before.contentHash); assert.deepEqual(after.financials, before.financials);
  await page.goto(origin + `/drafts/${created.id}`); await page.getByRole('heading', { name: 'Read-only draft', exact: true }).waitFor();
  const row = page.locator(`[data-referral-id="${referral.id}"]`);
  await row.getByText('Carrier capacity request', { exact: true }).click();
  const panel = row.getByRole('region', { name: 'Servicing carrier case', exact: true });
  await panel.getByText('conditional', { exact: true }).waitFor();
  await panel.screenshot({ path: `${directory}/${prefix}-desktop.png` });
  await page.setViewportSize({ width: 390, height: 844 }); assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
  await panel.screenshot({ path: `${directory}/${prefix}-mobile.png` }); await page.setViewportSize({ width: 1560, height: 1000 });
  results.push({ productCode: prefix, policyId: fixture.policyId, draftId: created.id, caseId: carrier.id, state: detail.case.state,
   missingEvidence: missing.map(x => x.requirement.code), issuedCoverPreserved: true });
 }
 await writeFile(directory + '/report.json', JSON.stringify({ completedAt: new Date().toISOString(), results }, null, 2));
 console.log('Both demo products retain missing trading proof, internal referrals, carrier query history and conditional permission.');
} finally { await browser?.close(); await lock.close(); await unlink(directory + '/running.lock'); }
