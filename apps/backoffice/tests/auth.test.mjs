import test from 'node:test';
import assert from 'node:assert/strict';
import { authError, validateSignIn } from '../lib/auth.ts';

test('sign-in reports the first missing or invalid field', () => {
  assert.equal(validateSignIn('', ''), 'Enter your email address.');
  assert.equal(validateSignIn('not-an-email', 'secret'), 'Enter a valid email address.');
  assert.equal(validateSignIn('staff@cover.example', ''), 'Enter your password.');
  assert.equal(validateSignIn('staff@cover.example', 'x'.repeat(1025)), 'Your password is too long.');
});
test('email whitespace is allowed without modifying password contents', () => {
  assert.equal(validateSignIn(' staff@cover.example ', ' password with spaces '), null);
});
test('credential rejection does not disclose whether the account exists', () => {
  assert.match(authError(401), /Check your details/);
  assert.doesNotMatch(authError(401), /does not exist/);
});
test('security and service failures have useful distinct recovery messages', () => {
  assert.match(authError(403), /security token/);
  assert.match(authError(429), /wait a minute/);
  assert.match(authError(503), /could not reach/);
});
