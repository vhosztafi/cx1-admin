import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir, writeFile } from 'node:fs/promises';

const origin = process.env.COVER_WEB_ORIGIN ?? 'http://127.0.0.1:3100';
if (!['localhost', '127.0.0.1'].includes(new URL(origin).hostname)) throw Error('Local demo origin required.');
const { agencyId } = JSON.parse(await readFile('.local/browser-evidence/agency-lifecycle-result.json', 'utf8'));
const password = (await readFile('.local/demo-password.txt', 'utf8')).trim();
const email = `accepted-browser-${crypto.randomUUID()}@cover.example`;
const browser = await chromium.launch({ channel: 'chrome', headless: true });
const staff = await browser.newContext();
const context = await browser.newContext({ viewport: { width: 1440, height: 960 } });
const page = await context.newPage(); page.setDefaultTimeout(20000);
const errors = []; page.on('pageerror', error => errors.push(error.message));
const output = '.local/browser-evidence'; await mkdir(output, { recursive: true });
async function login(target, address) {
  await target.goto(origin + '/login');
  await target.getByLabel('Email address', { exact: true }).fill(address);
  await target.getByLabel('Password', { exact: true }).fill(password);
  await target.getByRole('button', { name: 'Sign in', exact: true }).click();
}
try {
  const admin = await staff.newPage(); await login(admin, 'agency-admin@cover.example'); await admin.waitForURL(origin + '/');
  const current = await staff.request.get(`${origin}/api/v1/agencies/${agencyId}`); assert.equal(current.status(), 200);
  const csrf = (await (await staff.request.get(origin + '/api/v1/auth/csrf')).json()).requestToken;
  const created = await staff.request.post(`${origin}/api/v1/agencies/${agencyId}/invitations`, {
    headers: { 'X-CSRF-Token': csrf, 'Idempotency-Key': crypto.randomUUID(), 'If-Match': current.headers().etag },
    data: { displayName: 'Fictional accepted agency administrator', email, role: 'broker-admin' },
  });
  assert.equal(created.status(), 201); const { invitationId, id: userId } = await created.json();
  const reveal = await staff.request.post(`${origin}/api/v1/invitations/${invitationId}/demo-link`, { headers: { 'X-CSRF-Token': csrf }, data: {} });
  assert.equal(reveal.status(), 200); const raw = (await reveal.json()).invitationToken;
  await page.goto(origin + '/invitations/accept#' + raw);
  await page.getByLabel('New password', { exact: true }).waitFor(); assert.equal(new URL(page.url()).hash, '');
  await page.getByLabel('New password', { exact: true }).fill(password);
  await page.getByLabel('Confirm password', { exact: true }).fill(password);
  await page.getByRole('button', { name: 'Set password', exact: true }).click();
  await page.getByRole('heading', { name: 'Password saved', exact: true }).waitFor();
  assert.equal((await context.request.get(origin + '/api/v1/account')).status(), 401);
  await login(page, email); await page.waitForURL(origin + '/agency-access');
  await page.getByRole('heading', { name: 'You are signed in', exact: true }).waitFor();
  const actor = await (await context.request.get(origin + '/api/v1/account')).json(); assert.equal(actor.scope, 'agency'); assert.equal(actor.agencyId, agencyId);
  assert.equal((await context.request.get(origin + '/api/v1/agency-context')).status(), 200);
  for (const path of ['/', '/clients', '/clients/new', '/agents', `/agents/${agencyId}`, `/agents/${agencyId}/sharing`, '/admin', '/account']) {
    await page.goto(origin + path); await page.waitForURL(origin + '/agency-access');
    assert.equal(await page.getByRole('navigation', { name: 'Main navigation' }).count(), 0);
    await page.getByRole('heading', { name: 'You are signed in', exact: true }).waitFor();
  }
  assert.equal((await context.request.get(origin + '/api/v1/clients')).status(), 403);
  assert.equal((await context.request.get(`${origin}/api/v1/agencies/${agencyId}/sharing`)).status(), 403);
  await page.reload(); await page.getByRole('heading', { name: 'You are signed in', exact: true }).waitFor();
  await page.screenshot({ path: `${output}/agency-access-desktop.png`, fullPage: true });
  await page.setViewportSize({ width: 390, height: 844 });
  assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth));
  await page.screenshot({ path: `${output}/agency-access-mobile.png`, fullPage: true });
  await page.route('**/api/v1/auth/logout', route => route.abort('failed'), { times: 1 });
  await page.getByRole('button', { name: 'Log out', exact: true }).click();
  await page.getByText('We could not sign you out. Please try again.', { exact: true }).waitFor();
  const logout = page.getByRole('button', { name: 'Log out', exact: true }); await logout.focus(); await page.keyboard.press('Enter');
  await page.waitForURL(origin + '/login');
  assert.equal((await context.request.get(origin + '/api/v1/account')).status(), 401);
  await page.goto(origin + '/agency-access'); await page.waitForURL(origin + '/login');
  await admin.goto(origin + '/agency-access'); await admin.waitForURL(origin + '/');
  assert.deepEqual(errors, []);
  await writeFile(`${output}/agency-access-result.json`, JSON.stringify({ agencyId, userId, invitationId, accepted: true, guardsVerified: true }, null, 2));
  console.log('Accepted agency browser passed: real invitation/password/login, persisted agency session, internal deep-link guards, scoped API, reload, desktop/mobile, failed logout recovery and keyboard logout.');
} catch (error) { await page.screenshot({ path: `${output}/agency-access-failure.png`, fullPage: true }); throw error; }
finally { await context.close(); await staff.close(); await browser.close(); }
