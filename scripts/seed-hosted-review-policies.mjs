import assert from 'node:assert/strict';
import { mkdir, readFile, writeFile, open, unlink } from 'node:fs/promises';
import { chromium } from 'playwright';
import { openDemoJournal } from './demo-command-journal.mjs';

// Run only against the named password-gated staging site. Every business
// mutation uses an authenticated, CSRF-protected API command.
const origin = 'https://cx1-admin-dev.gyongyos.co.uk';
const sitePassword = process.env.COVER_SITE_PASSWORD;
const appPassword = process.env.COVER_APP_PASSWORD;
assert.ok(sitePassword && appPassword, 'Set COVER_SITE_PASSWORD and COVER_APP_PASSWORD privately.');
const directory = '.local/hosted-review-policies-v1';
await mkdir(directory, { recursive: true });
const lock = await open(`${directory}/running.lock`, 'wx');
let browser;
try {
  await lock.writeFile(String(process.pid));
  const journal = await openDemoJournal(`${directory}/commands.json`, origin, ['cx1-admin-dev.gyongyos.co.uk']);
  browser = await chromium.launch({ channel: 'chrome', headless: true });
  async function session(role) {
    const page = await browser.newPage();
    await page.goto(origin + '/');
    if (await page.getByRole('heading', { name: 'Private access' }).count()) {
      await page.getByLabel('Password', { exact: true }).fill(sitePassword);
      await page.getByRole('button', { name: 'Continue' }).click();
    }
    await page.goto(origin + '/login');
    await page.getByLabel('Email address', { exact: true }).fill(`${role}@cover.example`);
    await page.getByLabel('Password', { exact: true }).fill(appPassword);
    await page.getByRole('button', { name: 'Sign in', exact: true }).click();
    await page.waitForURL(origin + '/');
    return page;
  }
  const author = await session('agency-admin');
  const reviewer = await session('agency-reviewer');
  const underwriter = await session('underwriter');
  const servicing = await session('servicing');
  async function get(page, path) {
    const response = await page.request.get(origin + path);
    assert.equal(response.status(), 200, `${path}: HTTP ${response.status()}`);
    return { data: await response.json(), etag: response.headers().etag };
  }
  async function send(page, request, key) {
    const csrf = (await get(page, '/api/v1/auth/csrf')).data.requestToken;
    const response = await page.request.post(origin + request.path, {
      headers: { 'X-CSRF-Token': csrf, 'Idempotency-Key': key, ...(request.etag ? { 'If-Match': request.etag } : {}) },
      ...(request.file ? { multipart: { fileName: request.file.name, contentType: 'text/plain',
        file: { name: request.file.name, mimeType: 'text/plain', buffer: Buffer.from(request.file.content) } } } : { data: request.data }),
    });
    assert.ok(response.ok(), `${request.path}: HTTP ${response.status()} ${await response.text()}`);
    return response.json();
  }
  const command = (page, name, path, data, etagPath, file) => journal.command(name,
    async () => ({ path, data, file, ...(etagPath ? { etag: (await get(page, etagPath)).etag } : {}) }),
    (request, key) => send(page, request, key));
  async function until(page, path, accept) {
    const deadline = Date.now() + 90_000;
    let last;
    do {
      const data = (await get(page, path)).data;
      if (accept(data)) return data;
      last = data;
      await new Promise(resolve => setTimeout(resolve, 300));
    } while (Date.now() < deadline);
    const jobs = Array.isArray(last?.items) ? last.items.map(x => ({ state: x.state, workState: x.workState,
      attempts: x.attempts, errorCode: x.errorCode })) : { state: last?.state, errorCode: last?.errorCode };
    throw new Error(`Timed out waiting for ${path}; worker state: ${JSON.stringify(jobs)}`);
  }
  async function proof(prefix, route, purpose) {
    assert.ok(purpose, `Missing current proof purpose for ${prefix}`);
    const step = `${prefix}:proof:${purpose.code}:${purpose.riskItemId ?? 'policy'}:${purpose.termsVersionId ?? 'risk'}`;
    const file = await command(underwriter, `${step}:upload`, `${route}/underwriting/evidence-files`, undefined, route,
      { name: `fictional-${purpose.code}.txt`, content: `Fictional business review proof for ${purpose.code}. No real customer data.` });
    const assessment = (await get(underwriter, `${route}/underwriting`)).data;
    const attached = await command(underwriter, `${step}:attach`, `${route}/underwriting/evidence`, {
      cycleId: assessment.context.cycleId, fileId: file.id, requirementCode: purpose.code, inputFingerprint: purpose.inputFingerprint,
      ...(purpose.riskItemId ? { riskItemId: purpose.riskItemId } : {}),
      ...(purpose.conditionId ? { conditionId: purpose.conditionId } : {}),
      ...(purpose.termsVersionId ? { termsVersionId: purpose.termsVersionId } : {}),
      reason: 'Supplied fictional business review proof',
    }, route);
    const saved = (await get(underwriter, `${route}/underwriting/evidence`)).data.items.find(x => x.id === attached.id);
    assert.ok(saved);
    await command(underwriter, `${step}:review`, `${route}/underwriting/evidence/${saved.id}/reviews`, {
      cycleId: assessment.context.cycleId, associationEtag: saved.etag, expectedFingerprint: purpose.inputFingerprint,
      outcome: 'accepted', reason: 'Reviewed fictional proof against its current purpose',
    }, route);
    return saved;
  }

  const published = (await get(author, '/api/v1/agency-product-catalog')).data.items
    .filter(x => x.productCode.startsWith('motor-trade-') && x.distributionEligible);
  assert.equal(published.length, 2, 'Expected two published Motor Trade demo versions.');
  const examples = JSON.parse(await readFile('contracts/examples/underwriting-demo.json', 'utf8')).proposals;
  const today = new Intl.DateTimeFormat('en-CA', { timeZone: 'Europe/London', year: 'numeric', month: '2-digit', day: '2-digit' }).format(new Date());
  const shift = days => new Date(Date.parse(today + 'T12:00:00Z') + days * 86_400_000).toISOString().slice(0, 10);
  const startDate = shift(1);
  const marker = journal.createdAt.replaceAll(/[^0-9]/g, '').slice(0, 14);
  const details = JSON.parse(await readFile('scripts/fixtures/complete-agency.json', 'utf8'));
  details.legalName = `Fictional CX1 Review Agency ${marker}`;
  details.relationshipManagerId = (await get(author, '/api/v1/account')).data.id;
  details.commercialTerms.effectiveFrom = shift(-1);
  details.compliance.tobaSignedOn = shift(-1);
  details.compliance.piExpiresOn = shift(365);
  const agency = await command(author, 'review:agency:create', '/api/v1/agencies', {
    details, onboardingStep: 6,
    products: published.map(x => ({ productVersionId: x.productVersionId, effectiveFrom: shift(-1), brokerCommissionBasisPoints: 1250 })),
  });
  const agencyRoute = `/api/v1/agencies/${agency.id}`;
  await command(author, 'review:agency:invitation', `${agencyRoute}/invitations`, {
    displayName: 'Fictional CX1 review broker', email: `review-${marker}@cover.example`, role: 'broker-admin',
  }, agencyRoute);
  const agencyEvidence = await command(author, 'review:agency:file', `${agencyRoute}/evidence-files`, undefined, agencyRoute,
    { name: 'fictional-review-agency.txt', content: 'Fictional agency review evidence. No real customer information.' });
  for (const kind of ['toba', 'professional-indemnity', 'dpa', 'client-money'])
    await command(author, `review:agency:evidence:${kind}`, `${agencyRoute}/evidence`, {
      kind, fileId: agencyEvidence.id, notes: 'Fictional reviewed agency evidence',
      ...(kind === 'professional-indemnity' ? { expiresOn: shift(365) } : {}),
    }, agencyRoute);
  for (const kind of ['fca', 'financial-check', 'sanctions', 'ownership'])
    await command(author, `review:agency:check:${kind}`, `${agencyRoute}/checks`, { kind }, agencyRoute);
  await until(author, agencyRoute, x => x.validation.valid);
  const activation = await command(author, 'review:agency:activate', `${agencyRoute}/activate`,
    { reason: 'Fictional business review agency activation' }, agencyRoute);
  await command(reviewer, 'review:agency:approve', `/api/v1/agency-state-requests/${activation.id}/decision`,
    { outcome: 'approve', reason: 'Independent fictional business review approval' }, `/api/v1/agency-state-requests/${activation.id}`);

  const clientName = `Fictional CX1 Review Traders ${marker}`;
  const client = await command(servicing, 'review:client:create', '/api/v1/clients', {
    legalName: clientName, entityType: 'sole-trader',
    address: { line1: '1 Example Street', town: 'Example Town', postcode: 'AB1 2CD', country: 'GB' },
  });
  const relationship = await command(servicing, 'review:relationship:create', `/api/v1/clients/${client.id}/relationships`,
    { agencyId: agency.id }, `/api/v1/clients/${client.id}`);
  const relationshipId = relationship.id;
  const offers = (await get(underwriter, `/api/v1/quote-products?relationshipId=${relationshipId}`)).data.items
    .filter(x => x.captureEligible);
  assert.equal(offers.length, 2, 'The approved fictional agency must offer both published Motor Trade products.');
  const quotes = [];
  for (const offer of offers) {
    const productCode = offer.productCode;
    const proposal = structuredClone(examples[productCode]);
    proposal.termIntent.localStartDate = startDate;
    proposal.risk.business.startedOn = '2010-01-01';
    const quote = await command(underwriter, `review:${productCode}:create`, '/api/v1/quotes',
      { relationshipId, productVersionId: offer.productVersionId, proposal });
    quotes.push({ productCode, quoteId: quote.id });
  }
  await writeFile(`${directory}/fixtures.json`, JSON.stringify({ agencyId: agency.id, clientId: client.id,
    relationshipId, quotes }, null, 2));

  const policies = [];
  for (const { productCode, quoteId } of quotes) {
    const prefix = `review:${productCode}`;
    const route = `/api/v1/quotes/${quoteId}`;
    let saved = (await get(underwriter, route)).data;
    if (saved.boundPolicyId) {
      const policy = (await get(underwriter, `/api/v1/policies/${saved.boundPolicyId}`)).data;
      policies.push({ productCode, quoteId, policyId: policy.id, termId: policy.termId, versionId: policy.versionId });
      continue;
    }
    for (const vehicle of saved.proposal.risk.vehicles) {
      const lookup = await command(underwriter, `${prefix}:lookup:${vehicle.id}`, `${route}/lookups`,
        { revisionId: saved.revisionId, kind: 'vehicle', scope: 'vehicle', riskItemId: vehicle.id, scenario: 'no-match' }, route);
      const completed = await until(underwriter, `${route}/lookups`, x => x.items.some(item => item.id === lookup.id && item.state === 'no-match'));
      const result = completed.items.find(x => x.id === lookup.id);
      await command(underwriter, `${prefix}:selection:${vehicle.id}`, `${route}/lookup-selections`, {
        lookupId: result.id, revisionId: saved.revisionId, inputFingerprint: result.inputFingerprint,
        manualReason: 'Fictional vehicle reviewed for business demonstration',
      }, route);
      saved = (await get(underwriter, route)).data;
    }
    await command(underwriter, `${prefix}:rate`, `${route}/rate`,
      { revisionId: saved.revisionId, reason: 'Rate fictional business review policy' }, route);
    let underwriting = await until(underwriter, `${route}/underwriting`, x => !!x.ratingId);
    for (const purpose of underwriting.proofRequirements.filter(x => !x.satisfied)) await proof(prefix, route, purpose);
    const referrals = (await get(underwriter, `/api/v1/referrals?quoteId=${quoteId}`)).data.items;
    if (referrals.length) await command(underwriter, `${prefix}:decisions`, `${route}/referral-decisions`, {
      cycleId: underwriting.context.cycleId,
      decisions: referrals.map(x => ({ referralId: x.id, etag: x.etag, outcome: 'approve',
        reason: 'Current authority and reviewed fictional proof' })),
    }, route);
    let termsHistory = (await get(underwriter, `${route}/terms`)).data;
    await command(underwriter, `${prefix}:terms`, `${route}/terms/prepare`, {
      cycleId: underwriting.context.cycleId, ratingId: underwriting.ratingId, templateVersionId: termsHistory.templates[0].id,
    }, route);
    underwriting = (await get(underwriter, `${route}/underwriting`)).data;
    await proof(prefix, route, underwriting.proofRequirements.find(x => x.code === 'signed-statement'));
    termsHistory = (await get(underwriter, `${route}/terms`)).data;
    const termsVersion = termsHistory.terms[0];
    const recipient = termsHistory.recipientOptions[0];
    assert.ok(termsVersion && recipient, 'Terms and fictional recipient are required.');
    await command(underwriter, `${prefix}:deliver`, `${route}/terms`,
      { termsVersionId: termsVersion.id, recipientContactIds: [recipient.id] }, route);
    await until(underwriter, `${route}/terms`, x => x.deliveries.some(d => d.termsVersionId === termsVersion.id && d.state === 'delivered'));
    underwriting = (await get(underwriter, `${route}/underwriting`)).data;
    const acceptedProof = await proof(prefix, route, underwriting.proofRequirements.find(x => x.code === 'acceptance-proof'));
    underwriting = (await get(underwriter, `${route}/underwriting`)).data;
    await command(underwriter, `${prefix}:accept`, `${route}/acceptances`, {
      cycleId: underwriting.context.cycleId, ratingId: underwriting.ratingId, termsVersionId: termsVersion.id,
      termsHash: underwriting.termsHash, assuranceHash: underwriting.assuranceHash,
      accepterLabel: 'Fictional business review customer', acceptedAt: new Date().toISOString(),
      channel: 'written', evidenceAssociationId: acceptedProof.id,
    }, route);
    underwriting = (await get(underwriter, `${route}/underwriting`)).data;
    assert.equal(underwriting.capabilities.canIssue, true, `Issue blocked for ${productCode}`);
    const issued = await command(underwriter, `${prefix}:issue`, `${route}/issue`, {
      cycleId: underwriting.context.cycleId, ratingId: underwriting.ratingId,
      acceptanceId: underwriting.acceptanceId, termsHash: underwriting.termsHash,
      assuranceHash: underwriting.assuranceHash, reason: 'Issue fictional business review policy base',
    }, route);
    const policy = (await get(underwriter, `/api/v1/policies/${issued.policyId}`)).data;
    assert.equal(policy.sourceQuoteId, quoteId);
    policies.push({ productCode, quoteId, policyId: policy.id, termId: policy.termId, versionId: policy.versionId });
  }
  await writeFile(`${directory}/policies.json`, JSON.stringify({ completedAt: new Date().toISOString(), policies }, null, 2));
  console.log('Issued two fictional business review policies through normal underwriting and issue commands.');
  for (const policy of policies) console.log(`${policy.productCode}: ${origin}/policies/${policy.policyId}`);
} finally {
  await browser?.close();
  await lock.close();
  await unlink(`${directory}/running.lock`);
}
