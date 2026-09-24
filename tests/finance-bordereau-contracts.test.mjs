import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const api=JSON.parse(readFileSync(new URL('../contracts/openapi.json',import.meta.url)));

test('bordereau commands require current finance authority, CSRF, keys and conditional revisions',()=>{
 const generate=api.paths['/finance/providers/{providerId}/periods/{periodId}/bordereaux'].post;
 assert.match(generate['x-permission'],/finance-bordereau and current internal finance identity/);
 assert.deepEqual(generate.security,[{Session:[],Csrf:[]}]);
 assert.equal(generate.parameters.find(x=>x.name==='Idempotency-Key').required,true);
 for(const path of ['/finance/bordereaux/{batchId}/members/{sourceJournalId}/corrections',
  '/finance/bordereaux/{batchId}/members/{sourceJournalId}/exclusions','/finance/bordereaux/{batchId}/validations']) {
  const command=api.paths[path].post;
  assert.equal(command.parameters.find(x=>x.name==='If-Match').required,true);
  assert.equal(command.parameters.find(x=>x.name==='Idempotency-Key').required,true);
  assert.deepEqual(command.security,[{Session:[],Csrf:[]}]);
 }
 const correction=api.paths['/finance/bordereaux/{batchId}/members/{sourceJournalId}/corrections'].post;
 const properties=Object.keys(correction.requestBody.content['application/json'].schema.properties);
 assert.deepEqual(properties,['policyReference','providerProductCode','agencyReference','reason']);
 for(const forbidden of ['premium','tax','fee','commission','netDue'])assert.ok(!properties.includes(forbidden));
});

test('bordereau read and export expose pinned version, full validation and exact CSV bytes',()=>{
 const detail=api.paths['/finance/bordereaux/{batchId}'].get;
 const download=api.paths['/finance/bordereaux/{batchId}/versions/{versionId}/download'].get;
 assert.match(detail['x-permission'],/current internal finance identity/);
 assert.match(download['x-permission'],/current internal finance identity/);
 assert.equal(detail.responses['200'].content['application/json'].schema.$ref,'#/components/schemas/BordereauVersion');
 assert.equal(download.responses['200'].content['text/csv'].schema.format,'binary');
 assert.equal(download.responses['200'].headers['Cache-Control'].schema.const,'no-store');
 const version=api.components.schemas.BordereauVersion;
 for(const name of ['sourceCutoff','sourceHash','membersHash','validation','contentHash','members'])
  assert.ok(version.required.includes(name));
 const member=api.components.schemas.BordereauMember;
 for(const name of ['sourceJournalId','productVersionId','agencyTermsVersionId','premium','tax','commission','netDue',
  'correctionActorId','correctionReason','excludedAt','exclusionReason'])assert.ok(member.required.includes(name));
 const types=readFileSync(new URL('../contracts/generated/finance-bordereaux.ts',import.meta.url),'utf8');
 assert.match(types,/sourceHash: string/);
 assert.match(types,/exclusionActorId: string \| null/);
});
