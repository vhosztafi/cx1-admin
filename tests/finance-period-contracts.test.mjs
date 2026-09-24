import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const api=JSON.parse(readFileSync(new URL('../contracts/openapi.json',import.meta.url)));

test('period close is scoped, versioned, idempotent and reasoned',()=>{
 assert.deepEqual(api.paths['/finance/periods'].get.security,[{Session:[]}]);
 const post=api.paths['/finance/periods/{periodId}/close'].post;
 assert.match(post['x-permission'],/finance-period-close and current internal finance identity/);
 assert.deepEqual(post.security,[{Session:[],Csrf:[]}]);
 assert.equal(post.parameters.find(x=>x.name==='If-Match').required,true);
 assert.equal(post.parameters.find(x=>x.name==='Idempotency-Key').required,true);
 assert.deepEqual(post.requestBody.content['application/json'].schema.required,['reason']);
 assert.equal(post['x-runtime-status'],'phase-10-11-implemented');
});

test('correction pins original source and balanced four-account movement',()=>{
 const post=api.paths['/finance/corrections'].post;
 const journal=api.paths['/finance/journals'].post;
 assert.deepEqual(journal.requestBody.content['application/json'].schema,
  post.requestBody.content['application/json'].schema);
 assert.deepEqual(post.security,[{Session:[],Csrf:[]}]);
 assert.equal(post.parameters.find(x=>x.name==='Idempotency-Key').required,true);
 const input=post.requestBody.content['application/json'].schema;
 for(const field of ['originalSourceKind','originalSourceId','debtorDelta','providerDelta',
  'cashDelta','internalDelta','effectiveAt','reason'])assert.ok(input.required.includes(field));
 const output=api.components.schemas.FinanceCorrection;
 for(const field of ['postingId','postingDate','agencyId','originalSourceId'])
  assert.ok(output.required.includes(field));
 const types=readFileSync(new URL('../contracts/generated/finance-periods.ts',import.meta.url),'utf8');
 assert.match(types,/originalSourceKind:'insurance'\|'finance-posting'/);
});
