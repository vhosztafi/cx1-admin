import test from 'node:test';
import assert from 'node:assert/strict';
import { addQuoteActivity, changeQuoteActivity, moveQuoteActivity, quoteActivities, removeQuoteActivity } from '../lib/quote-form.ts';

const first = '51000000-0000-4000-8000-000000000041';
const second = '51000000-0000-4000-8000-000000000042';
const base = () => ({ schemaVersion: '1.0', productCode: 'motor-trade-road-risks', insured: { proposerNames: ['Alex Example'] }, risk: { business: { description: 'Fictional repairs' } } });

test('occupation edits after reordering follow immutable row identity and preserve other proposal sections', () => {
  const original = addQuoteActivity(addQuoteActivity(base(), first), second);
  const moved = moveQuoteActivity(original, second, -1);
  const edited = changeQuoteActivity(moved, first, 'turnoverBasisPoints', 3333);
  assert.deepEqual(quoteActivities(edited).map(row => row.id), [second, first]);
  assert.equal(quoteActivities(edited)[1].turnoverBasisPoints, 3333); assert.equal(quoteActivities(original)[0].turnoverBasisPoints, undefined);
  assert.equal(edited.risk.business.description, 'Fictional repairs'); assert.deepEqual(edited.insured.proposerNames, ['Alex Example']);
  const removed = removeQuoteActivity(edited, second); assert.equal(quoteActivities(removed)[0].id, first);
  assert.equal(quoteActivities(edited).length, 2); assert.equal(quoteActivities(original).length, 2);
});

test('blank rows remain saveable and selected reference and explicit zero are copied without caller mutation', () => {
  const original = addQuoteActivity(base(), first); assert.deepEqual(quoteActivities(original), [{ id: first }]);
  const reference = { collection: 'mtOccupations', version: 'pinned', value: 12, label: 'Fictional selection' };
  let edited = changeQuoteActivity(original, first, 'code', reference); reference.label = 'Changed elsewhere';
  assert.equal(quoteActivities(edited)[0].code.label, 'Fictional selection');
  edited = changeQuoteActivity(edited, first, 'turnoverBasisPoints', 0); assert.equal(quoteActivities(edited)[0].turnoverBasisPoints, 0);
  edited = changeQuoteActivity(edited, first, 'code', undefined); assert.equal(quoteActivities(edited)[0].code, undefined);
  assert.equal(quoteActivities(edited)[0].id, first);
});

test('stale and duplicate row identities fail instead of updating another indexed row', () => {
  const original = addQuoteActivity(base(), first);
  const letterId = '51000000-0000-4000-8000-000000abcdef';
  assert.throws(() => addQuoteActivity(addQuoteActivity(base(), letterId), letterId.toUpperCase()));
  assert.throws(() => addQuoteActivity(original, first)); assert.throws(() => addQuoteActivity(original, 'bad-id'));
  assert.throws(() => changeQuoteActivity(original, second, 'turnoverBasisPoints', 100));
  assert.throws(() => removeQuoteActivity(original, second)); assert.throws(() => changeQuoteActivity(original, first, 'id', second));
  assert.throws(() => quoteActivities({ ...base(), risk: { business: { activities: [{ id: first }, { id: first }] } } }));
  assert.equal(moveQuoteActivity(original, first, -1), original);
  assert.throws(() => moveQuoteActivity(original, first, 2));
});
