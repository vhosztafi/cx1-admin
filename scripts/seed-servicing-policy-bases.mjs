import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir, open, unlink } from 'node:fs/promises';
import { chromium } from 'playwright';
import { openDemoJournal } from './demo-command-journal.mjs';
import { resolve, sep } from 'node:path';

// Additive bases for cancellation demonstrations. Every policy passes normal
// rating, reviewed proof, authority, delivery, acceptance and atomic issue APIs.
const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const purpose = process.env.COVER_SERVICING_BASE_PURPOSE ?? 'cancellation';
assert.ok(['cancellation', 'acceptance'].includes(purpose));
const directory = process.env.COVER_SERVICING_BASE_DIRECTORY ?? '.local/servicing-policy-bases-v1';
assert.ok(resolve(directory).startsWith(resolve('.local') + sep), 'Seed journal must stay in the local workspace.');
await mkdir(directory, { recursive: true });
const lock = await open(directory + '/running.lock', 'wx');
let browser;
try {
 await lock.writeFile(String(process.pid));
 browser = await chromium.launch({ channel: 'chrome', headless: true });
 const journal = await openDemoJournal(directory + '/commands.json', origin);
 const start = new Date(Date.parse(journal.createdAt) + 86400000).toISOString().slice(0, 10);
 const fixtures = JSON.parse(await readFile('.local/browser-evidence/underwriting-rating/report.json', 'utf8')).journeys;
 const proposals = JSON.parse(await readFile('contracts/examples/underwriting-demo.json', 'utf8')).proposals;
 const page = await browser.newPage();
 await page.goto(origin + '/login'); await page.getByLabel('Email address', { exact: true }).fill('underwriter@cover.example');
 await page.getByLabel('Password', { exact: true }).fill((await readFile('.local/demo-password.txt', 'utf8')).trim());
 await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(origin + '/');
 async function get(path) {
  const response = await page.request.get(origin + path); assert.equal(response.status(), 200, path);
  return { data: await response.json(), etag: response.headers().etag };
 }
 async function send(request, key) {
  assert.ok(request.path.startsWith('/api/v1/'));
  const token = (await get('/api/v1/auth/csrf')).data.requestToken;
  const response = await page.request.post(origin + request.path, { headers: { 'X-CSRF-Token': token, 'Idempotency-Key': key,
   ...(request.etag ? { 'If-Match': request.etag } : {}) }, ...(request.file ? { multipart: {
    fileName: request.file.name, contentType: 'text/plain', file: { name: request.file.name, mimeType: 'text/plain', buffer: Buffer.from(request.file.content) },
   } } : { data: request.data }) });
  assert.ok(response.ok(), `${request.path}: HTTP ${response.status()}`);
  return response.json();
 }
 const command = (name, path, data, etagPath, file) => journal.command(name, async () => ({ path, data, file,
  ...(etagPath ? { etag: (await get(etagPath)).etag } : {}) }), send);
 async function until(path, accept) {
  const deadline = Date.now() + 90000;
  do { const data = (await get(path)).data; if (accept(data)) return data; await new Promise(resolve => setTimeout(resolve, 300)); } while (Date.now() < deadline);
  throw Error(`Demo worker did not complete: ${path}`);
 }
 async function proof(prefix, route, purpose) {
  assert.ok(purpose, 'Expected current evidence purpose');
  const step = `${prefix}:proof:${purpose.code}:${purpose.riskItemId ?? 'policy'}:${purpose.termsVersionId ?? 'risk'}`;
  const file = await command(step + ':upload', route + '/underwriting/evidence-files', undefined, route,
   { name: `fictional-${purpose.code}.txt`, content: `Fictional demonstration evidence for ${purpose.code}. No genuine personal information.` });
  const assessment = (await get(route + '/underwriting')).data;
  const association = await command(step + ':attach', route + '/underwriting/evidence', {
   cycleId: assessment.context.cycleId, fileId: file.id, requirementCode: purpose.code, inputFingerprint: purpose.inputFingerprint,
   ...(purpose.riskItemId ? { riskItemId: purpose.riskItemId } : {}), ...(purpose.conditionId ? { conditionId: purpose.conditionId } : {}),
   ...(purpose.termsVersionId ? { termsVersionId: purpose.termsVersionId } : {}), reason: 'Supplied fictional servicing demonstration proof',
  }, route);
  const saved = (await get(route + '/underwriting/evidence')).data.items.find(x => x.id === association.id); assert.ok(saved);
  await command(step + ':review', route + `/underwriting/evidence/${saved.id}/reviews`, { cycleId: assessment.context.cycleId,
   associationEtag: saved.etag, expectedFingerprint: purpose.inputFingerprint, outcome: 'accepted', reason: 'Reviewed fictional proof against its exact current purpose' }, route);
  return saved;
 }
 const policies = [];
 for (const productCode of ['motor-trade-combined', 'motor-trade-road-risks']) {
  const fixture = fixtures.find(x => x.productCode === productCode); assert.ok(fixture);
  const source = (await get('/api/v1/quotes/' + fixture.quoteId)).data;
  const proposal = structuredClone(proposals[productCode]); proposal.termIntent.localStartDate = start;
  proposal.risk.business.startedOn = '2010-01-01';
  if (productCode === 'motor-trade-combined') proposal.cover.requestedSections.find(x => x.code === 'stock-custody').limit = '90000.00';
  const prefix = `${purpose}-base:${productCode}`;
  const quote = await command(prefix + ':create', '/api/v1/quotes', { relationshipId: source.relationshipId, productVersionId: source.productVersionId, proposal });
  const route = '/api/v1/quotes/' + quote.id;
  const existing = (await get(route)).data;
  if (existing.boundPolicyId) {
   const policy = (await get('/api/v1/policies/' + existing.boundPolicyId)).data;
   assert.equal(policy.sourceQuoteId, quote.id); policies.push({ productCode, quoteId: quote.id, policyId: policy.id, termId: policy.termId, versionId: policy.versionId }); continue;
  }
  for (const vehicle of proposal.risk.vehicles) {
   const draft = (await get(route)).data;
   const request = await command(prefix + ':lookup:' + vehicle.id, route + '/lookups', { revisionId: draft.revisionId, kind: 'vehicle', scope: 'vehicle', riskItemId: vehicle.id, scenario: 'no-match' }, route);
   const data = await until(route + '/lookups', x => x.items.some(item => item.id === request.id && item.state === 'no-match'));
   const lookup = data.items.find(x => x.id === request.id);
   await command(prefix + ':selection:' + vehicle.id, route + '/lookup-selections', { lookupId: lookup.id, revisionId: draft.revisionId,
    inputFingerprint: lookup.inputFingerprint, manualReason: 'Fictional servicing policy base' }, route);
  }
  await command(prefix + ':rate', route + '/rate', { revisionId: (await get(route)).data.revisionId, reason: 'Rate fictional servicing demonstration cover' }, route);
  let assessment = await until(route + '/underwriting', x => !!x.ratingId);
  for (const purpose of assessment.proofRequirements.filter(x => !x.satisfied)) await proof(prefix, route, purpose);
  const referrals = (await get('/api/v1/referrals?quoteId=' + quote.id)).data.items;
  if (referrals.length) await command(prefix + ':decisions', route + '/referral-decisions', { cycleId: assessment.context.cycleId,
   decisions: referrals.map(x => ({ referralId: x.id, etag: x.etag, outcome: 'approve', reason: 'Current authority and reviewed fictional servicing-base proof' })) }, route);
  let history = (await get(route + '/terms')).data;
  await command(prefix + ':terms', route + '/terms/prepare', { cycleId: assessment.context.cycleId, ratingId: assessment.ratingId, templateVersionId: history.templates[0].id }, route);
  assessment = (await get(route + '/underwriting')).data;
  await proof(prefix, route, assessment.proofRequirements.find(x => x.code === 'signed-statement'));
  history = (await get(route + '/terms')).data; const terms = history.terms[0], recipient = history.recipientOptions[0]; assert.ok(recipient);
  await command(prefix + ':deliver', route + '/terms', { termsVersionId: terms.id, recipientContactIds: [recipient.id] }, route);
  await until(route + '/terms', x => x.deliveries.some(item => item.termsVersionId === terms.id && item.state === 'delivered'));
  assessment = (await get(route + '/underwriting')).data;
  const acceptedProof = await proof(prefix, route, assessment.proofRequirements.find(x => x.code === 'acceptance-proof'));
  assessment = (await get(route + '/underwriting')).data;
  await command(prefix + ':accept', route + '/acceptances', { cycleId: assessment.context.cycleId, ratingId: assessment.ratingId, termsVersionId: terms.id,
   termsHash: assessment.termsHash, assuranceHash: assessment.assuranceHash, accepterLabel: 'Fictional servicing demonstration customer',
   acceptedAt: new Date().toISOString(), channel: 'written', evidenceAssociationId: acceptedProof.id }, route);
  assessment = (await get(route + '/underwriting')).data; assert.equal(assessment.capabilities.canIssue, true);
  const receipt = await command(prefix + ':issue', route + '/issue', { cycleId: assessment.context.cycleId, ratingId: assessment.ratingId,
   acceptanceId: assessment.acceptanceId, termsHash: assessment.termsHash, assuranceHash: assessment.assuranceHash, reason: `Issue fictional base for servicing ${purpose} demonstration` }, route);
  const policy = (await get('/api/v1/policies/' + receipt.policyId)).data;
  assert.equal(policy.sourceQuoteId, quote.id); assert.equal(policy.snapshot.productCode, productCode);
  policies.push({ productCode, quoteId: quote.id, policyId: policy.id, termId: policy.termId, versionId: policy.versionId });
 }
 await writeFile(directory + '/policies.json', JSON.stringify({ completedAt: new Date().toISOString(), policies }, null, 2));
 await writeFile(directory + '/fixtures.json', JSON.stringify({ journeys: policies }, null, 2));
 console.log('Two additive servicing policy bases issued through reviewed proof and acceptance.');
} finally { await browser?.close(); await lock.close(); await unlink(directory + '/running.lock'); }
