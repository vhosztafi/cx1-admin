import assert from 'node:assert/strict';
import { readFile, writeFile, mkdir } from 'node:fs/promises';
import { chromium } from 'playwright';
const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100'; assert.ok(['127.0.0.1', 'localhost'].includes(new URL(origin).hostname));
const output = '.local/browser-evidence/policy-discovery'; await mkdir(output, { recursive: true });
const fixtures = JSON.parse(await readFile('.local/browser-evidence/underwriting-issue/report.json', 'utf8')).journeys;
const password = (await readFile('.local/demo-password.txt', 'utf8')).trim(), browser = await chromium.launch({ channel: 'chrome', headless: true });
const contexts = [], errors = [], report = { journeys: [] };
async function get(page, path) { const r = await page.request.get(origin + path); assert.equal(r.status(), 200, `${path}: ${await r.text()}`); return { data: await r.json(), etag: r.headers().etag }; }
async function post(page, path, data, etag) { const token = (await get(page, '/api/v1/auth/csrf')).data.requestToken; const r = await page.request.post(origin + path, { headers: { 'X-CSRF-Token': token, 'Idempotency-Key': crypto.randomUUID(), ...(etag ? { 'If-Match': etag } : {}) }, data }); assert.ok(r.ok(), `${path}: ${r.status()}: ${await r.text()}`); return r.json(); }
async function login(role) { const context = await browser.newContext({ viewport: { width: 1560, height: 1000 } }); contexts.push(context); const page = await context.newPage(); page.setDefaultTimeout(30000); page.on('pageerror', e => errors.push(e.message)); await page.goto(origin + '/login'); await page.getByLabel('Email address').fill(role + '@cover.example'); await page.getByLabel('Password', { exact: true }).fill(password); await page.getByRole('button', { name: 'Sign in', exact: true }).click(); await page.waitForURL(origin + '/'); return page; }
let page;
try {
  page = await login('underwriter'); const admin = await login('agency-admin');
  for (const fixture of fixtures) {
    const policy = (await get(page, '/api/v1/policies/' + fixture.policyId)).data, registration = policy.snapshot.risk.vehicles[0].registration;
    await page.goto(origin + '/policies'); await page.getByLabel('Search policies', { exact: true }).fill(registration); await page.getByRole('button', { name: 'Search', exact: true }).click();
    await page.getByRole('link', { name: policy.reference, exact: true }).waitFor();
    await page.getByLabel('Product', { exact: true }).selectOption(fixture.productCode); await page.getByLabel('Status', { exact: true }).selectOption('scheduled');
    await page.getByLabel('Sort by', { exact: true }).selectOption('inception'); await page.getByLabel('Order', { exact: true }).selectOption('desc');
    await page.getByRole('link', { name: policy.reference, exact: true }).click(); await page.getByRole('heading', { name: policy.reference, exact: true }).waitFor();
    await page.getByRole('link', { name: 'Open client record', exact: true }).click(); await page.getByRole('link', { name: 'Policies', exact: true }).last().click();
    await page.getByRole('link', { name: policy.reference, exact: true }).click(); await page.getByRole('heading', { name: policy.reference, exact: true }).waitFor();
    const records = (await get(page, `/api/v1/clients/${policy.clientId}/records?kind=policy`)).data; assert.ok(records.items.some(x => x.id === policy.id && x.kind === 'policy'));
    const activity = (await get(page, `/api/v1/clients/${policy.clientId}/activity?pageSize=100`)).data; assert.ok(activity.items.some(x => x.eventType === 'policy.issued' && x.recordId === fixture.quoteId));
    const quotes = (await get(page, `/api/v1/quotes?status=bound&clientId=${policy.clientId}`)).data; assert.ok(quotes.items.some(x => x.id === fixture.quoteId));
    await page.goto(origin + `/agents/${policy.agencyId}/sharing`); await page.getByLabel('Search shared policy summaries', { exact: true }).fill(policy.reference); await page.locator('form').filter({ has: page.getByLabel('Search shared policy summaries', { exact: true }) }).getByRole('button', { name: 'Search', exact: true }).click();
    await page.getByRole('button', { name: policy.reference, exact: true }).click(); await page.getByRole('link', { name: 'Open internal policy record', exact: true }).waitFor();
    await page.getByLabel('Search shared policy summaries', { exact: true }).fill(registration); await page.locator('form').filter({ has: page.getByLabel('Search shared policy summaries', { exact: true }) }).getByRole('button', { name: 'Search', exact: true }).click(); await page.getByText('No shared policies match', { exact: true }).waitFor();
    // An accepted agency cookie, not the internal preview, verifies the actual
    // external API boundary. This does not create agency workflow pages.
    const email = `policy-demo-${crypto.randomUUID().slice(0, 8)}@example.invalid`;
    const invitation = await post(admin, `/api/v1/agencies/${policy.agencyId}/invitations`, { email, displayName: 'Fictional policy summary reader', role: 'broker-readonly' }, (await get(admin, `/api/v1/agencies/${policy.agencyId}`)).etag);
    const link = await post(admin, `/api/v1/invitations/${invitation.invitationId}/demo-link`, {});
    const context = await browser.newContext(); contexts.push(context); const broker = await context.newPage(); await broker.goto(origin + '/login'); await post(broker, '/api/v1/auth/invitations/accept', { invitationToken: link.invitationToken, password });
    await post(broker, '/api/v1/auth/login', { email, password });
    const own = (await get(broker, `/api/v1/agency-context/policies/${policy.id}`)).data; assert.equal(own.id, policy.id); assert.deepEqual(Object.keys(own).sort(), ['clientName', 'endsAt', 'id', 'productCode', 'reference', 'startsAt', 'state']);
    assert.equal((await get(broker, '/api/v1/agency-context/policies?q=' + encodeURIComponent(registration))).data.totalCount, 0);
    assert.equal((await broker.request.get(origin + '/api/v1/policies/' + policy.id)).status(), 403);
    assert.equal((await broker.request.get(origin + '/api/v1/agency-context/policies/' + crypto.randomUUID())).status(), 404);
    report.journeys.push({ productCode: fixture.productCode, policyId: policy.id, reference: policy.reference, invitationId: invitation.invitationId, checks: ['issued registration search', 'product/status/sort', 'policy-client-policy roundtrip', 'client records/activity', 'bound quote discovery', 'safe internal preview and hidden registration denial', 'accepted agency cookie own allowlist and internal/foreign denial'] });
    await writeFile(output + '/report.json', JSON.stringify(report, null, 2));
  }
  await page.goto(origin + '/policies'); await page.getByRole('table', { name: 'Issued policies', exact: true }).waitFor(); await page.screenshot({ path: output + '/desktop.png', fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 }); assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true); await page.screenshot({ path: output + '/mobile.png', fullPage: true });
  assert.deepEqual(errors, []); console.log('Both products: registration discovery, client links and actual accepted-agency safe projection passed.');
} catch (error) { if (page) await page.screenshot({ path: output + '/failure.png', fullPage: true }).catch(() => {}); throw error; }
finally { for (const context of contexts) await context.close(); await browser.close(); }
