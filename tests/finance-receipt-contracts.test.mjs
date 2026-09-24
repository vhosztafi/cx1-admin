import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const api=JSON.parse(readFileSync(new URL('../contracts/openapi.json',import.meta.url)));

test('cash receipt writes are scoped, replayable CSRF commands with explicit payer version',()=>{
 const record=api.paths['/finance/agencies/{agencyId}/receipts'].post;
 const assign=api.paths['/finance/receipts/{receiptId}/payer'].post;
 const allocate=api.paths['/finance/receipts/{receiptId}/allocations'].post;
 const reverse=api.paths['/finance/allocations/{allocationId}/reversals'].post;
 for(const operation of [record,assign,allocate,reverse]) {
  assert.match(operation['x-permission'],/finance-cash-write/);
  assert.deepEqual(operation.security,[{Session:[],Csrf:[]}]);
  assert.equal(operation.parameters.find(x=>x.name==='Idempotency-Key').required,true);
  assert.equal(operation['x-runtime-status'],'phase-10-04-implemented');
  assert.equal(operation.responses['201'].headers['Cache-Control'].schema.const,'no-store');
 }
 for(const operation of [assign,allocate])
  assert.equal(operation.parameters.find(x=>x.name==='If-Match').required,true);
 assert.equal(record.requestBody.content['application/json'].schema.additionalProperties,false);
 assert.equal(allocate.requestBody.content['application/json'].schema.properties.items.maxItems,100);
 assert.equal(reverse.responses['201'].content['application/json'].schema.$ref,'#/components/schemas/FinanceReceiptReversal');
});

test('receipt reads expose saved cash, residual, payer and append-only reversal history',()=>{
 const list=api.paths['/finance/agencies/{agencyId}/receipts'].get;
 const detail=api.paths['/finance/receipts/{receiptId}'].get;
 assert.match(list['x-permission'],/finance-read/);
 assert.match(detail['x-permission'],/finance-read/);
 assert.equal(list.responses['200'].content['application/json'].schema.$ref,'#/components/schemas/FinanceReceiptPage');
 assert.equal(detail.responses['200'].content['application/json'].schema.$ref,'#/components/schemas/FinanceReceipt');
 assert.equal(detail.responses['200'].headers['Cache-Control'].schema.const,'no-store');
 for(const field of ['amount','residual','assignmentId','payerKind','accountingPeriodId','allocations'])
  assert.ok(api.components.schemas.FinanceReceipt.required.includes(field));
 assert.ok(api.components.schemas.FinanceReceiptAllocation.required.includes('reversalOfId'));
 const types=readFileSync(new URL('../contracts/generated/finance-receipts.ts',import.meta.url),'utf8');
 assert.match(types,/residual: string/);
 assert.match(types,/reversalOfId: string \| null/);
});
