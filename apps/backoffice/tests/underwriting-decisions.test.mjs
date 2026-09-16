import test from 'node:test';
import assert from 'node:assert/strict';
import { conditionFromForm, referralDecisionCommand, proofMatches, riskTargetLabel } from '../lib/underwriting-decisions.ts';

const quote = 'aaaaaaaa-0000-4000-8000-000000000001', cycle = 'aaaaaaaa-0000-4000-8000-000000000002';
const child = 'aaaaaaaa-0000-4000-8000-000000000003', driver = 'aaaaaaaa-0000-4000-8000-000000000004';
const etag = '"AAAAAAAAAAE="';
const proposal = { risk: { drivers: [{ id: driver, firstName: 'Alex', surname: 'Demo' }], premises: [], vehicles: [] } };
test('risk labels identify the actual saved target without inventing a driver', () => {
  assert.equal(riskTargetLabel(proposal, driver), 'Driver 1 · Alex Demo');
  assert.equal(riskTargetLabel(proposal, child), 'Historical risk target');
  assert.equal(riskTargetLabel(proposal), '');
});
test('bulk decisions retain exact selected child versions and cannot include a previous cycle', () => {
  const row = { id: child, etag, cycleId: cycle, state: 'open' };
  const command = referralDecisionCommand(quote, cycle, etag, [row], [child], 'query', 'Need proof', [{ code: 'provide-trading-history' }], 'Supply history');
  row.etag = '"AAAAAAAAAAI="';
  assert.equal(JSON.parse(command.body).decisions[0].etag, etag);
  assert.equal(JSON.parse(command.body).decisions[0].question, 'Supply history');
  assert.throws(() => referralDecisionCommand(quote, cycle, etag, [{ ...row, cycleId: quote }], [child], 'approve', 'Checked'));
  assert.throws(() => referralDecisionCommand(quote, cycle, etag, [row], [child, child], 'approve', 'Checked'));
  assert.throws(() => referralDecisionCommand(quote, cycle, etag, [row], [child], 'approve-with-conditions', 'Checked'));
});
test('condition forms emit closed definitions and require actual same-risk targets', () => {
  assert.deepEqual(conditionFromForm('named-drivers-only', { driverIds: [driver] }, proposal), { code: 'named-drivers-only', driverIds: [driver], wordingVersion: '1' });
  assert.throws(() => conditionFromForm('provide-driver-proof', { targetId: child, requirementCode: 'driving-record' }, proposal));
  assert.throws(() => conditionFromForm('provide-signed-statement', {}, proposal));
  assert.throws(() => conditionFromForm('any-driver-minimum-licence', { minimumYears: '0' }, proposal));
  assert.deepEqual(conditionFromForm('any-driver-minimum-licence', { minimumYears: '2' }, proposal), { code: 'any-driver-minimum-licence', minimumYears: 2, wordingVersion: '1' });
});
test('reviewed condition proof must match purpose, target, fingerprint and cycle', () => {
  const requirement = { code: 'driving-record', riskItemId: driver, conditionId: child, inputFingerprint: 'a'.repeat(64) };
  const evidence = { cycleId: cycle, requirementCode: requirement.code, riskItemId: driver, conditionId: child, inputFingerprint: requirement.inputFingerprint, reviewState: 'accepted', screeningState: 'accepted', withdrawn: false };
  assert.ok(proofMatches(evidence, requirement, cycle));
  for (const change of [{ withdrawn: true }, { reviewState: 'unreviewed' }, { inputFingerprint: 'b'.repeat(64) }, { cycleId: quote }, { conditionId: undefined }]) assert.equal(proofMatches({ ...evidence, ...change }, requirement, cycle), false);
});
