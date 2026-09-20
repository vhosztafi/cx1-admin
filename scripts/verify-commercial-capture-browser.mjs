import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {spawn} from 'node:child_process';
import {randomUUID} from 'node:crypto';
import {chromium} from 'playwright';

const stage = process.argv[process.argv.indexOf('--stage') + 1];
assert.equal(stage, 'business-loss', 'Choose the implemented acceptance stage explicitly.');
if (!process.argv.includes('--worker')) {
  const output = `.local/phase8-03-browser-${randomUUID()}`;
  await mkdir(output, {recursive: true});
  const child = spawn('dotnet', ['test', 'backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj', '--no-restore',
    ...(process.argv.includes('--no-build') ? ['--no-build'] : []), '--filter', 'FullyQualifiedName~RealSqlCommercialCaptureBusinessLossBrowser',
    '--logger', 'trx;LogFileName=sql.trx', '--results-directory', output], {stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true});
  let log = ''; child.stdout.on('data', x => log += x); child.stderr.on('data', x => log += x);
  const code = await new Promise((resolve, reject) => {child.on('error', reject); child.on('close', resolve);});
  await writeFile(output + '/test.log', log); console.log(log.slice(-6500));
  assert.equal(code, 0, `Commercial capture browser failed: ${output}`);
  assert.match(await readFile(output + '/sql.trx', 'utf8'), /<Counters\b[^>]*total="1"[^>]*executed="1"[^>]*passed="1"[^>]*failed="0"/);
  await writeFile('.local/phase8-03-browser-current.txt', output);
} else {
  const f = JSON.parse(process.env.COVER_COMMERCIAL_BROWSER_FIXTURE);
  for (const origin of [f.apiOrigin, f.webOrigin]) assert.ok(['localhost', '127.0.0.1', '[::1]'].includes(new URL(origin).hostname));
  assert.notEqual(new URL(f.apiOrigin).port, '5000');
  const browser = await chromium.launch({channel: 'chrome', headless: true});
  const page = await browser.newPage({viewport: {width: 1480, height: 980}}); page.setDefaultTimeout(25000);
  await page.clock.setFixedTime(new Date(f.clockNow));
  const errors = []; page.on('pageerror', error => errors.push(error.message));
  await page.route('**/api/v1/**', async route => {const url = new URL(route.request().url()); const response = await route.fetch({url: f.apiOrigin + url.pathname + url.search}); await route.fulfill({response});});
  const button = name => page.getByRole('button', {name, exact: true});
  const field = name => page.getByLabel(name, {exact: true});
  const checks = []; let quoteId;
  const questions = JSON.parse(await readFile('contracts/quote-question-catalogue.json', 'utf8')).deferredQuestions;
  const businessQuestions = questions.filter(q => q.stages.includes('Commercial Combined:step-2'));
  async function login(email) {
    await page.goto(f.webOrigin + '/login'); await field('Email address').fill(email); await field('Password').fill(process.env.COVER_COMMERCIAL_BROWSER_PASSWORD);
    await button('Sign in').click(); await page.waitForURL(f.webOrigin + '/');
  }
  async function create(suffix, product) {
    const discovery = await page.request.get(f.apiOrigin + '/api/v1/clients?q=CL-QUOTE-STORAGE-' + suffix);
    assert.equal(discovery.status(), 200, await discovery.text());
    assert.equal((await discovery.json()).items.length, 1, `Fixture client must be discoverable through actual API: ${await discovery.text()}`);
    await page.goto(f.webOrigin + '/quotes/new'); await field('Search clients').fill('CL-QUOTE-STORAGE-' + suffix); await button('Search').click();
    await page.getByRole('button', {name: /Fictional quote client/}).click();
    await page.getByRole('radio', {name: /Fictional quote storage/}).check();
    await page.getByRole('radio', {name: product}).and(page.locator(':enabled')).check();
    const creation = page.waitForResponse(r => r.url().endsWith('/api/v1/quotes') && r.request().method() === 'POST');
    await button('Create quote draft').click(); const created = await creation; assert.equal(created.status(), 201, await created.text());
    await page.waitForURL(/\/quotes\/[0-9a-f-]{36}$/);
    const id = new URL(page.url()).pathname.split('/').at(-1);
    await page.getByRole('link', {name: 'Edit quote draft', exact: true}).click();
    return id;
  }
  async function read() {
    const response = await page.request.get(f.apiOrigin + `/api/v1/quotes/${quoteId}`);
    assert.equal(response.status(), 200, await response.text());
    return {view: await response.json(), etag: response.headers().etag};
  }
  async function write(proposal, etag) {
    const csrf = await (await page.request.get(f.apiOrigin + '/api/v1/auth/csrf')).json();
    return page.request.put(f.apiOrigin + `/api/v1/quotes/${quoteId}/proposal`, {headers: {'X-CSRF-Token': csrf.requestToken, 'Idempotency-Key': randomUUID(), 'If-Match': etag}, data: {proposal, reason: 'Fictional browser acceptance'}});
  }
  async function save() {
    const response = page.waitForResponse(r => r.url().endsWith(`/quotes/${quoteId}/proposal`) && r.request().method() === 'PUT');
    await button('Save quote draft').click(); const saved = await response; assert.equal(saved.status(), 200, await saved.text());
    await page.getByRole('status').filter({hasText: 'Draft saved.'}).waitFor(); return read();
  }
  const go = name => page.getByRole('navigation', {name: 'Quote stages', exact: true}).getByRole('button', {name: new RegExp(name)}).click();
  try {
    await login('underwriter@cover.example'); quoteId = await create('CC-BROWSER', /Commercial Combined/);
    await field('Legal name').fill('Fictional commercial browser business'); await field('Entity type').selectOption('charity-or-trust');
    await field('Proposer full name(s)').fill('Fictional Proposer One\nFictional Proposer Two');
    await field('Company number').fill('FICTIONAL01'); await field('Employers reference number (ERN)').fill('FICTIONAL/ERN');
    await field('Correspondence address line 1').fill('1 Fictional Street'); await field('Correspondence address line 2').fill('Fictional Suite');
    await field('Town').fill('London'); await field('Country').selectOption('GB'); await field('Telephone').fill('02079460000'); await field('Email').fill('fictional@example.test');
    await field('Business start date').fill('2024-01-01');
    const expectedAnswers = new Map();
    for (const q of businessQuestions) {
      if (q.kind === 'boolean') {await field(q.label).selectOption('no'); expectedAnswers.set(q.questionId, false);}
      else if (q.kind === 'reference') {const choice = q.referenceValues[0]; await field(q.label).selectOption(String(choice.value)); expectedAnswers.set(q.questionId, {collection:q.questionId, ...choice, version:'commercial-reference-1'});}
      else {const value = q.kind === 'percentage' ? '0' : 'Fictional captured history'; await field(q.label).fill(value); expectedAnswers.set(q.questionId, q.kind === 'percentage' ? 0 : value);}
    }
    await field('Business or trade description').fill('Fictional warehouse and retail activity'); await field('Estimated annual turnover').fill('125000.01');
    await field('VAT registered').selectOption('no'); await field('Correspondence postcode').fill('SW1A 1AA');
    await button('Add activity').click(); await field('Turnover share (%) 1').fill('bad'); assert.equal(await button('Save quote draft').isDisabled(), true);
    await button('Remove activity 1').click(); assert.equal(await button('Save quote draft').isEnabled(), true);
    await button('Add activity').click(); await field('Activity code 1').fill('WAREHOUSE'); await field('Activity description 1').fill('Fictional warehouse'); await field('Turnover share (%) 1').fill('100');
    const business = await save(); assert.equal(business.view.proposal.insured.entityType, 'charity-or-trust'); assert.equal(business.view.proposal.risk.business.turnover, '125000.01');
    assert.deepEqual(business.view.proposal.insured.proposerNames, ['Fictional Proposer One', 'Fictional Proposer Two']);
    assert.equal(business.view.proposal.insured.address.line1, '1 Fictional Street'); assert.equal(business.view.proposal.insured.companyNumber, 'FICTIONAL01');
    for (const q of businessQuestions) {
      const responses = q.targetContainer.split('.').reduce((value, key) => value[key], business.view.proposal);
      assert.deepEqual(responses.answers.find(x => x.questionId === q.questionId).value, expectedAnswers.get(q.questionId), q.questionId);
    }
    const activityId = business.view.proposal.risk.business.activities[0].id;
    await page.reload(); await field('Legal name').waitFor(); assert.equal(await field('Estimated annual turnover').inputValue(), '125000.01');
    assert.equal((await read()).view.proposal.risk.business.activities[0].id, activityId);
    checks.push('CC browser creation, charity/trust, exact turnover, activity validation/removal and stable saved identity');
    await page.screenshot({path: f.output + '/business-desktop.png'});
    await go('Claims & losses'); await button('Add loss').click();
    await field('Loss date').fill('2025-07-01'); await field('Loss type').selectOption('1'); await field('Loss amount').fill('10.011');
    assert.equal(await button('Apply loss details').isDisabled(), true); await field('Loss amount').fill('10.01');
    await field('Paid amount').fill('0'); await field('Outstanding reserve').fill('10.01'); await field('Loss status').selectOption('open');
    await field('Was it insured').selectOption('no');
    await field('Loss circumstances').fill('Fictional loss for persistent browser checks'); await button('Apply loss details').click();
    const firstLoss = (await save()).view.proposal.risk.losses[0]; assert.equal(firstLoss.amount, '10.01'); assert.equal(firstLoss.paid, '0.00'); assert.equal(firstLoss.occurredOn, '2025-07-01');
    assert.equal(firstLoss.responses.answers.find(x => x.questionId === 'prototype.addloss.insured').value, false);
    await button('Edit loss 1').click(); await field('Loss amount').fill('12.34'); await button('Apply loss details').click();
    const edited = (await save()).view.proposal.risk.losses[0]; assert.equal(edited.id, firstLoss.id); assert.equal(edited.amount, '12.34');
    await page.reload(); await go('Claims & losses'); await page.getByRole('cell', {name: '£12.34', exact: true}).waitFor();
    await button('Edit loss 1').click(); await field('Loss amount').fill('999'); await button('Discard loss edits').click();
    assert.equal((await read()).view.proposal.risk.losses[0].amount, '12.34');
    await page.setViewportSize({width: 390, height: 844}); assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.screenshot({path: f.output + '/loss-mobile.png'});
    await button('Edit loss 1').click(); assert.equal(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth), true);
    await page.screenshot({path: f.output + '/loss-dialog-mobile.png'}); await button('Discard loss edits').click();
    await page.setViewportSize({width: 1480, height: 980});
    page.once('dialog', dialog => dialog.accept()); await button('Remove loss 1').click(); assert.deepEqual((await save()).view.proposal.risk.losses, []);
    const lossQuestion = questions.find(q => q.products.includes('commercial-combined') && q.label.toLowerCase().includes('loss') && q.kind === 'boolean' && q.targetContainer === 'risk.declarations');
    assert.ok(lossQuestion, 'Find the source loss declaration.');
    await field('Any circumstances that could give rise to a claim').selectOption('no');
    await field(lossQuestion.label).selectOption('no'); const noLoss = await save();
    assert.equal(noLoss.view.proposal.risk.declarations.answers.find(x => x.questionId === lossQuestion.questionId).value, false);
    await field(lossQuestion.label).selectOption(''); const unknown = await save();
    assert.equal(unknown.view.proposal.risk.declarations.answers.some(x => x.questionId === lossQuestion.questionId), false);
    const foreign = structuredClone(unknown.view.proposal); foreign.risk.losses = [{id: randomUUID(), riskItemId: randomUUID()}];
    const denied = await write(foreign, unknown.etag); assert.equal(denied.status(), 422); assert.equal((await read()).etag, unknown.etag);
    checks.push('loss add/edit/discard/remove, exact money and stable ID, persisted No versus unanswered, foreign location rejected, 390px screen/dialog');
    await go('Proposer & history'); await field('Legal name').fill('Retained unsaved business');
    const current = await read(); const concurrent = structuredClone(current.view.proposal); concurrent.insured.legalName = 'Concurrent saved business';
    assert.equal((await write(concurrent, current.etag)).status(), 200);
    const stale = page.waitForResponse(r => r.url().endsWith(`/quotes/${quoteId}/proposal`) && r.request().method() === 'PUT');
    await button('Save quote draft').click(); assert.equal((await stale).status(), 412);
    assert.equal(await field('Legal name').inputValue(), 'Retained unsaved business'); await button('Load saved comparison').click();
    await button('Discard my edits and load saved revision').click(); assert.equal(await field('Legal name').inputValue(), 'Concurrent saved business');
    await field('Trading as').fill('Confirmed exact retry'); const attempts = [];
    const savePath = `/api/v1/quotes/${quoteId}/proposal`;
    await page.route('**' + savePath, async route => {
      const request = route.request(); attempts.push({body: request.postData(), key: request.headers()['idempotency-key'], etag: request.headers()['if-match']});
      if (attempts.length === 2) {await route.fulfill({status: 403, contentType: 'application/json', body: '{}'}); return;}
      const response = await route.fetch({url: f.apiOrigin + savePath}); assert.equal(response.status(), 200);
      if (attempts.length === 1) await route.abort('failed'); else await route.fulfill({response});
    });
    await button('Save quote draft').click(); await button('Retry same save').waitFor(); assert.equal(await field('Legal name').isDisabled(), true);
    await button('Retry same save').click(); await page.getByRole('heading', {name: 'Quote access changed', exact: true}).waitFor();
    assert.equal(await field('Legal name').count(), 0); await button('Check current access').click();
    await button('Retry same save').waitFor(); assert.equal(await field('Legal name').isDisabled(), true);
    await button('Retry same save').click(); await page.getByRole('status').filter({hasText: 'Draft saved.'}).waitFor();
    assert.equal(attempts.length, 3); assert.deepEqual(attempts[0], attempts[1]); assert.deepEqual(attempts[0], attempts[2]); await page.unroute('**' + savePath);
    assert.equal((await read()).view.proposal.insured.tradingName, 'Confirmed exact retry');
    checks.push('stale save retains input, read-only comparison and explicit discard; committed lost response and intervening denial retain identical body/key/ETag and require current access recovery');
    const mtId = await create('MT-BROWSER', /Road Risks/); assert.notEqual(mtId, quoteId);
    const mt = await page.request.get(f.apiOrigin + `/api/v1/quotes/${mtId}`); assert.equal((await mt.json()).productCode, 'motor-trade-road-risks');
    await page.getByRole('heading', {name: /Edit QT-/}).waitFor(); assert.equal(await field('Entity type').count(), 0);
    checks.push('retained Motor Trade creation and editor route');
    assert.deepEqual(errors, []); await writeFile(f.output + '/report.json', JSON.stringify({quoteId, activityId, lossId: firstLoss.id, mtId, verifiedQuestionIds:[...businessQuestions.map(q=>q.questionId), 'prototype.addloss.insured', lossQuestion.questionId, 'prototype.quote.385743089b72'], checks}, null, 2));
    console.log(JSON.stringify({output: f.output, checks}));
  } catch (error) {await page.screenshot({path: f.output + '/failure.png'}).catch(() => {}); throw error;}
  finally {await browser.close();}
}
