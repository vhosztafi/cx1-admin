import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import Ajv from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import { amountInput, percentageInput, countInput, fieldValue, changeField, changeAnswer, selectedReference } from '../lib/quote-form.ts';

const readJson = async path => JSON.parse(await readFile(new URL(`../../../contracts/${path}`, import.meta.url), 'utf8'));
const schema = await readJson('schemas/quote-draft.schema.json');
const references = await readJson('reference-data/motor-trade-capture.json');
const ajv = new Ajv({ strict: true, allErrors: true }); addFormats(ajv);
const valid = ajv.compile(schema);
const empty = () => ({ schemaVersion: '1.0', productCode: 'motor-trade-road-risks' });

test('GBP input preserves maximum decimal precision without binary rounding or truncation', () => {
  for (const [input, expected] of [['0', '0.00'], ['0.1', '0.10'], ['1.01', '1.01'], ['9999999999999.99', '9999999999999.99']]) assert.equal(amountInput(input).value, expected);
  assert.deepEqual(amountInput(''), { value: undefined });
  for (const input of ['-1', '1e2', '01.00', '1,000', '1.005', '1.', ' 1', '10000000000000', 'Infinity']) assert.ok(amountInput(input).error, input);
});

test('percentage capture keeps exact integer basis points and distinguishes blank from explicit zero', () => {
  assert.equal(percentageInput('33.33').value, 3333);
  assert.equal(percentageInput('33.34').value, 3334);
  assert.equal(percentageInput('33.33').value * 2 + percentageInput('33.34').value, 10000);
  assert.equal(percentageInput('0').value, 0); assert.equal(percentageInput('100.00').value, 10000);
  assert.deepEqual(percentageInput(''), { value: undefined });
  for (const text of ['100.01', '0.001', '-0', '1e1', 'NaN', ' 50', '05']) assert.ok(percentageInput(text).error, text);
});

test('integer counts retain zero without accepting floats exponent syntax or overflow', () => {
  assert.equal(countInput('0').value, 0); assert.equal(countInput('1000000').value, 1000000);
  assert.deepEqual(countInput(''), { value: undefined });
  for (const input of ['1000001', '1.0', '01', '-1', '1e2']) assert.ok(countInput(input).error);
});

test('one field edit preserves full names sibling data child IDs and an independent proposal snapshot', () => {
  const initial = { ...empty(), insured: { proposerNames: ['Alex Example', 'Sam van Example'], contact: { email: 'fictional@example.test' } }, risk: { drivers: [{ id: '51000000-0000-4000-8000-000000000011', fullName: 'Sam van Example' }] } };
  const snapshot = structuredClone(initial);
  const next = changeField(initial, 'insured.contact.telephone', '01141234567');
  assert.deepEqual(initial, snapshot); assert.deepEqual(next.insured.proposerNames, snapshot.insured.proposerNames);
  assert.equal(next.insured.contact.email, snapshot.insured.contact.email);
  next.risk.drivers[0].fullName = 'A different draft'; assert.deepEqual(initial, snapshot);
  const cleared = changeField(next, 'insured.contact.telephone', undefined);
  assert.equal(fieldValue(cleared, 'insured.contact.telephone'), undefined);
  assert.equal(fieldValue(cleared, 'insured.contact.email'), 'fictional@example.test');
  for (const path of ['productCode', 'risk.drivers.0.fullName', 'insured.__proto__.polluted', 'insured.constructor.polluted']) assert.throws(() => changeField(initial, path, 'x'));
  assert.throws(() => changeField(initial, 'risk.drivers.fullName', 'x')); assert.equal({}.polluted, undefined);
});

test('versioned answers replace by question identity despite order and preserve explicit false zero and empty selections', () => {
  const version = references.version;
  let proposal = changeAnswer(empty(), 'insured.responses', version, 'MTS-01-Q01', 'boolean', false);
  proposal = changeAnswer(proposal, 'insured.responses', version, 'MTS-01-Q02', 'references', []);
  const snapshot = structuredClone(proposal);
  const next = changeAnswer(proposal, 'insured.responses', version, 'MTS-01-Q01', 'boolean', true);
  assert.deepEqual(proposal, snapshot); assert.equal(next.insured.responses.answers.length, 2);
  assert.deepEqual(next.insured.responses.answers.map(answer => answer.value), [true, []]);
  const cleared = changeAnswer(next, 'insured.responses', version, 'MTS-01-Q01', 'boolean', undefined);
  assert.deepEqual(cleared.insured.responses.answers.map(answer => answer.questionId), ['MTS-01-Q02']);
  const count = changeAnswer(cleared, 'risk.business.responses', version, 'MTS-03-Q08', 'count', 0);
  assert.equal(count.risk.business.responses.answers[0].value, 0);
  assert.ok(valid(count), JSON.stringify(valid.errors));
});

test('editing refuses silent catalogue upgrades duplicate identities and malformed retained answer containers', () => {
  const version = references.version;
  const proposal = changeAnswer(empty(), 'insured.responses', version, 'MTS-01-Q01', 'boolean', false);
  assert.throws(() => changeAnswer(proposal, 'insured.responses', 'future-version', 'MTS-01-Q01', 'boolean', true));
  proposal.insured.responses.answers.push(structuredClone(proposal.insured.responses.answers[0]));
  assert.throws(() => changeAnswer(proposal, 'insured.responses', version, 'MTS-01-Q01', 'boolean', true));
  assert.throws(() => changeAnswer({ ...empty(), insured: { responses: 'broken' } }, 'insured.responses', version, 'MTS-01-Q01', 'boolean', true));
  for (const [kind, value] of [['boolean', 'false'], ['count', 1.2], ['percentage', 10001], ['money', 1.01], ['unknown', true]]) assert.throws(() => changeAnswer(empty(), 'insured.responses', version, 'test-question', kind, value));
});

test('reference choices copy exact trusted value and label without string-number coercion', () => {
  const choices = references.collections.companyTypes;
  const reference = selectedReference('companyTypes', references.version, choices, choices[0].value);
  assert.deepEqual(reference, { collection: 'companyTypes', version: references.version, value: choices[0].value, label: choices[0].text });
  assert.throws(() => selectedReference('companyTypes', references.version, choices, String(choices[0].value)));
  assert.throws(() => selectedReference('companyTypes', references.version, choices, 999999));
  assert.throws(() => selectedReference('companyTypes', references.version, [choices[0], choices[0]], choices[0].value));
  const proposal = changeField(empty(), 'insured.declaredCompanyType', reference);
  reference.label = 'Changed caller value'; assert.equal(proposal.insured.declaredCompanyType.label, choices[0].text);
  assert.ok(valid(proposal), JSON.stringify(valid.errors));
});

test('canonical amount and percentage answer metadata passes the strict draft schema for both products', () => {
  for (const productCode of ['motor-trade-road-risks', 'motor-trade-combined']) {
    let proposal = { ...empty(), productCode };
    proposal = changeAnswer(proposal, 'risk.business.responses', references.version, 'example-money', 'money', '9999999999999.99');
    proposal = changeAnswer(proposal, 'risk.business.responses', references.version, 'example-percentage', 'percentage', 3333);
    assert.equal(proposal.risk.business.responses.answers[0].currency, 'GBP');
    assert.equal(proposal.risk.business.responses.answers[1].unit, 'basis-points');
    assert.ok(valid(proposal), JSON.stringify(valid.errors));
  }
});


test('business conditional answers retain child details and unrelated answers until explicitly cleared', () => {
  const version = references.version;
  let draft = changeAnswer(empty(), 'risk.business.responses', version, 'MTS-03-Q04', 'boolean', true);
  draft = changeAnswer(draft, 'risk.business.responses', version, 'MTS-03-Q05', 'text', 'Fictional Trade Association');
  draft = changeAnswer(draft, 'risk.business.responses', version, 'MTS-03-Q08', 'count', 0);
  const previous = structuredClone(draft);
  draft = changeAnswer(draft, 'risk.business.responses', version, 'MTS-03-Q04', 'boolean', false);
  assert.equal(draft.risk.business.responses.answers.find(x => x.questionId === 'MTS-03-Q05').value, 'Fictional Trade Association');
  assert.equal(previous.risk.business.responses.answers[0].value, true);
  draft = changeAnswer(draft, 'risk.business.responses', version, 'MTS-03-Q05', 'text', undefined);
  assert.deepEqual(draft.risk.business.responses.answers.map(x => [x.questionId, x.value]), [['MTS-03-Q04', false], ['MTS-03-Q08', 0]]);
  assert.equal(valid(draft), true, JSON.stringify(valid.errors));
});
