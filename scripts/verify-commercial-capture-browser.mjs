import assert from 'node:assert/strict';
import {mkdir, readFile, writeFile} from 'node:fs/promises';
import {spawn} from 'node:child_process';
import {randomUUID} from 'node:crypto';
import {chromium} from 'playwright';
import {underwritingResponseValidator} from './validate-underwriting-response.mjs';

const stage = process.argv.includes('--stage') ? process.argv[process.argv.indexOf('--stage') + 1] : 'full';
assert.ok(['business-loss','full','rating'].includes(stage), 'Choose an implemented acceptance stage.');
const plan = stage==='rating'?'05':stage==='full'?'04':'03';
if (!process.argv.includes('--worker')) {
  const output = `.local/phase8-${plan}-browser-${randomUUID()}`;
  await mkdir(output, {recursive: true});
  const child = spawn('dotnet', ['test', 'backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj', '--no-restore',
    ...(process.argv.includes('--no-build') ? ['--no-build'] : []), '--filter', `FullyQualifiedName~${stage==='rating'?'RealSqlCommercialRatingBrowser':`RealSqlCommercialCapture${stage==='full'?'Full':'BusinessLoss'}Browser`}`,
    '--logger', 'trx;LogFileName=sql.trx', '--results-directory', output], {stdio: ['ignore', 'pipe', 'pipe'], windowsHide: true});
  let log = ''; child.stdout.on('data', x => log += x); child.stderr.on('data', x => log += x);
  const code = await new Promise((resolve, reject) => {child.on('error', reject); child.on('close', resolve);});
  await writeFile(output + '/test.log', log); console.log(log.slice(-6500));
  assert.equal(code, 0, `Commercial capture browser failed: ${output}`);
  assert.match(await readFile(output + '/sql.trx', 'utf8'), /<Counters\b[^>]*total="1"[^>]*executed="1"[^>]*passed="1"[^>]*failed="0"/);
  await writeFile(`.local/phase8-${plan}-browser-current.txt`, output);
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
  const verifiedQuestionIds = new Set();
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
  async function fullCapture() {
    const valuesFor = new Map();
    async function answer(q) {
      const control=field(q.label); await control.waitFor();
      let value;
      if(q.kind==='boolean') {
        value=['prototype.quote.be47c08f530f','prototype.quote.7660fc5eb42e','prototype.quote.c70fcdf94e4e','prototype.addloc.detached','prototype.quote.8cc83c3a006f'].includes(q.questionId)||q.label.includes('above 2 metres');
        await control.selectOption(value?'yes':'no');
      } else if(q.kind==='reference') {
        const options=q.referenceValues?.length?q.referenceValues:q.sourceOptions.map((label,index)=>({value:index+1,label}));
        assert.deepEqual(await control.locator('option').allTextContents(),['Not answered',...options.map(x=>x.label)],q.questionId+' exact options');
        const option=q.questionId==='prototype.quote.ade0f3f0df5e'||q.questionId==='prototype.quote.4a288359be03'?options[1]:options[0];
        await control.selectOption(String(option.value));value={collection:q.questionId,...option,version:'commercial-reference-1'};
      } else {
        const text=q.kind==='count'?(q.questionId==='prototype.addloc.year-built'?'2000':'2'):q.kind==='percentage'?'0':q.kind==='money'?'0.00':'Fictional declared details';
        await control.fill(text);value=['count','percentage'].includes(q.kind)?Number(text):text;
      }
      valuesFor.set(q.questionId,value);
    }
    function checkAnswers(proposal,items,item) {
      for(const q of items) {
        const container=q.targetContainer.includes('[]')?item.responses:q.targetContainer.split('.').reduce((value,key)=>value?.[key],proposal);
        assert.deepEqual(container?.answers.find(x=>x.questionId===q.questionId)?.value,valuesFor.get(q.questionId),q.questionId);
        verifiedQuestionIds.add(q.questionId);
      }
    }
    async function globals(number) {
      const items=questions.filter(q=>q.stages.includes(`Commercial Combined:step-${number}`)&&!q.targetContainer.includes('[]'));
      for(const q of items)await answer(q);
      const current=await save();checkAnswers(current.view.proposal,items);return current;
    }
    const locationQuestions=questions.filter(q=>q.targetContainer==='risk.locations[].responses');
    async function addLocation(index) {
      await button('Add location').click();await field('Location reference').fill(`LOC-${index}`);
      await field('Location address line 1').fill(`${index} Fictional Works`);await field('Location address line 2').fill('Fictional unit');
      await field('Location town').fill('Sheffield');await field('Location county').fill('South Yorkshire');
      await field('Location postcode').fill('ZZ9 9ZZ');assert.equal(await button('Apply location details').isDisabled(),true);
      await field('Location postcode').fill(index===2?'S4 7AA':'S9 2QT');await field('Location country').selectOption('GB');
      await field('Location use').selectOption('Warehouse and storage');await field('Sprinklers present').selectOption('no');await field('Declared flood zone').selectOption('1');
      await field('Buildings sum insured').fill('100000.011');assert.equal(await button('Apply location details').isDisabled(),true);
      await field('Buildings sum insured').fill(index===2?'200000.00':'100000.01');await field('Contents sum insured').fill('25000.02');await field('Stock sum insured').fill('5000.03');await field('Supplied maximum loss estimate').fill('120000.00');
      for(const q of locationQuestions)await answer(q);
      await button('Apply location details').click();const current=await save();const row=current.view.proposal.risk.locations.at(-1);
      checkAnswers(current.view.proposal,locationQuestions,row);return row;
    }
    await go('Agency & product');await field('Term type').selectOption('annual');await field('Requested start date').fill('2026-09-16');await field('Requested start time').fill('00:00');await save();
    await go('Locations & occupancy');const first=await addLocation(1);const second=await addLocation(2);await addLocation(3);
    page.once('dialog',dialog=>dialog.accept());await button('Remove location 3').click();assert.equal((await save()).view.proposal.risk.locations.length,2);
    await button('Edit location 1').click();await field('Contents sum insured').fill('25000.12');await button('Apply location details').click();
    const edited=(await save()).view.proposal.risk.locations;assert.equal(edited[0].id,first.id);assert.equal(edited[1].id,second.id);assert.equal(edited[0].contents,'25000.12');assert.equal(edited[1].contents,'25000.02');
    await globals(4);
    await go('Claims & losses');await button('Add loss').click();await field('Loss date').fill('2025-07-01');await field('Loss type').selectOption('1');await field('Loss location').selectOption(first.id);
    await field('Loss amount').fill('1');await field('Paid amount').fill('0');await field('Outstanding reserve').fill('1');await field('Loss status').selectOption('open');await field('Loss circumstances').fill('Fictional owned-location loss');await field('Was it insured').selectOption('no');await button('Apply loss details').click();
    assert.equal((await save()).view.proposal.risk.losses[0].riskItemId,first.id);
    await go('Locations & occupancy');page.once('dialog',dialog=>dialog.accept());await button('Remove location 1').click();await page.getByRole('alert').filter({hasText:'Clear or change the linked loss locations'}).waitFor();assert.equal((await read()).view.proposal.risk.locations.length,2);
    await go('Claims & losses');page.once('dialog',dialog=>dialog.accept());await button('Remove loss 1').click();await field('Any loss or damage in the last 5 years').selectOption('no');await save();
    await go('Construction & protections');await globals(5);await go('Flood & subsidence');await globals(6);
    await field('Subsidence, ground heave or landslip cover requested').selectOption('no');await save();assert.equal(await field('Any signs of damage attributable to subsidence, heave or landslip').count(),0);
    await field('Subsidence, ground heave or landslip cover requested').selectOption('yes');await save();
    await go('Sums insured & BI');await globals(7);
    async function biDetails(){
      await field('Business interruption basis').selectOption('estimated-gross-profit');await field('Business interruption sum insured').fill('500000.01');await field('Indemnity period').selectOption('24');await field('BI declaration linked').selectOption('no');
      await field('Suppliers or customers extension').selectOption('3');await button('Add dependency').click();await field('Dependency type 1').selectOption('supplier');await field('Dependency name 1').fill('Fictional supplier');await field('Dependency limit 1').fill('10000.01');
    }
    await biDetails();await save();await field('Business interruption required').selectOption('no');page.once('dialog',dialog=>dialog.accept());await button('Clear retained BI details').click();
    assert.equal((await save()).view.proposal.risk.businessInterruption,undefined);await field('Business interruption required').selectOption('yes');
    for(const q of questions.filter(q=>['prototype.quote.7bd824326d4c','prototype.quote.9b4688f28580','prototype.quote.72cb884b6df1','prototype.quote.a2f1dd35162a','prototype.quote.ab908171e40b'].includes(q.questionId)))await answer(q);
    await biDetails();await save();
    const wageQuestions=questions.filter(q=>q.targetContainer==='risk.wages[].responses');
    async function wage(index){
      await button('Add wage category').click();await field('Wage category').selectOption(index===1?'manual-on-premises':'clerical');
      await field('Employee wages').fill(index===1?'50000.01':'25000.02');await field('Labour-only subcontractor wages').fill('10000');await field('Bona fide subcontractor wages').fill('0');await field('Wage category notes').fill('Fictional annual wages');
      for(const q of wageQuestions)await answer(q);await button('Apply wage details').click();const current=await save();const row=current.view.proposal.risk.wages.at(-1);checkAnswers(current.view.proposal,wageQuestions,row);return row;
    }
    await go('Liability & wage roll');await field('Employers’ liability required').selectOption('yes');const wageOne=await wage(1);const wageTwo=await wage(2);await wage(3);
    page.once('dialog',dialog=>dialog.accept());await button('Remove wage category 3').click();await save();
    await button('Edit wage category 1').click();await field('Employee wages').fill('55000.01');await button('Apply wage details').click();const wages=(await save()).view.proposal.risk.wages;
    assert.equal(wages[0].id,wageOne.id);assert.equal(wages[1].id,wageTwo.id);assert.equal(wages[1].employees,'25000.02');
    await field('Employers’ liability limit').selectOption('10000000.00');await field('Public liability limit').selectOption('5000000.00');await field('Products liability limit').selectOption('0.00');await field('Employers reference number (ERN)').fill('FICTIONAL/ERN');
    await field('Maximum working height (metres)').fill('1e2');assert.equal(await button('Save quote draft').isDisabled(),true);
    await button('Enter a height from 0 to 1,000 metres with at most two decimal places.').click();
    assert.equal(await field('Maximum working height (metres)').evaluate(node=>node===document.activeElement),true);
    await field('Maximum working height (metres)').fill('2.05');await field('Hot work procedures').fill('Fictional permit and fire-watch procedure');await globals(8);
    await field('Employers’ liability required').selectOption('no');page.once('dialog',dialog=>dialog.accept());await button('Clear retained EL details').click();const cleared=await save();assert.deepEqual(cleared.view.proposal.risk.wages,[]);assert.equal(cleared.view.proposal.risk.liability.employersLimit,undefined);
    await field('Employers’ liability required').selectOption('yes');await wage(1);await wage(2);await field('Employers’ liability limit').selectOption('10000000.00');await field('Employers reference number (ERN)').fill('FICTIONAL/ERN');await save();
    await go('Health & safety');await globals(9);await go('Cover & declarations');await globals(10);
    await field('Contract works required').selectOption('yes');await field('Contract works sum insured').fill('10000.01');await field('Contract works excess').fill('250');await field('Other material facts').fill('Fictional fully captured commercial proposal for browser verification.');await save();
    await field('Contract works required').selectOption('no');page.once('dialog',dialog=>dialog.accept());await button('Clear retained contract works details').click();assert.equal((await save()).view.proposal.cover.contractWorks.sumInsured,undefined);
    await field('Contract works required').selectOption('yes');await field('Contract works sum insured').fill('10000.01');await field('Contract works excess').fill('250');
    const final=await save();assert.equal(final.view.proposal.risk.businessInterruption.basis,'estimated-gross-profit');assert.equal(verifiedQuestionIds.size,109);
    assert.equal(final.view.readiness.ready,true,JSON.stringify(final.view.readiness.issues));
    await writeFile(f.output+'/full-readiness.json',JSON.stringify(final.view.readiness,null,2));
    await writeFile(f.output+'/full-proposal.json',JSON.stringify(final.view.proposal,null,2));
    await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:f.output+'/full-cover-mobile.png'});
    await go('Locations & occupancy');await button('Edit location 2').click();assert.equal(await field('Location postcode').inputValue(),'S4 7AA');assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:f.output+'/location-dialog-mobile.png'});await button('Discard item edits').click();
    await page.reload();await go('Liability & wage roll');assert.equal(await button('Edit wage category 2').count(),1);assert.equal((await read()).view.proposal.risk.locations[0].id,first.id);
    await page.setViewportSize({width:1480,height:980});await page.screenshot({path:f.output+'/wages-desktop.png'});
    checks.push('all109 source questions with exact options and scoped readback; two locations/wages add-edit-remove; owned loss link and blocked location removal; explicit EL/BI/contract-works clearing; invalid postcode/money/height; all stages and390px reload');
  }
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
      if (q.kind === 'boolean') {const yes=q.questionId==='prototype.quote.cfb8d4a5494b'; await field(q.label).selectOption(yes?'yes':'no'); expectedAnswers.set(q.questionId, yes);}
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
      verifiedQuestionIds.add(q.questionId);
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
    verifiedQuestionIds.add('prototype.addloss.insured');
    await button('Edit loss 1').click(); await field('Loss amount').fill('12.34'); await button('Apply loss details').click();
    const edited = (await save()).view.proposal.risk.losses[0]; assert.equal(edited.id, firstLoss.id); assert.equal(edited.amount, '12.34');
    assert.match(await page.locator('p').filter({hasText:'Total declared loss amount:'}).innerText(),/£12\.34/);
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
    assert.equal(noLoss.view.proposal.risk.declarations.answers.find(x=>x.questionId==='prototype.quote.385743089b72').value,false);
    verifiedQuestionIds.add(lossQuestion.questionId); verifiedQuestionIds.add('prototype.quote.385743089b72');
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
    if(stage==='full'||stage==='rating') await fullCapture();
    if(stage==='rating') {
      await page.goto(f.webOrigin+`/quotes/${quoteId}`);
      const panel = page.locator('.panel').filter({has:page.getByRole('heading',{name:'Commercial Combined rating',exact:true})});
      await panel.getByRole('button',{name:'Rate quote',exact:true}).click();
      await field('Underwriting action reason').fill('Fictional browser rating acceptance');
      const response = page.waitForResponse(r=>r.url().endsWith(`/quotes/${quoteId}/rate`)&&r.request().method()==='POST');
      await page.getByRole('dialog').getByRole('button',{name:'Rate quote',exact:true}).click();
      const requested = await response; assert.equal(requested.status(),202,await requested.text());
      let assessed;
      for(let attempt=0;attempt<200;attempt++) {
        const result=await page.request.get(f.apiOrigin+`/api/v1/quotes/${quoteId}/underwriting`);assert.equal(result.status(),200,await result.text());assessed=await result.json();
        if(assessed.ratingId)break;
        assert.ok(!assessed.blockers.some(x=>x.code==='quote-rating-failed'),JSON.stringify(assessed));
        await new Promise(resolve=>setTimeout(resolve,250));
      }
      assert.ok(assessed.ratingId,JSON.stringify(assessed));assert.equal(assessed.capabilities.canIssue,false);assert.equal(assessed.capabilities.canSubmit,false);
      const saved=await page.request.get(f.apiOrigin+`/api/v1/ratings/${assessed.ratingId}`);assert.equal(saved.status(),200,await saved.text());const price=await saved.json();
      for(const [name,value] of [['UnderwritingAssessment',assessed],['UnderwritingRatingView',price]]) {const valid=await underwritingResponseValidator(name);assert.equal(valid(value),true,JSON.stringify(valid.errors));}
      assert.equal(price.input.productCode,'commercial-combined');assert.equal(price.applicable,true);assert.equal(price.fee,'75.00');assert.ok(price.factors.some(x=>x.multiplierBasisPoints===17000));
      await panel.getByRole('button',{name:'Re-rate quote',exact:true}).and(page.locator(':enabled')).waitFor();
      await page.reload();
      const gross=new Intl.NumberFormat('en-GB',{style:'currency',currency:'GBP'}).format(Number(price.grossPayable));
      await panel.getByText(gross,{exact:true}).waitFor();
      await panel.locator('summary').filter({hasText:'Saved rating factors'}).click();
      await panel.getByRole('region',{name:'Commercial rating factors'}).waitFor();
      await page.evaluate(()=>window.scrollTo(0,0));await page.screenshot({path:f.output+'/rating-desktop.png',fullPage:true});
      await page.setViewportSize({width:390,height:844});await page.evaluate(()=>window.scrollTo(0,0));await page.screenshot({path:f.output+'/rating-mobile.png',fullPage:true});
      assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth+1),true);
      await page.setViewportSize({width:1480,height:980});
      await writeFile(f.output+'/rating.json',JSON.stringify(price,null,2));await writeFile(f.output+'/rating-assessment.json',JSON.stringify(assessed,null,2));
      checks.push('CC durable rating requested from actual UI, actual API schemas validated, saved premium/factors/multiplier read back and displayed after reload at desktop/390px, no terms/issue authority fabricated');
    }
    const mtId = await create('MT-BROWSER', /Road Risks/); assert.notEqual(mtId, quoteId);
    const mt = await page.request.get(f.apiOrigin + `/api/v1/quotes/${mtId}`); assert.equal((await mt.json()).productCode, 'motor-trade-road-risks');
    await page.getByRole('heading', {name: /Edit QT-/}).waitFor(); assert.equal(await field('Entity type').count(), 0);
    checks.push('retained Motor Trade creation and editor route');
    assert.deepEqual(errors, []); await writeFile(f.output + '/report.json', JSON.stringify({quoteId, activityId, lossId: firstLoss.id, mtId, verifiedQuestionIds:[...verifiedQuestionIds], checks}, null, 2));
    console.log(JSON.stringify({output: f.output, checks}));
  } catch (error) {await page.screenshot({path: f.output + '/failure.png'}).catch(() => {}); throw error;}
  finally {await browser.close();}
}
