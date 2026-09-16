import test from 'node:test';
import assert from 'node:assert/strict';
import { changeRequestedCover, requestedCoverRows } from '../lib/requested-cover.ts';
const id = 'aaaaaaaa-0000-4000-8000-000000000001', premise = 'aaaaaaaa-0000-4000-8000-000000000002';
const base = () => ({ schemaVersion: '1.0', productCode: 'motor-trade-combined', risk: { premises: [{id: premise}] }, cover: {} });
test('cover choices are explicit, preserve identity and never invent a missing amount', () => {
  const original = base(); assert.deepEqual(requestedCoverRows(original), []);
  assert.throws(() => changeRequestedCover(original, 'stock-custody', id, true, {limit: '10000', excess: '0'}, []));
  const selected = changeRequestedCover(original, 'stock-custody', id, true, {limit: '10000', excess: '0', anyOneVehicleLimit: '5000'}, []);
  assert.deepEqual(original.cover, {});
  assert.equal(selected.cover.requestedSections[0].limit, '10000.00');
  const removed = changeRequestedCover(selected, 'stock-custody', premise, false, {}, []);
  assert.deepEqual(removed.cover.requestedSections[0], {id, code: 'stock-custody', selected: false});
  assert.equal(selected.cover.requestedSections[0].selected, true);
});
test('premises must target actual current risks and product-specific cover cannot leak into road risks', () => {
  assert.throws(() => changeRequestedCover(base(), 'premises', id, true, {limit:'1000',excess:'0'}, [id]));
  const result = changeRequestedCover(base(), 'premises', id, true, {limit:'1000',excess:'0'}, [premise]);
  assert.deepEqual(result.cover.requestedSections[0].premisesIds, [premise]);
  assert.throws(() => changeRequestedCover({...base(), productCode:'motor-trade-road-risks'}, 'stock-custody', id, false, {}, []));
  assert.throws(() => changeRequestedCover(base(), 'tools-equipment', id, true, {limit:'0',excess:'0'}, []));
});
