import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const api=JSON.parse(readFileSync(new URL('../contracts/openapi.json',import.meta.url)));

test('bank reconciliation commands require current finance authority, CSRF and replay keys',()=>{
 const routes=[
  '/finance/agencies/{agencyId}/bank-lines',
  '/finance/agencies/{agencyId}/reconciliations',
  '/finance/reconciliations/{reconciliationId}/matches',
  '/finance/reconciliation-matches/{matchId}/reversals',
  '/finance/reconciliations/{reconciliationId}/exclusions',
  '/finance/reconciliations/{reconciliationId}/variances',
  '/finance/reconciliations/{reconciliationId}/target-variances',
  '/finance/reconciliations/{reconciliationId}/complete',
 ];
 for(const path of routes){
  const operation=api.paths[path].post;
  assert.match(operation['x-permission'],/finance-reconcile.*current stored finance agency scope/);
  assert.deepEqual(operation.security,[{Session:[],Csrf:[]}]);
  assert.equal(operation.parameters.find(x=>x.name==='Idempotency-Key').required,true);
  assert.equal(operation['x-runtime-status'],'phase-10-06-implemented');
  assert.equal(operation.responses['201'].headers['Cache-Control'].schema.const,'no-store');
  assert.equal(operation.requestBody.content['application/json'].schema.additionalProperties,false);
 }
 for(const path of ['/finance/agencies/{agencyId}/bank-lines',
  '/finance/bank-lines/{bankLineId}','/finance/reconciliations/{reconciliationId}']){
  const operation=api.paths[path].get;
  assert.match(operation['x-permission'],/finance-read.*current stored finance agency scope/);
  assert.deepEqual(operation.security,[{Session:[]}]);
  assert.equal(operation.responses['200'].headers['Cache-Control'].schema.const,'no-store');
 }
});

test('bank reconciliation contracts retain exact saved IDs, residual and variance evidence',()=>{
 const schemas=api.components.schemas;
 const source=api.paths['/finance/reconciliations/{reconciliationId}/matches'].post.requestBody.content['application/json'].schema;
 assert.ok(source.required.includes('financePostingId'));
 assert.ok(source.required.includes('bankLineId'));
 assert.equal(source.properties.signedAmount.type,'string');
 assert.equal(schemas.FinanceBankLine.properties.currency.const,'GBP');
 for(const field of ['importKey','rawJson','duplicateCandidateIds'])
  assert.ok(schemas.FinanceBankLine.required.includes(field));
 for(const field of ['netVariance','absoluteVariance','unaddressedCount','lines','targets','matches'])
  assert.ok(schemas.FinanceReconciliation.required.includes(field));
 for(const field of ['financePostingId','postingDate','residual','explanation','addressed'])
  assert.ok(schemas.FinanceReconciliationTarget.required.includes(field));
 assert.ok(schemas.FinanceReconciliationMatch.required.includes('reversalOfId'));
 assert.equal(api.paths['/finance/reconciliations/{reconciliationId}/complete'].post.responses['201']
  .content['application/json'].schema.$ref,'#/components/schemas/FinanceReconciliationCompleteResult');
 const types=readFileSync(new URL('../contracts/generated/finance-reconciliation.ts',import.meta.url),'utf8');
 assert.match(types,/netVariance:string; absoluteVariance:string; unaddressedCount:number/);
 assert.match(types,/reversalOfId:string\|null/);
 assert.match(types,/actualVariance:string; absoluteVariance:string/);
});
