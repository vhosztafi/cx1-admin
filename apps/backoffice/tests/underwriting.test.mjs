import test from 'node:test';
import assert from 'node:assert/strict';
import { underwritingCommand, sendUnderwritingCommand, quoteStateLabel, formatGbp } from '../lib/underwriting-api.ts';
import { uncertainQuoteFailure, staleQuoteFailure } from '../lib/quotes.ts';

const quoteId = 'aaaaaaaa-0000-4000-8000-000000000001';
const revisionId = 'aaaaaaaa-0000-4000-8000-000000000002';
const cycleId = 'aaaaaaaa-0000-4000-8000-000000000003';
const etag = '"AAAAAAAAAAE="';

test('each persisted state is explicit and expiry does not turn a bound policy into an expired quote', () => {
  const labels = { draft: 'Draft', 'rating-pending': 'Rating requested', rated: 'Rated', referred: 'Referred', approved: 'Approved', sent: 'Sent', accepted: 'Accepted', declined: 'Declined', bound: 'Policy issued', withdrawn: 'Withdrawn' };
  for (const [state, label] of Object.entries(labels)) assert.equal(quoteStateLabel(state), label);
  assert.equal(quoteStateLabel('rated', '2026-09-01T00:00:00Z', Date.parse('2026-09-16T00:00:00Z')), 'Rating expired');
  assert.equal(quoteStateLabel('bound', '2026-09-01T00:00:00Z', Date.parse('2026-09-16T00:00:00Z')), 'Policy issued');
  assert.equal(quoteStateLabel('unexpected'), 'Status unavailable');
});

test('money display preserves decimal strings without computing new financial totals', () => {
  assert.equal(formatGbp('1234567890123.45'), '£1,234,567,890,123.45');
  assert.equal(formatGbp('0.00'), '£0.00');
  assert.equal(formatGbp('600.50'), '£600.50');
  assert.equal(formatGbp('invalid'), 'Unavailable');
});

test('rating and cycle commands freeze the exact body, key and owning quote version', () => {
  const body = { revisionId, reason: 'Initial rating' };
  const command = underwritingCommand(quoteId, 'rate', etag, body, 'rating-command-test-001');
  body.reason = 'Changed after request';
  assert.equal(JSON.parse(command.body).reason, 'Initial rating');
  assert.equal(command.etag, etag);
  assert.throws(() => { command.key = 'changed'; }, TypeError);
  assert.throws(() => underwritingCommand(quoteId, 'rate', etag, { cycleId, reason: 'Wrong identity' }));
  assert.throws(() => underwritingCommand(quoteId, 'submit', etag, { cycleId, reason: 'OK', manualPremium: '1.00' }));
  assert.throws(() => underwritingCommand(quoteId, 'rate', 'W/"old"', { revisionId, reason: 'Rating' }));
});

test('lost response retries the identical underwriting request and checks receipt ownership', async () => {
  const command = underwritingCommand(quoteId, 'rate', etag, { revisionId, reason: 'Rate this revision' }, 'rating-command-test-002');
  const original = globalThis.fetch, seen = [];
  globalThis.fetch = async (url, init) => {
    seen.push({ url, body: init.body, key: init.headers['Idempotency-Key'], etag: init.headers['If-Match'] });
    if (seen.length === 1) throw new TypeError('Response lost');
    return new Response(JSON.stringify({ id: cycleId, quoteId, quoteEtag: etag, jobId: revisionId, state: 'queued' }), { status: 202, headers: { ETag: etag } });
  };
  try {
    await assert.rejects(sendUnderwritingCommand(command, 'csrf'), uncertainQuoteFailure);
    assert.equal((await sendUnderwritingCommand(command, 'renewed')).jobId, revisionId);
    assert.deepEqual(seen[0], seen[1]);
    globalThis.fetch = async () => new Response(JSON.stringify({ id: cycleId, quoteId: revisionId, quoteEtag: etag }), { headers: { ETag: etag } });
    await assert.rejects(sendUnderwritingCommand(command, 'csrf'), uncertainQuoteFailure);
    globalThis.fetch = async () => new Response('private diagnostic', { status: 412 });
    await assert.rejects(sendUnderwritingCommand(command, 'csrf'), staleQuoteFailure);
    assert.equal(command.etag, etag);
  } finally { globalThis.fetch = original; }
});
