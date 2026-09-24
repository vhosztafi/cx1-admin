import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const api=JSON.parse(readFileSync(new URL('../contracts/openapi.json',import.meta.url)));

test('demo insurer submission pins version and hash with current finance authority',()=>{
 const post=api.paths['/finance/bordereaux/{batchId}/versions/{versionId}/submissions'].post;
 assert.match(post['x-permission'],/finance-bordereau and current internal finance identity/);
 assert.deepEqual(post.security,[{Session:[],Csrf:[]}]);
 assert.equal(post.parameters.find(x=>x.name==='If-Match').required,true);
 assert.equal(post.parameters.find(x=>x.name==='Idempotency-Key').required,true);
 assert.deepEqual(post.requestBody.content['application/json'].schema.required,['contentHash']);
 assert.equal(post.responses['202'].content['application/json'].schema.$ref,
  '#/components/schemas/FinanceSubmissionQueued');
 assert.equal(post['x-runtime-status'],'phase-10-10-implemented');
});

test('version-specific submission status exposes separate provider result and applied state',()=>{
 const get=api.paths['/finance/bordereaux/{batchId}/versions/{versionId}/submission'].get;
 assert.deepEqual(get.security,[{Session:[]}]);
 assert.match(get['x-permission'],/current internal finance identity/);
 assert.equal(get.responses['200'].content['application/json'].schema.$ref,
  '#/components/schemas/FinanceSubmission');
 const fields=api.components.schemas.FinanceSubmission.required;
 for(const field of ['versionId','contentHash','providerState','providerOperationId','appliedAt','attempts'])
  assert.ok(fields.includes(field));
 const types=readFileSync(new URL('../contracts/generated/finance-submissions.ts',import.meta.url),'utf8');
 assert.match(types,/providerState: 'accepted' \| 'rejected' \| null/);
 assert.match(types,/attempts: FinanceSubmissionAttempt\[\]/);
});
