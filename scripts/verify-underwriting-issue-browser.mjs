import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { chromium } from 'playwright';
const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
assert.ok(['127.0.0.1', 'localhost'].includes(new URL(origin).hostname));
const output = '.local/browser-evidence/underwriting-issue'; await mkdir(output, { recursive: true });
const password = (await readFile('.local/demo-password.txt', 'utf8')).trim();
const fixtures = JSON.parse(await readFile('.local/browser-evidence/underwriting-terms/report.json', 'utf8')).journeys;
const report = await readFile(output + '/report.json', 'utf8').then(JSON.parse).catch(() => ({ journeys: [] }));
const browser = await chromium.launch({ channel: 'chrome', headless: true }), contexts = [], errors = [];
const save = () => writeFile(output + '/report.json', JSON.stringify(report, null, 2));
async function get(page, path) { const r = await page.request.get(origin + path); assert.equal(r.status(), 200, `${path}: ${await r.text()}`); return { data: await r.json(), etag: r.headers().etag }; }
async function post(page, path, data, etag) { const token = (await get(page, '/api/v1/auth/csrf')).data.requestToken; return page.request.post(origin + path, { headers: { 'X-CSRF-Token': token, 'Idempotency-Key': crypto.randomUUID(), ...(etag ? { 'If-Match': etag } : {}) }, data }); }
async function login(role) { const context = await browser.newContext({ viewport: { width: 1560, height: 1000 } }); contexts.push(context); const page = await context.newPage(); page.setDefaultTimeout(25000); page.on('pageerror', e => errors.push(e.message)); await page.goto(origin + '/login'); await page.getByLabel('Email address').fill(role + '@cover.example'); await page.getByLabel('Password', { exact: true }).fill(password); await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(origin + '/'); return page; }
async function account(page, role) { assert.ok((await post(page, '/api/v1/auth/logout')).ok()); assert.ok((await post(page, '/api/v1/auth/login', { email: role + '@cover.example', password })).ok()); }
async function open(page, id) { await page.goto(origin + '/quotes/' + id); await page.getByRole('tab', { name: 'Quotation', exact: true }).click(); await page.getByRole('button', { name: 'Refresh quotation', exact: true }).waitFor(); }
async function review(page) { await page.getByLabel('Issue reason', { exact: true }).fill('Fictional business demo: issue the accepted cover'); await page.getByRole('button', { name: 'Review policy issue', exact: true }).click(); }
let page;
try {
  page = await login('underwriter');
  if (process.argv.includes('--preflight')) {
    for (const fixture of fixtures) { const state = (await get(page, '/api/v1/quotes/' + fixture.quoteId + '/underwriting')).data; assert.equal(state.capabilities.canIssue, true, `${fixture.productCode}: ${JSON.stringify(state.blockers)}`); }
    console.log('Both retained demo acceptances are currently issuable.');
  }
  else
  for (const fixture of fixtures) {
    const route = '/api/v1/quotes/' + fixture.quoteId;
    let journey = report.journeys.find(x => x.quoteId === fixture.quoteId), quote = await get(page, route);
    if (!quote.data.boundPolicyId) {
      assert.equal(journey, undefined, 'Saved policy cannot disappear between runs');
      const servicing = await login('servicing'); await open(servicing, fixture.quoteId); assert.equal(await servicing.getByRole('button', { name: 'Review policy issue', exact: true }).count(), 0);
      await open(page, fixture.quoteId); await review(page);
      const dialog = page.getByRole('dialog');
      await dialog.getByRole('button', { name: 'Confirm action', exact: true }).focus(); await page.keyboard.press('Tab'); assert.equal(await dialog.getByRole('button', { name: 'Back to form', exact: true }).evaluate(x => x === document.activeElement), true);
      const current = (await get(page, route + '/underwriting')).data, history = (await get(page, route + '/terms')).data;
      const accepted = history.acceptances.find(x => x.id === current.acceptanceId); assert.ok(accepted);
      const changed = await post(page, route + '/acceptances', { cycleId: current.context.cycleId, ratingId: current.ratingId, termsVersionId: accepted.termsVersionId, termsHash: current.termsHash, assuranceHash: current.assuranceHash, accepterLabel: 'Fictional refreshed issue confirmation', acceptedAt: accepted.acceptedAt, channel: accepted.channel, evidenceAssociationId: accepted.evidenceAssociationId }, (await get(page, route)).etag);
      assert.ok(changed.ok(), await changed.text());
      await dialog.getByRole('button', { name: 'Confirm action', exact: true }).click(); await dialog.getByRole('button', { name: 'Read current state', exact: true }).waitFor();
      assert.equal((await get(page, route)).data.boundPolicyId, null);
      await dialog.getByRole('button', { name: 'Read current state', exact: true }).click(); await dialog.getByText(/Current state: accepted/).waitFor(); await dialog.getByRole('button', { name: 'Back to form', exact: true }).click();
      assert.equal(await page.getByLabel('Issue reason', { exact: true }).inputValue(), 'Fictional business demo: issue the accepted cover');
      await open(page, fixture.quoteId); await review(page);
      await account(page, 'servicing'); await dialog.getByRole('button', { name: 'Confirm action', exact: true }).click(); await dialog.getByRole('alert').waitFor(); assert.equal((await get(page, route)).data.boundPolicyId, null);
      await account(page, 'underwriter'); await dialog.getByRole('button', { name: 'Back to form', exact: true }).click(); await open(page, fixture.quoteId); await review(page);
      const attempts = []; let lose = true, receipt;
      await page.route('**' + route + '/issue', async intercepted => {
        const request = intercepted.request(); if (request.method() !== 'POST') return intercepted.continue();
        attempts.push({ body: request.postData(), key: request.headers()['idempotency-key'], etag: request.headers()['if-match'] });
        if (lose) { lose = false; const response = await intercepted.fetch(); assert.equal(response.status(), 201, await response.text()); receipt = await response.json();
          journey = { productCode: fixture.productCode, quoteId: fixture.quoteId, ...receipt, checks: ['servicing hidden', 'stale acceptance blocked', 'retained form and current state', 'account switch blocked', 'focus wraps'] }; report.journeys.push(journey); await save(); await intercepted.abort('failed');
        } else await intercepted.continue();
      });
      await dialog.getByRole('button', { name: 'Confirm action', exact: true }).evaluate(button => { button.click(); button.click(); }); await dialog.getByRole('button', { name: 'Retry same action', exact: true }).waitFor(); assert.equal(attempts.length, 1);
      await page.keyboard.press('Escape'); assert.equal(await dialog.isVisible(), true); assert.equal(await dialog.getByRole('button', { name: 'Back to form', exact: true }).isDisabled(), true);
      await dialog.getByRole('button', { name: 'Retry same action', exact: true }).click(); await page.waitForURL(origin + '/policies/' + receipt.policyId); assert.equal(attempts.length, 2); assert.deepEqual(attempts[0], attempts[1]); await page.unroute('**' + route + '/issue'); journey.checks.push('lost committed response exact retry', 'double click one dispatch', 'uncertain Escape blocked'); await save();
    } else { assert.ok(journey && journey.policyId === quote.data.boundPolicyId, 'Previously issued fixtures require their recorded receipt'); await page.goto(origin + '/policies/' + journey.policyId); }
    const policy = (await get(page, '/api/v1/policies/' + journey.policyId)).data;
    for (const field of ['termId', 'versionId', 'transactionId']) assert.equal(policy[field], journey[field]);
    assert.equal(policy.financials.obligationId, journey.obligationId); assert.equal(policy.documentRequests.length, 3); assert.ok(policy.documentRequests.every(x => x.state === 'requested' && x.versionId === policy.versionId));
    const pennies = value => BigInt(value.replace('.', '')); assert.equal(policy.financials.lines.reduce((sum, x) => sum + (x.side === 'debit' ? 1n : -1n) * pennies(x.amount), 0n), 0n);
    assert.equal(policy.snapshot.productCode, fixture.productCode); await page.getByRole('heading', { name: policy.reference, exact: true }).waitFor();
    assert.ok(Math.abs((await page.locator('.underwriting-rail').boundingBox()).width - 314) < 2);
    await page.screenshot({ path: `${output}/${fixture.productCode}-desktop.png`, fullPage: true });
    for (const name of ['Risk details', 'Cover', 'Drivers', 'Vehicles', 'Transactions', 'Documents', 'Overview']) { await page.getByRole('tab', { name, exact: true }).click(); assert.equal(await page.getByRole('tab', { name, exact: true }).getAttribute('aria-selected'), 'true'); }
    await page.setViewportSize({ width: 390, height: 844 }); assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true); await page.screenshot({ path: `${output}/${fixture.productCode}-mobile.png`, fullPage: true }); await page.setViewportSize({ width: 1560, height: 1000 });
    await page.reload(); await page.getByRole('heading', { name: policy.reference, exact: true }).waitFor(); await open(page, fixture.quoteId); await page.getByRole('link', { name: 'Open issued policy', exact: true }).first().waitFor();
    journey.checks = [...new Set([...journey.checks, 'immutable policy and compound receipt', 'balanced opening journal', 'three requested documents', 'policy tabs and persisted quote link', '314px rail and 390px containment'])]; await save();
  }
  assert.deepEqual(errors, []); if (!process.argv.includes('--preflight')) console.log('Both actual Motor Trade policy issue journeys passed; saved evidence: ' + output);
} catch (error) { if (page) await page.screenshot({ path: output + '/failure.png', fullPage: true }).catch(() => {}); throw error; }
finally { for (const context of contexts) await context.close(); await browser.close(); }
