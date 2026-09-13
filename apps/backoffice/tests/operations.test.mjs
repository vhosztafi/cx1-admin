import test from 'node:test';
import assert from 'node:assert/strict';
import { operationalFetch, operationError } from '../lib/operations.ts';

test('an API failure never displays an arbitrary server response body', async () => {
  const original = globalThis.fetch;
  globalThis.fetch = async () => new Response('{"detail":"server-password-secret"}', {status:503});
  try {
    await assert.rejects(() => operationalFetch('/api/v1/admin/jobs'), error => {
      assert.doesNotMatch(error.message, /server-password-secret/);
      assert.match(error.message, /could not confirm/);
      return true;
    });
  } finally {globalThis.fetch = original;}
});
test('stale selections and ended sessions offer different recovery actions', () => {
  assert.match(operationError(412), /select again/);
  assert.match(operationError(401), /Sign in again/);
  assert.match(operationError(409), /cannot be applied/);
});
