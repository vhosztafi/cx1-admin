import test from 'node:test';
import assert from 'node:assert/strict';
import { quotationCommand, acceptanceInstant, acceptanceProofs } from '../lib/quotation.ts';
import { sendUnderwritingCommand } from '../lib/underwriting-api.ts';
import { sendCapacityRecovery } from '../lib/capacity.ts';

const id = n => `aaaaaaaa-0000-4000-8000-${String(n).padStart(12, '0')}`;
const etag = '"AAAAAAAAAAE="';
const context = { cycleId: id(2), ratingId: id(3), termsVersionId: id(4), termsHash: 'a'.repeat(64), assuranceHash: 'b'.repeat(64) };
test('prepared and sent quotation commands retain exact reviewed identities', () => {
  const body = { termsVersionId: id(4), recipientContactIds: [id(5)] };
  const command = quotationCommand(id(1), 'terms', etag, body);
  body.recipientContactIds.push(id(6));
  assert.deepEqual(JSON.parse(command.body), { termsVersionId: id(4), recipientContactIds: [id(5)] });
  assert.ok(Object.isFrozen(command));
  assert.throws(() => quotationCommand(id(1), 'terms', etag, { ...body, recipientContactIds: [id(5), id(5)] }));
  assert.throws(() => quotationCommand(id(1), 'terms', etag, { ...body, email: 'arbitrary@example.test' }));
  assert.throws(() => quotationCommand(id(1), 'terms/prepare', etag, { cycleId: id(2), ratingId: id(3) }));
  assert.throws(() => quotationCommand(id(1), 'terms', 'W/"old"', body));
});
test('acceptance needs closed channel, exact hashes, named accepter and actual proof identity', () => {
  const body = { ...context, accepterLabel: 'Alex Example', acceptedAt: '2026-09-17T10:30:00Z', channel: 'written', evidenceAssociationId: id(7) };
  assert.deepEqual(JSON.parse(quotationCommand(id(1), 'acceptances', etag, body).body), body);
  for (const change of [{ channel: 'checkbox' }, { accepterLabel: ' ' }, { assuranceHash: 'old' }, { evidenceAssociationId: 'invented' }, { acceptedAt: '2026-09-17T10:30:00' }, { extra: true }])
    assert.throws(() => quotationCommand(id(1), 'acceptances', etag, { ...body, ...change }));
});
test('acceptance timestamps require an explicit offset and reject impossible dates', () => {
  assert.equal(acceptanceInstant('2026-09-17T10:30:00+01:00'), '2026-09-17T09:30:00.000Z');
  for (const value of ['', '2026-09-17T10:30', '2026-02-30T10:00:00Z', '2026-09-17T25:00:00Z']) assert.throws(() => acceptanceInstant(value));
});
test('acceptance selector only offers reviewed current exact-version evidence', () => {
  const purpose = { code: 'acceptance-proof', termsVersionId: id(4), inputFingerprint: 'c'.repeat(64) };
  const evidence = { id: id(7), cycleId: id(2), requirementCode: purpose.code, termsVersionId: id(4), inputFingerprint: purpose.inputFingerprint, reviewState: 'accepted', screeningState: 'accepted', withdrawn: false };
  const assessment = { context: { cycleId: id(2) }, termsVersionId: id(4), proofRequirements: [purpose] };
  const rows = [evidence, ...[{ withdrawn: true }, { reviewState: 'pending' }, { termsVersionId: id(8) }, { inputFingerprint: 'd'.repeat(64) }, { cycleId: id(9) }].map((change, i) => ({ ...evidence, id: id(i + 20), ...change }))];
  assert.deepEqual(acceptanceProofs(rows, assessment).map(x => x.id), [id(7)]);
});
test('send receipt cannot turn missing job identity or a nonqueued state into confirmed delivery', async () => {
  const command = quotationCommand(id(1), 'terms', etag, { termsVersionId: id(4), recipientContactIds: [id(5)] });
  const original = globalThis.fetch;
  try {
    for (const fields of [{}, { jobId: id(9), state: 'delivered' }]) {
      globalThis.fetch = async () => new Response(JSON.stringify({ id: id(8), quoteId: id(1), quoteEtag: etag, ...fields }), { status: 202, headers: { ETag: etag } });
      await assert.rejects(sendUnderwritingCommand(command, 'csrf'));
    }
    globalThis.fetch = async () => new Response(JSON.stringify({ id: id(8), quoteId: id(1), quoteEtag: etag, jobId: id(9), state: 'queued' }), { status: 202, headers: { ETag: etag } });
    assert.equal((await sendUnderwritingCommand(command, 'csrf')).state, 'queued');
  } finally { globalThis.fetch = original; }
});
test('delivery recovery requires the original job and exact delivery kind', async () => {
  const original = globalThis.fetch, command = { url: `/api/v1/jobs/${id(9)}/retry`, method: 'POST', etag, key: 'retained-job-recovery', body: JSON.stringify({ reason: 'Provider recovered' }) };
  try {
    globalThis.fetch = async () => new Response(JSON.stringify({ id: id(9), kind: 'capacity-escalation', state: 'pending' }), { headers: { ETag: etag } });
    await assert.rejects(sendCapacityRecovery(command, id(9), 'csrf', 'quote-delivery'));
    globalThis.fetch = async () => new Response(JSON.stringify({ id: id(9), kind: 'quote-delivery', state: 'pending' }), { headers: { ETag: etag } });
    await sendCapacityRecovery(command, id(9), 'csrf', 'quote-delivery');
    await assert.rejects(sendCapacityRecovery(command, id(8), 'csrf', 'quote-delivery'));
  } finally { globalThis.fetch = original; }
});
