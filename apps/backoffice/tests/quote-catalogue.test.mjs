import test from 'node:test';
import assert from 'node:assert/strict';
import { matchingQuoteCatalogue } from '../lib/quote-catalogue.ts';

test('reference editing requires all owned revision pins and never falls back to current product availability', () => {
  const catalogue = { version: 'retained-version' };
  const pins = { schemaVersion: '1.0', questionSetVersion: catalogue.version, referenceDataVersion: catalogue.version };
  assert.equal(matchingQuoteCatalogue(pins, catalogue), true);
  assert.equal(matchingQuoteCatalogue(undefined, catalogue), false);
  for (const field of Object.keys(pins)) {
    const omitted = { ...pins }; delete omitted[field]; assert.equal(matchingQuoteCatalogue(omitted, catalogue), false);
    assert.equal(matchingQuoteCatalogue({ ...pins, [field]: 'new-version' }, catalogue), false);
    assert.equal(matchingQuoteCatalogue({ ...pins, [field]: '' }, catalogue), false);
  }
  assert.equal(matchingQuoteCatalogue(pins, { version: 'current-product-version' }), false);
});
