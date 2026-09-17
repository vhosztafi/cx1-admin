import {chromium} from 'playwright';
import assert from 'node:assert/strict';
import {readFile, writeFile, mkdir} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';

// Separate invocations deliberately require fresh cookies after the operator
// restarts only the locally recorded API/Next process IDs between these modes.
const mode = process.argv[2];
assert.ok(['capture', 'verify'].includes(mode)); assert.equal(process.argv.length, 3);
const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['localhost', '127.0.0.1'].includes(new URL(origin).hostname));
const directory = '.local/browser-evidence/underwriting-restart'; await mkdir(directory, {recursive: true});
const fixtures = JSON.parse(await readFile('.local/browser-evidence/underwriting-issue/report.json', 'utf8')).journeys;
assert.equal(new Set(fixtures.map(x => x.productCode)).size, 2);
const carrierFixture = JSON.parse(await readFile('.local/browser-evidence/underwriting-final/report.json', 'utf8'));
assert.ok(carrierFixture.obligationId && carrierFixture.documentRequestIds.length === 3); fixtures.push(carrierFixture);
const password = (await readFile('.local/demo-password.txt', 'utf8')).trim();
const browser = await chromium.launch({channel: 'chrome', headless: true});
const page = await browser.newPage({viewport: {width: 1560, height: 1000}});
const hash = value => createHash('sha256').update(JSON.stringify(value)).digest('hex');
const specification = JSON.parse(await readFile('contracts/openapi.json', 'utf8'));
const ajv = new Ajv2020({strict: true, allErrors: true}); addFormats(ajv); ajv.addFormat('binary', true);
const aliases = {}, schemaRoot = 'https://contracts.cover-mga.example/restart-runtime';
for (const name of ['policy', 'policy-draft', 'quote-draft', 'issued-policy']) {
  const schema = JSON.parse(await readFile(`contracts/schemas/${name}.schema.json`, 'utf8'));
  ajv.addSchema(schema); aliases[`./schemas/${name}.schema.json`] = schema.$id;
}
function relocate(value) {
  if (Array.isArray(value)) return value.map(relocate);
  if (value && typeof value === 'object') return Object.fromEntries(Object.entries(value).map(([key, item]) =>
    [key, key === '$ref' ? (aliases[item] ?? item.replace('#/components/schemas/', `${schemaRoot}#/$defs/`)) : relocate(item)]));
  return value;
}
ajv.addSchema({$id: schemaRoot, $defs: relocate(specification.components.schemas)});
const validateCapacity = ajv.compile({$ref: `${schemaRoot}#/$defs/UnderwritingEscalationView`});

async function get(path) {
  const result = await page.request.get(origin + path);
  assert.equal(result.status(), 200, `${path}: ${await result.text()}`);
  return result.json();
}
try {
  await page.goto(origin + '/login'); await page.getByLabel('Email address').fill('underwriter@cover.example');
  await page.getByLabel('Password', {exact: true}).fill(password);
  await page.getByRole('button', {name: 'Sign in', exact: true}).click(); await page.waitForURL(origin + '/');
  const snapshots = [];
  for (const fixture of fixtures) {
    const route = `/api/v1/policies/${fixture.policyId}`, policy = await get(route);
    assert.equal(policy.sourceQuoteId, fixture.quoteId); assert.equal(policy.reference, fixture.policyReference);
    assert.equal(policy.termId, fixture.termId); assert.equal(policy.versionId, fixture.versionId);
    assert.equal(policy.transactionId, fixture.transactionId); assert.equal(policy.financials.obligationId, fixture.obligationId);
    assert.deepEqual(policy.documentRequests.map(x => x.id).sort(), [...fixture.documentRequestIds].sort());
    const pennies = amount => BigInt(amount.replace('.', ''));
    const balance = policy.financials.lines.reduce((sum, line) => sum + (line.side === 'debit' ? 1n : -1n) * pennies(line.amount), 0n);
    assert.equal(balance, 0n);
    const hashes = {policy: hash(policy)};
    for (const suffix of [`/terms/${fixture.termId}`, `/terms/${fixture.termId}/versions/${fixture.versionId}`,
      `/terms/${fixture.termId}/transactions/${fixture.transactionId}`, `/terms/${fixture.termId}/obligations/${fixture.obligationId}`]) {
      const data = await get(route + suffix); assert.equal(hash(data), hashes.policy); hashes[suffix] = hash(data);
    }
    const quoteRoute = `/api/v1/quotes/${fixture.quoteId}`, quote = await get(quoteRoute);
    assert.equal(quote.state, 'bound'); assert.equal(quote.boundPolicyId, fixture.policyId);
    hashes.revision = hash(await get(`${quoteRoute}/revisions/${quote.revisionId}`));
    const terms = await get(quoteRoute + '/terms');
    const acceptance = terms.acceptances.find(x => x.id === policy.acceptanceId); assert.ok(acceptance);
    hashes.acceptance = hash(acceptance);
    // History pages are traversed; pagination metadata is deliberately excluded.
    for (const resource of ['underwriting/evidence', 'referrals', 'ratings']) {
      let cursor, items = [];
      do {
        const path = resource === 'referrals' ? `/api/v1/referrals?quoteId=${fixture.quoteId}&` : `${quoteRoute}/${resource}?`;
        const data = await get(`${path}pageSize=100${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`);
        items.push(...data.items); cursor = data.nextCursor;
      } while (cursor);
      hashes[resource] = hash(items);
    }
    if (fixture.escalationId) {
      const capacity = await get(`/api/v1/escalations/${fixture.escalationId}`);
      assert.ok(validateCapacity(capacity), JSON.stringify(validateCapacity.errors));
      assert.equal(capacity.quoteId, fixture.quoteId); assert.ok(capacity.actionHistory.length >= 3);
      assert.ok(capacity.attemptHistory.some(x => x.outcome === 'succeeded'));
      // Scope the stable history: similar cases may be added independently later.
      hashes.capacityActions = hash(capacity.actionHistory);
      hashes.capacityAttempts = hash(capacity.attemptHistory);
      hashes.capacityPointers = hash([capacity.currentSubmissionId, capacity.currentResponseId, capacity.state]);
      let cursor, messages = [];
      do {
        const data = await get(`/api/v1/escalations/${fixture.escalationId}/messages?pageSize=100${cursor ? '&cursor=' + encodeURIComponent(cursor) : ''}`);
        messages.push(...data.items); cursor = data.nextCursor;
      } while (cursor);
      assert.ok(messages.filter(x => x.provenance === 'supplied-response').length >= 2);
      hashes.capacityMessages = hash(messages);
    }
    await page.goto(origin + `/policies/${fixture.policyId}`);
    await page.getByRole('heading', {name: fixture.policyReference, exact: true}).waitFor();
    snapshots.push({policyId: fixture.policyId, productCode: fixture.productCode, contentHash: policy.contentHash, hashes});
  }
  const file = directory + '/report.json';
  if (mode === 'capture') await writeFile(file, JSON.stringify({capturedAt: new Date().toISOString(), snapshots}, null, 2));
  else {
    const previous = JSON.parse(await readFile(file, 'utf8')); assert.deepEqual(snapshots, previous.snapshots);
    await writeFile(file, JSON.stringify({...previous, verifiedAt: new Date().toISOString(), passed: true}, null, 2));
  }
  console.log(`${mode}: ${snapshots.length} persisted policy graphs across both products, balanced journals, documents, source revisions, acceptance and carrier history agree.`);
} finally {await browser.close();}
