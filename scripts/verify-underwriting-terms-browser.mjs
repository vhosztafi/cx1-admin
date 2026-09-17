import { writeFileSync } from 'node:fs';
import { execFileSync } from 'node:child_process';
import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';
const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['127.0.0.1', 'localhost'].includes(new URL(origin).hostname));
const output = '.local/browser-evidence/underwriting-terms'; await mkdir(output, { recursive: true });
const password = (await readFile('.local/demo-password.txt', 'utf8')).trim();
const fixtures = JSON.parse(await readFile('.local/browser-evidence/underwriting-rating/report.json', 'utf8')).journeys;
const proposals = JSON.parse(await readFile('contracts/examples/underwriting-demo.json', 'utf8')).proposals;
const browser = await chromium.launch({ channel: 'chrome', headless: true }), contexts = [], errors = [], report = { journeys: [] };
const tomorrow = new Date(Date.now() + 86400000).toISOString().slice(0, 10);
async function get(page, path) { const r = await page.request.get(origin + path); assert.equal(r.status(), 200, `${path}: ${await r.text()}`); return { data: await r.json(), etag: r.headers().etag }; }
async function post(page, path, data, etag, multipart) { const csrf = (await get(page, '/api/v1/auth/csrf')).data.requestToken; const r = await page.request.post(origin + path, { headers: { 'X-CSRF-Token': csrf, 'Idempotency-Key': crypto.randomUUID(), ...(etag ? { 'If-Match': etag } : {}) }, ...(multipart ? { multipart } : { data }) }); assert.ok(r.ok(), `${path}: ${r.status()} ${await r.text()}`); return { data: await r.json(), etag: r.headers().etag }; }
async function until(read, accept) { const end = Date.now() + 60000; do { const value = await read(); if (accept(value)) return value; await new Promise(r => setTimeout(r, 350)); } while (Date.now() < end); throw Error('Timed out waiting for persisted quotation outcome'); }
async function login(role) { const context = await browser.newContext({ viewport: { width: 1560, height: 1000 }, timezoneId: 'UTC' }); contexts.push(context); const page = await context.newPage(); page.setDefaultTimeout(25000); page.on('pageerror', e => errors.push(e.message)); await page.goto(origin + '/login'); await page.getByLabel('Email address').fill(role + '@cover.example'); await page.getByLabel('Password', { exact: true }).fill(password); await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(origin + '/'); return page; }
async function confirm(page) { const dialog = page.getByRole('dialog'); await dialog.getByRole('button', { name: 'Confirm action', exact: true }).click(); await dialog.waitFor({ state: 'hidden' }); }
async function ready(page) {
  try { await page.getByRole('button', { name: 'Refresh quotation', exact: true }).waitFor({ timeout: 18000 }); }
  catch (error) { const retry = page.getByRole('button', { name: 'Try again', exact: true }); if (!await retry.count()) throw error; await retry.click(); await page.getByRole('button', { name: 'Refresh quotation', exact: true }).waitFor(); report.explicitReadRecovery = (report.explicitReadRecovery ?? 0) + 1; }
}
async function refresh(page) { await page.getByRole('button', { name: 'Refresh quotation', exact: true }).click(); await ready(page); }
async function open(page, id) { await page.goto(origin + '/quotes/' + id); await page.getByRole('tab', { name: 'Quotation', exact: true }).click(); await ready(page); }
async function apiProof(page, id, purpose) {
  const route = '/api/v1/quotes/' + id, name = `fictional-${purpose.code}-${crypto.randomUUID().slice(0, 6)}.txt`;
  const file = (await post(page, route + '/underwriting/evidence-files', undefined, (await get(page, route)).etag, { fileName: name, contentType: 'text/plain', file: { name, mimeType: 'text/plain', buffer: Buffer.from('Fictional business demonstration proof for ' + purpose.code) } })).data;
  const assessment = (await get(page, route + '/underwriting')).data;
  const association = (await post(page, route + '/underwriting/evidence', { cycleId: assessment.context.cycleId, fileId: file.id, requirementCode: purpose.code, inputFingerprint: purpose.inputFingerprint, ...(purpose.riskItemId ? { riskItemId: purpose.riskItemId } : {}), ...(purpose.conditionId ? { conditionId: purpose.conditionId } : {}), ...(purpose.termsVersionId ? { termsVersionId: purpose.termsVersionId } : {}), reason: 'Actual fictional supplied proof' }, (await get(page, route)).etag)).data;
  const saved = (await get(page, route + '/underwriting/evidence')).data.items.find(x => x.id === association.id);
  await post(page, route + `/underwriting/evidence/${saved.id}/reviews`, { cycleId: assessment.context.cycleId, associationEtag: saved.etag, expectedFingerprint: purpose.inputFingerprint, outcome: 'accepted', reason: 'Reviewed fictional proof for current purpose' }, (await get(page, route)).etag); return saved;
}
async function uiProof(page, id, code) {
  const route = '/api/v1/quotes/' + id, purpose = (await get(page, route + '/underwriting')).data.proofRequirements.find(x => x.code === code); assert.ok(purpose);
  const name = `fictional-${code}-${crypto.randomUUID().slice(0, 6)}.txt`;
  await page.getByLabel('Underwriting evidence file', { exact: true }).setInputFiles({ name, mimeType: 'text/plain', buffer: Buffer.from(`Fictional supplied ${code} for exact terms ${purpose.termsVersionId}`) });
  await page.getByRole('button', { name: 'Upload document', exact: true }).click(); await confirm(page); await ready(page);
  await page.getByLabel(`${purpose.label} · Saved document`, { exact: true }).selectOption({ label: name });
  await page.getByLabel(`${purpose.label} · Attachment reason`, { exact: true }).fill('Supplied for this exact prepared quotation');
  await page.locator('fieldset').filter({ has: page.getByLabel(`${purpose.label} · Saved document`, { exact: true }) }).getByRole('button', { name: 'Attach proof', exact: true }).click(); await confirm(page); await ready(page);
  const saved = (await get(page, route + '/underwriting/evidence')).data.items.find(x => x.fileName === name); assert.equal(saved.termsVersionId, purpose.termsVersionId);
  const card = page.locator(`[data-evidence-id="${saved.id}"]`); await card.getByLabel(`Evidence reason for ${name}`, { exact: true }).fill('Reviewed actual fictional document against this quotation'); await card.getByRole('button', { name: 'Record review', exact: true }).click(); await confirm(page); await ready(page); return saved;
}
async function create(page, fixture) {
  const source = (await get(page, '/api/v1/quotes/' + fixture.quoteId)).data, proposal = structuredClone(proposals[fixture.productCode]); proposal.termIntent.localStartDate = tomorrow; proposal.risk.business.startedOn = '2010-01-01';
  if (fixture.productCode === 'motor-trade-combined') proposal.cover.requestedSections.find(x => x.code === 'stock-custody').limit = '90000.00';
  const id = (await post(page, '/api/v1/quotes', { relationshipId: source.relationshipId, productVersionId: source.productVersionId, proposal })).data.id, route = '/api/v1/quotes/' + id;
  for (const vehicle of proposal.risk.vehicles) { let quote = await get(page, route); const request = (await post(page, route + '/lookups', { revisionId: quote.data.revisionId, kind: 'vehicle', scope: 'vehicle', riskItemId: vehicle.id, scenario: 'no-match' }, quote.etag)).data; const lookup = await until(async () => (await get(page, route + '/lookups')).data.items.find(x => x.id === request.id), x => x?.state === 'no-match'); quote = await get(page, route); await post(page, route + '/lookup-selections', { lookupId: lookup.id, revisionId: quote.data.revisionId, inputFingerprint: lookup.inputFingerprint, manualReason: 'Fictional quotation fixture' }, quote.etag); }
  const quote = await get(page, route); await post(page, route + '/rate', { revisionId: quote.data.revisionId, reason: 'Fictional quotation demonstration' }, quote.etag); let assessment = await until(async () => (await get(page, route + '/underwriting')).data, x => !!x.ratingId);
  for (const purpose of assessment.proofRequirements.filter(x => !x.satisfied)) await apiProof(page, id, purpose);
  const refs = (await get(page, '/api/v1/referrals?quoteId=' + id)).data.items;
  if (refs.length) await post(page, route + '/referral-decisions', { cycleId: assessment.context.cycleId, decisions: refs.map(x => ({ referralId: x.id, etag: x.etag, outcome: 'approve', reason: 'Current authority and actual reviewed fictional proof' })) }, (await get(page, route)).etag);
  const contact = (await post(page, `/api/v1/relationships/${source.relationshipId}/contacts`, { fullName: 'Fictional quotation recipient ' + crypto.randomUUID().slice(0, 6), role: 'Director', email: 'quotation-demo@example.invalid', isPrimary: false, marketingConsent: { state: 'not-asked', email: false, telephone: false, recordedAt: new Date().toISOString(), source: 'Fictional business demonstration' } }, (await get(page, `/api/v1/relationships/${source.relationshipId}`)).etag)).data;
  return { id, contactId: contact.id, relationshipId: source.relationshipId };
}
if (process.argv.includes('--readback')) {
  try {
    const saved = JSON.parse(await readFile(output + '/report.json', 'utf8')), page = await login('underwriter');
    for (const journey of saved.journeys) {
      await open(page, journey.quoteId); await page.getByText('Current acceptance recorded', { exact: true }).waitFor();
      const state = (await get(page, '/api/v1/quotes/' + journey.quoteId + '/underwriting')).data; assert.equal(state.acceptanceId, journey.acceptanceId);
      await page.screenshot({ path: `${output}/${journey.productCode}-desktop-viewport.png` });
      await page.setViewportSize({ width: 390, height: 844 }); assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true); await page.screenshot({ path: `${output}/${journey.productCode}-mobile-viewport.png` });
      await page.getByRole('button', { name: 'Review acceptance', exact: true }).scrollIntoViewIfNeeded(); await page.screenshot({ path: `${output}/${journey.productCode}-acceptance-mobile.png` }); await page.setViewportSize({ width: 1560, height: 1000 });
    }
    assert.deepEqual(errors, []); console.log('Both persisted quotation acceptances, current exact IDs and final desktop/mobile views passed.');
  } finally { for (const context of contexts) await context.close(); await browser.close(); }
  process.exit(0);
}
if (process.argv.includes('--negative')) {
  let page;
  const sql = query => {
    const file = '.local/phase6-09-browser-scenario.sql';
    writeFileSync(file, `SET NOCOUNT ON; IF DB_NAME()<>N'CoverMGA_Demo' THROW 51000,'Wrong demo database',1; ${query}`, 'utf8');
    return execFileSync('sqlcmd', ['-S', '.\\SQL2022', '-E', '-C', '-b', '-d', 'CoverMGA_Demo', '-h', '-1', '-W', '-w', '65535', '-f', '65001', '-i', file], { encoding: 'utf8' }).trim();
  };
  const original = sql("SELECT TOP(1) [Values] FROM SettingVersion WHERE Scope=N'quote-delivery' AND EffectiveFrom<=SYSUTCDATETIME() ORDER BY Version DESC;");
  assert.equal(JSON.parse(original).kind, 'quote-delivery');
  function scenario(values) { sql(`SET XACT_ABORT ON; BEGIN TRAN; DECLARE @v int=(SELECT MAX(Version)+1 FROM SettingVersion WITH(UPDLOCK,HOLDLOCK) WHERE Scope=N'quote-delivery'); INSERT SettingVersion(Id,Scope,Version,EffectiveFrom,[Values],CreatedAt) VALUES(NEWID(),N'quote-delivery',@v,SYSUTCDATETIME(),N'${values.replaceAll("'", "''")}',SYSUTCDATETIME()); COMMIT;`); }
  try {
    page = await login('underwriter'); const fixture = fixtures.find(x => x.productCode === 'motor-trade-road-risks'), created = await create(page, fixture), id = created.id, route = '/api/v1/quotes/' + id;
    let assessment = (await get(page, route + '/underwriting')).data, history = (await get(page, route + '/terms')).data;
    await post(page, route + '/terms/prepare', { cycleId: assessment.context.cycleId, ratingId: assessment.ratingId, templateVersionId: history.templates[0].id }, (await get(page, route)).etag);
    assessment = (await get(page, route + '/underwriting')).data; await apiProof(page, id, assessment.proofRequirements.find(x => x.code === 'signed-statement'));
    await open(page, id); history = (await get(page, route + '/terms')).data; const recipient = history.recipientOptions.find(x => x.id === created.contactId);
    const select = async () => page.getByRole('checkbox', { name: `${recipient.name} · ${recipient.email}`, exact: true }).check();
    for (const name of ['transient-once', 'reject']) {
      scenario(JSON.stringify({ demo: true, kind: 'quote-delivery', schemaVersion: '1', scenario: name }));
      try { await select(); await page.getByRole('button', { name: 'Review quotation delivery', exact: true }).click(); await confirm(page); }
      finally { scenario(original); }
      await ready(page);
      if (name === 'transient-once') {
        const queued = (await get(page, route + '/terms')).data.deliveries[0]; assert.equal(queued.state, 'queued'); await page.getByRole('heading', { name: 'Demo delivery queued', exact: true }).waitFor(); await page.screenshot({ path: output + '/queued-viewport.png' });
        await until(async () => (await get(page, route + '/terms')).data, x => x.deliveries[0]?.state === 'delivered');
      } else {
        await until(async () => (await get(page, route + '/terms')).data, x => x.deliveries[0]?.state === 'failed');
      }
      await refresh(page);
    }
    await page.getByRole('heading', { name: 'Demo delivery failed', exact: true }).waitFor(); await page.screenshot({ path: output + '/failed-viewport.png' });
    const before = (await get(page, route + '/terms')).data.deliveries.length;
    await select(); await page.getByRole('button', { name: 'Review quotation delivery', exact: true }).click();
    const contactRoute = `/api/v1/relationships/${created.relationshipId}/contacts/${created.contactId}`;
    await post(page, contactRoute + '/end', { reason: 'Fictional stale quotation recipient test' }, (await get(page, contactRoute)).etag);
    await page.getByRole('dialog').getByRole('button', { name: 'Confirm action', exact: true }).click(); await page.getByRole('dialog').getByRole('alert').waitFor(); await page.getByRole('button', { name: 'Read current state', exact: true }).click(); await page.getByRole('dialog').getByText(/Current state:/).waitFor(); await page.getByRole('button', { name: 'Back to form', exact: true }).click(); assert.equal((await get(page, route + '/terms')).data.deliveries.length, before); assert.equal(await page.getByRole('checkbox', { name: `${recipient.name} · ${recipient.email}`, exact: true }).isChecked(), true);
    await refresh(page); history = (await get(page, route + '/terms')).data; const replacement = history.recipientOptions[0]; assert.ok(replacement); await page.getByRole('checkbox', { name: `${replacement.name} · ${replacement.email}`, exact: true }).check(); await page.getByRole('button', { name: 'Review quotation delivery', exact: true }).click();
    let token = (await get(page, '/api/v1/auth/csrf')).data.requestToken; assert.ok((await page.request.post(origin + '/api/v1/auth/logout', { headers: { 'X-CSRF-Token': token } })).ok()); token = (await get(page, '/api/v1/auth/csrf')).data.requestToken; assert.ok((await page.request.post(origin + '/api/v1/auth/login', { headers: { 'X-CSRF-Token': token }, data: { email: 'servicing@cover.example', password } })).ok());
    await page.getByRole('dialog').getByRole('button', { name: 'Confirm action', exact: true }).click(); await page.getByRole('dialog').getByRole('alert').waitFor(); await page.getByRole('button', { name: 'Back to form', exact: true }).click(); assert.equal((await get(page, route + '/terms')).data.deliveries.length, before);
    token = (await get(page, '/api/v1/auth/csrf')).data.requestToken; await page.request.post(origin + '/api/v1/auth/logout', { headers: { 'X-CSRF-Token': token } }); token = (await get(page, '/api/v1/auth/csrf')).data.requestToken; assert.ok((await page.request.post(origin + '/api/v1/auth/login', { headers: { 'X-CSRF-Token': token }, data: { email: 'underwriter@cover.example', password } })).ok());
    await refresh(page); await page.getByRole('checkbox', { name: `${replacement.name} · ${replacement.email}`, exact: true }).check(); await page.getByRole('button', { name: 'Review quotation delivery', exact: true }).click(); assessment = (await get(page, route + '/underwriting')).data;
    await post(page, route + '/return-to-draft', { cycleId: assessment.context.cycleId, reason: 'Fictional superseded quotation test' }, (await get(page, route)).etag);
    await page.getByRole('dialog').getByRole('button', { name: 'Confirm action', exact: true }).click(); await page.getByRole('button', { name: 'Read current state', exact: true }).click(); await page.getByRole('dialog').getByText(/Current state: draft/).waitFor(); await page.getByRole('button', { name: 'Back to form', exact: true }).click(); assert.equal((await get(page, route + '/terms')).data.deliveries.length, before);
    assert.deepEqual(errors, []); const result = { quoteId: id, actualQueuedAndFailed: true, staleRecipientNoWrite: true, accountSwitchNoWrite: true, supersededTermsNoWrite: true, scenariosRestoredByAppendedVersion: true }; await writeFile(output + '/negative-report.json', JSON.stringify(result, null, 2)); console.log(JSON.stringify(result));
  } catch (error) { if (page) await page.screenshot({ path: output + '/negative-failure.png', fullPage: true }); throw error; }
  finally { scenario(original); for (const context of contexts) await context.close(); await browser.close(); }
  process.exit(0);
}
let page;
try {
  page = await login('underwriter');
  for (const fixture of fixtures) {
    const created = await create(page, fixture), id = created.id, route = '/api/v1/quotes/' + id;
    await open(page, id); await page.getByLabel('Quotation template', { exact: true }).selectOption({ index: 1 }); await page.getByRole('button', { name: 'Prepare exact terms', exact: true }).click();
    const dialog = page.getByRole('dialog'), first = dialog.getByRole('button', { name: 'Back to form', exact: true }), last = dialog.getByRole('button', { name: 'Confirm action', exact: true }); await last.focus(); await page.keyboard.press('Tab'); assert.equal(await first.evaluate(x => document.activeElement === x), true); await confirm(page); await ready(page);
    let history = (await get(page, route + '/terms')).data; const terms = history.terms[0]; assert.ok(terms); assert.equal(await page.getByRole('button', { name: 'Review quotation delivery', exact: true }).isDisabled(), true);
    await uiProof(page, id, 'signed-statement');
    await page.getByLabel('Quotation template', { exact: true }).selectOption({ index: 1 }); await page.getByRole('button', { name: 'Prepare exact terms', exact: true }).click(); await confirm(page); await ready(page); assert.equal((await get(page, route + '/terms')).data.terms[0].id, terms.id);
    history = (await get(page, route + '/terms')).data; const recipient = history.recipientOptions.find(x => x.id === created.contactId); assert.ok(recipient);
    await page.getByRole('checkbox', { name: `${recipient.name} · ${recipient.email}`, exact: true }).check();
    const attempts = []; let lose = true; await page.route('**' + route + '/terms', async intercepted => { if (intercepted.request().method() !== 'POST') return intercepted.continue(); const request = intercepted.request(); attempts.push({ body: request.postData(), key: request.headers()['idempotency-key'], etag: request.headers()['if-match'] }); if (lose) { lose = false; const response = await intercepted.fetch(); assert.equal(response.status(), 202); await intercepted.abort('failed'); } else await intercepted.continue(); });
    await page.getByRole('button', { name: 'Review quotation delivery', exact: true }).click(); await page.getByRole('dialog').getByRole('button', { name: 'Confirm action', exact: true }).click(); await page.getByRole('button', { name: 'Retry same action', exact: true }).waitFor(); await page.keyboard.press('Escape'); assert.equal(await page.getByRole('dialog').isVisible(), true); await page.getByRole('button', { name: 'Retry same action', exact: true }).click(); await page.getByRole('dialog').waitFor({ state: 'hidden' }); assert.deepEqual(attempts[0], attempts[1]); await page.unroute('**' + route + '/terms'); await ready(page);
    history = await until(async () => (await get(page, route + '/terms')).data, x => x.deliveries[0]?.state === 'delivered'); const delivery = history.deliveries[0]; await refresh(page); await page.getByRole('heading', { name: 'Demo delivery completed', exact: true }).waitFor();
    const proof = await uiProof(page, id, 'acceptance-proof');
    await page.getByLabel('Accepted by', { exact: true }).fill('Fictional authorised customer'); await page.getByLabel('Acceptance received at', { exact: true }).fill(new Date().toISOString()); await page.getByLabel('Reviewed acceptance evidence', { exact: true }).selectOption(proof.id); await page.getByRole('button', { name: 'Review acceptance', exact: true }).click(); await confirm(page); await ready(page);
    const assessment = (await get(page, route + '/underwriting')).data; assert.ok(assessment.acceptanceId); history = (await get(page, route + '/terms')).data; assert.equal(history.acceptances[0].deliveryId, delivery.id); assert.equal(history.acceptances[0].termsVersionId, terms.id); assert.equal((await get(page, route)).data.state, 'accepted');
    await page.reload(); await page.getByRole('tab', { name: 'Quotation', exact: true }).click(); await ready(page); await page.getByText('Current acceptance recorded', { exact: true }).waitFor();
    await page.screenshot({ path: `${output}/${fixture.productCode}-desktop.png`, fullPage: true }); const rail = await page.locator('.underwriting-rail').boundingBox(); assert.ok(Math.abs(rail.width - 314) < 2); await page.setViewportSize({ width: 390, height: 844 }); assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true); await page.screenshot({ path: `${output}/${fixture.productCode}-mobile.png`, fullPage: true }); await page.setViewportSize({ width: 1560, height: 1000 });
    const proofCard = page.locator(`[data-evidence-id="${proof.id}"]`); await proofCard.getByLabel(`Evidence reason for ${proof.fileName}`, { exact: true }).fill('Fictional acceptance proof replaced after review'); await proofCard.getByRole('button', { name: 'Withdraw proof', exact: true }).click(); await confirm(page); await ready(page);
    assert.equal((await get(page, route + '/underwriting')).data.acceptanceId, undefined); await page.getByText('The quote has changed since acceptance. Review the current terms and record fresh acceptance.', { exact: true }).waitFor();
    const freshProof = await uiProof(page, id, 'acceptance-proof'); await page.getByLabel('Accepted by', { exact: true }).fill('Fictional customer fresh confirmation'); await page.getByLabel('Acceptance received at', { exact: true }).fill(new Date().toISOString()); await page.getByLabel('Reviewed acceptance evidence', { exact: true }).selectOption(freshProof.id); await page.getByRole('button', { name: 'Review acceptance', exact: true }).click(); await confirm(page); await ready(page);
    const freshAcceptance = (await get(page, route + '/underwriting')).data.acceptanceId; assert.ok(freshAcceptance); assert.notEqual(freshAcceptance, assessment.acceptanceId); assert.equal((await get(page, route + '/terms')).data.acceptances.length, 2);
    report.journeys.push({ productCode: fixture.productCode, quoteId: id, termsId: terms.id, deliveryId: delivery.id, acceptanceId: freshAcceptance, previousAcceptanceId: assessment.acceptanceId, proofId: proof.id, contactId: created.contactId, checks: ['persisted prepare/sign/send/accept', 'same terms retained after signature', 'actual supplied exact-version proof', 'lost committed send exact retry', 'uncertain Escape and focus wrap', 'persisted refresh', 'proof withdrawal invalidates acceptance and fresh reviewed proof restores it', '314px rail and390px containment'] }); await writeFile(output + '/report.json', JSON.stringify(report, null, 2));
  }
  assert.deepEqual(errors, []); console.log(JSON.stringify(report));
} catch (error) { if (page) await page.screenshot({ path: output + '/failure.png', fullPage: true }); throw error; } finally { for (const context of contexts) await context.close(); await browser.close(); }
