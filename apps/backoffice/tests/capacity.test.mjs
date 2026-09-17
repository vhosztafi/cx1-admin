import test from 'node:test';
import assert from 'node:assert/strict';
import { capacityExtension, capacityInstant, sendCapacityRecovery } from '../lib/capacity.ts';
test('capacity forms retain exact typed monetary, age and trade extent', () => {
  assert.deepEqual(capacityExtension('cover-stock-custody', 'cover-restriction', { maximumAmount: '150000.00' }), { dimension: 'stock-limit', maximumAmount: '150000.00' });
  for (const amount of ['0.00','150000.001','1e6','-1.00','150000']) assert.throws(() => capacityExtension('stock-limit', 'stock-limit', { maximumAmount: amount }));
  assert.deepEqual(capacityExtension('salvage-breaking', 'trade-restriction', {}), { dimension: 'trade-restriction', questionId: 'salvage-breaking', permitted: true });
  assert.throws(() => capacityExtension('UW-22', 'trading-history', {}));
  assert.throws(() => capacityExtension('driver-age', 'driver-age', { minimumAge: '40', maximumAge: '25' }));
  assert.deepEqual(capacityExtension('driver-age', 'driver-age', { minimumAge: '21', maximumAge: '70' }), { dimension: 'driver-age', minimumAge: 21, maximumAge: 70 });
});
test('capacity response dates require actual instants and retain timezone meaning', () => {
  assert.equal(capacityInstant('2026-09-17T10:00:00+01:00'), '2026-09-17T09:00:00.000Z');
  assert.throws(() => capacityInstant('')); assert.throws(() => capacityInstant('not a date'));
});
test('capacity recovery keeps exact retry identity and rejects a foreign job receipt', async t => {
  const id = 'aaaaaaaa-0000-4000-8000-000000000001', etag = '"AAAAAAAAAAE="';
  const command = Object.freeze({ method: 'POST', url: `/api/v1/jobs/${id}/retry`, body: '{"reason":"Retain original submission"}', key: 'capacity-retry-exact-key', etag });
  const calls = []; let foreign = true;
  t.mock.method(globalThis, 'fetch', async (url, options) => {
    calls.push({ url, body: options.body, headers: options.headers });
    return new Response(JSON.stringify({ id: foreign ? 'bbbbbbbb-0000-4000-8000-000000000001' : id, kind: 'capacity-escalation', state: 'pending' }), { headers: { ETag: etag } });
  });
  await assert.rejects(sendCapacityRecovery(command, id, 'csrf'), /could not be confirmed/);
  foreign = false; await sendCapacityRecovery(command, id, 'csrf');
  assert.deepEqual(calls[0], calls[1]);
  await assert.rejects(sendCapacityRecovery(command, 'other', 'csrf'), /original capacity job/);
  assert.equal(calls.length, 2);
});
