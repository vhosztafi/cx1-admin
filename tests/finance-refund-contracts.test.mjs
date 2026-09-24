import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const api=JSON.parse(readFileSync(new URL('../contracts/openapi.json',import.meta.url)));

test('refund commands require current scope, replay keys and independent versioned decision',()=>{
 const request=api.paths['/finance/credits/{creditObligationId}/refunds'].post;
 const decide=api.paths['/finance/refunds/{refundId}/decisions'].post;
 for(const operation of [request,decide]){
  assert.deepEqual(operation.security,[{Session:[],Csrf:[]}]);
  assert.equal(operation.parameters.find(x=>x.name==='Idempotency-Key').required,true);
  assert.equal(operation.responses['201'].headers['Cache-Control'].schema.const,'no-store');
  assert.equal(operation['x-runtime-status'],'phase-10-07-implemented');
  assert.equal(operation.requestBody.content['application/json'].schema.additionalProperties,false);
 }
 assert.match(request['x-permission'],/finance-refund-request/);
 assert.match(decide['x-permission'],/finance-refund-approve/);
 assert.equal(decide.parameters.find(x=>x.name==='If-Match').required,true);
 const source=request.requestBody.content['application/json'].schema.properties.sources;
 assert.equal(source.maxItems,100);
 assert.ok(source.items.required.includes('allocationId'));
 assert.equal(request.responses['201'].content['application/json'].schema.$ref,
  '#/components/schemas/FinanceRefundRequestResult');
});

test('refund detail exposes pinned credit, cash provenance, rule and audit decisions',()=>{
 const detail=api.paths['/finance/refunds/{refundId}'].get;
 assert.match(detail['x-permission'],/finance-read/);
 assert.deepEqual(detail.security,[{Session:[]}]);
 assert.equal(detail.responses['200'].headers['Cache-Control'].schema.const,'no-store');
 assert.ok(detail.responses['200'].headers.ETag);
 const refund=api.components.schemas.FinanceRefund;
 for(const field of ['creditObligationId','debtorId','sources','decisions','ruleId','ruleVersion',
  'requiredApprovals','state','etag'])assert.ok(refund.required.includes(field));
 const types=readFileSync(new URL('../contracts/generated/finance-refunds.ts',import.meta.url),'utf8');
 assert.match(types,/sources:FinanceRefundSource\[\]/);
 assert.match(types,/requiredApprovals:1\|2/);
});
