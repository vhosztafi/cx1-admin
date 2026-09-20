import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';

test('commercial issued policies and agency projections use the supported product catalogue', async () => {
 const api=JSON.parse(await readFile('contracts/openapi.json','utf8'));
 for(const name of ['PolicySummary','AgencySharedPolicy','AgencySharedQuote']) {
  assert.ok(api.components.schemas[name].properties.productCode.enum.includes('commercial-combined'), name);
 }
 const filter=api.paths['/policies'].get.parameters.find(x=>x.name==='productCode');
 assert.ok(filter.schema.enum.includes('commercial-combined'));
 assert.ok(!filter.schema.enum.includes('unsupported-product'));
});
