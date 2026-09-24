import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const api=JSON.parse(readFileSync(new URL('../contracts/openapi.json',import.meta.url)));

test('refund payment queue is scoped, versioned, idempotent and explicitly reviewed',()=>{
 const post=api.paths['/finance/refunds/{id}/payments'].post;
 assert.match(post['x-permission'],/finance-payment-execute and current internal finance identity/);
 assert.deepEqual(post.security,[{Session:[],Csrf:[]}]);
 assert.equal(post.parameters.find(x=>x.name==='If-Match').required,true);
 assert.equal(post.parameters.find(x=>x.name==='Idempotency-Key').required,true);
 assert.deepEqual(post.requestBody.content['application/json'].schema.required,['reason']);
 assert.ok(post.requestBody.content['application/json'].schema.properties.priorPaymentId);
 assert.equal(post['x-runtime-status'],'phase-10-08-implemented');
});

test('saved payment detail distinguishes provider acknowledgement from local paid posting',()=>{
 const get=api.paths['/finance/payments/{id}'].get;
 assert.deepEqual(get.security,[{Session:[]}]);
 const schema=api.components.schemas.FinancePayment;
 for(const field of ['creditObligationId','debtorId','providerState','providerOperationId',
  'appliedAt','priorPaymentId','workId'])assert.ok(schema.required.includes(field));
 assert.ok(schema.properties.state.enum.includes('provider-acknowledged/application-pending'));
 const types=readFileSync(new URL('../contracts/generated/finance-payments.ts',import.meta.url),'utf8');
 assert.match(types,/providerState: 'accepted' \| 'rejected' \| null/);
 assert.match(types,/priorPaymentId: string \| null/);
});

test('failed no-result payment resume is scoped, versioned and audited with reason',()=>{
 const post=api.paths['/finance/payments/{id}/resume'].post;
 assert.match(post['x-permission'],/finance-payment-execute and current internal finance identity/);
 assert.deepEqual(post.security,[{Session:[],Csrf:[]}]);
 assert.equal(post.parameters.find(x=>x.name==='If-Match').required,true);
 assert.equal(post.parameters.find(x=>x.name==='Idempotency-Key').required,true);
 assert.deepEqual(post.requestBody.content['application/json'].schema.required,['reason']);
 assert.equal(post['x-runtime-status'],'phase-10-08-implemented');
});
