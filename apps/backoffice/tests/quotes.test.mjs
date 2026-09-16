import test from 'node:test';
import assert from 'node:assert/strict';
import { canCaptureQuotes, createQuoteCommand, saveQuoteCommand, sendQuoteCommand, quoteFetch, QuoteError, uncertainQuoteFailure, staleQuoteFailure, validQuoteEtag } from '../lib/quotes.ts';

const id = 'aaaaaaaa-0000-4000-8000-000000000001';
const product = 'aaaaaaaa-0000-4000-8000-000000000002';
const etag = '"AAAAAAAAAAE="';
const proposal = () => ({ schemaVersion: '1.0', productCode: 'motor-trade-road-risks', insured: { proposerNames: ['Alex Example'] },
  termIntent: { kind: 'annual', timeZone: 'Europe/London', localStartDate: '2026-10-25', localStartTime: '01:30', utcOffsetMinutes: 60 } });

test('quote capture hints match internal roles without widening agency or administration access', () => {
  for (const role of ['servicing', 'underwriter', 'senior-underwriter']) assert.equal(canCaptureQuotes([role]), true);
  for (const role of ['agency-admin', 'system-admin', 'finance', 'broker-admin']) assert.equal(canCaptureQuotes([role]), false);
  assert.equal(canCaptureQuotes([]), false);
});

test('create and save capture immutable exact bodies while preserving omitted and explicit answers', () => {
  const draft = proposal(); draft.risk = { business: { turnover: '0.00', responses: { questionSetVersion: 'example', answers: [{ questionId: 'example', kind: 'boolean', value: false }] } } };
  const create = createQuoteCommand(id, product, draft, 'quote-create-test-001');
  const save = saveQuoteCommand(id, etag, draft, ' Exact reason ', 'quote-save-test-0001');
  const initialCreate = create.body, initialSave = save.body;
  draft.insured.proposerNames[0] = 'Later unsaved edit'; draft.risk.business.turnover = '10.00';
  assert.equal(create.body, initialCreate); assert.equal(save.body, initialSave);
  assert.equal(JSON.parse(save.body).reason, ' Exact reason ');
  assert.equal(JSON.parse(save.body).proposal.risk.business.responses.answers[0].value, false);
  assert.equal(JSON.parse(save.body).proposal.termIntent.utcOffsetMinutes, 60);
  assert.equal(JSON.parse(create.body).proposal.termIntent.localEndDate, undefined);
  assert.equal(create.etag, undefined); assert.equal(save.etag, etag);
  assert.throws(() => { save.key = 'replacement'; }, TypeError);
});

test('lost response retries identical URL method key body and ETag despite later form edits', async () => {
  const draft = proposal(), command = saveQuoteCommand(id, etag, draft, undefined, 'quote-retry-test-001');
  const previous = globalThis.fetch, seen = [];
  globalThis.fetch = async (url, init) => {
    seen.push({ url, method: init.method, body: init.body, key: init.headers['Idempotency-Key'], etag: init.headers['If-Match'], cache: init.cache });
    if (seen.length === 1) throw new TypeError('Response lost after commit');
    return new Response(JSON.stringify({ id }), { status: 200, headers: { ETag: '"AAAAAAAAAAI="' } });
  };
  try {
    await assert.rejects(sendQuoteCommand(command, 'csrf-first'), uncertainQuoteFailure);
    draft.insured.proposerNames.push('New form value');
    assert.deepEqual(await sendQuoteCommand(command, 'csrf-renewed'), { id, etag: '"AAAAAAAAAAI="' });
    assert.deepEqual(seen[0], seen[1]); assert.equal(seen[1].cache, 'no-store');
    assert.deepEqual(JSON.parse(seen[1].body).proposal.insured.proposerNames, ['Alex Example']);
  } finally { globalThis.fetch = previous; }
});

test('stale and denied responses preserve the original command and expose safe feedback', async () => {
  const command = saveQuoteCommand(id, etag, proposal(), undefined, 'quote-stale-test-001');
  const previous = globalThis.fetch;
  try {
    for (const status of [401, 403, 409, 412, 428, 422, 503]) {
      globalThis.fetch = async () => new Response('{"message":"private diagnostic","errors":["secret"]}', { status });
      await assert.rejects(sendQuoteCommand(command, 'csrf'), error => {
        assert.equal(error.status, status); assert.doesNotMatch(error.message, /private|diagnostic|secret/);
        assert.equal(staleQuoteFailure(error), [409, 412, 428].includes(status));
        assert.equal(uncertainQuoteFailure(error), status === 503); return true;
      });
      assert.equal(command.etag, etag); assert.equal(command.key, 'quote-stale-test-001');
    }
  } finally { globalThis.fetch = previous; }
});

test('malformed success remains uncertain and never invents a saved identity or version', async () => {
  const command = saveQuoteCommand(id, etag, proposal(), undefined, 'quote-receipt-test01');
  const previous = globalThis.fetch;
  try {
    for (const [body, version] of [[{ id }, null], [{ id }, 'W/' + etag], [{ id: product }, etag], [{}, etag], [null, etag]]) {
      globalThis.fetch = async () => new Response(JSON.stringify(body), { status: 200, headers: version ? { ETag: version } : {} });
      await assert.rejects(sendQuoteCommand(command, 'csrf'), error => uncertainQuoteFailure(error) && /could not be confirmed/.test(error.message));
    }
    globalThis.fetch = async () => new Response('<html>proxy failure</html>', { status: 200 });
    await assert.rejects(sendQuoteCommand(command, 'csrf'), uncertainQuoteFailure);
  } finally { globalThis.fetch = previous; }
});

test('command builders refuse absent identifiers weak versions and invalid recovery identities', () => {
  for (const value of ['', '00000000-0000-0000-0000-000000000000', ' ' + id]) assert.throws(() => createQuoteCommand(value, product, proposal()));
  for (const value of ['', 'W/' + etag, '"1"', '"AAAAAAAAAAB="', '*']) {
    assert.equal(validQuoteEtag(value), false); assert.throws(() => saveQuoteCommand(id, value, proposal()));
  }
  assert.equal(validQuoteEtag(etag), true);
  for (const key of ['short', ' '.repeat(16), 'a'.repeat(201), 'valid-size-key\n000', 'valid-size-key\u0085000']) assert.throws(() => createQuoteCommand(id, product, proposal(), key));
  for (const reason of ['', ' ', 'a'.repeat(1001)]) assert.throws(() => saveQuoteCommand(id, etag, proposal(), reason));
  assert.equal(uncertainQuoteFailure(new QuoteError(429)), true);
  assert.equal(uncertainQuoteFailure(new QuoteError(408)), true);
});

test('read requests retain no-store and expose their actual saved ETag', async () => {
  const previous = globalThis.fetch;
  globalThis.fetch = async (url, init) => { assert.equal(init.cache, 'no-store'); return new Response('{"items":[]}', { headers: { ETag: etag } }); };
  try { assert.deepEqual(await quoteFetch('/api/v1/quote-products'), { data: { items: [] }, etag }); }
  finally { globalThis.fetch = previous; }
});

test('multipart evidence retries preserve immutable file bytes metadata key and version', async () => {
  const previous = globalThis.fetch, seen = [];
  const upload = new File(['Fictional proof\r\n'], 'proof.txt', { type: 'text/plain' });
  const command = Object.freeze({ method: 'POST', url: `/api/v1/quotes/${id}/evidence-files`, body: '{}', key: 'evidence-retry-0001', etag, upload });
  try {
    globalThis.fetch = async (url, init) => {
      assert.ok(init.body instanceof FormData); assert.equal(init.headers['Content-Type'], undefined);
      seen.push({ url, key: init.headers['Idempotency-Key'], etag: init.headers['If-Match'],
        bytes: await init.body.get('file').text(), name: init.body.get('fileName'), type: init.body.get('contentType') });
      if (seen.length === 1) throw new TypeError('Lost response');
      return new Response(JSON.stringify({ id: product }), { status: 201, headers: { ETag: etag } });
    };
    await assert.rejects(sendQuoteCommand(command, 'csrf'), uncertainQuoteFailure);
    assert.equal((await sendQuoteCommand(command, 'fresh-csrf')).id, product);
    assert.deepEqual(seen[0], seen[1]); assert.equal(seen[1].bytes, 'Fictional proof\r\n');
  } finally { globalThis.fetch = previous; }
});
