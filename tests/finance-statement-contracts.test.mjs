import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const api=JSON.parse(readFileSync(new URL('../contracts/openapi.json',import.meta.url)));

test('statement generation uses a scoped CSRF command and safe version receipt',()=>{
 const create=api.paths['/finance/agencies/{agencyId}/statements'].post;
 assert.equal(create['x-permission'],'statement-generate and current agency scope');
 assert.deepEqual(create.security,[{Session:[],Csrf:[]}]);
 assert.equal(create.parameters.find(x=>x.name==='Idempotency-Key').required,true);
 assert.deepEqual(api.components.schemas.FinanceStatementGeneration.required,['id','version']);
 assert.equal(create.responses['201'].content['application/json'].schema.$ref,'#/components/schemas/FinanceStatementGeneration');
});

test('statement reads expose current agency grant and exact saved bytes',()=>{
 const list=api.paths['/finance/agencies/{agencyId}/statements'].get;
 const detail=api.paths['/finance/statements/{id}'].get;
 const download=api.paths['/finance/statements/{id}/download'].get;
 assert.match(list['x-permission'],/statement-download grant/);
 assert.match(detail['x-permission'],/statement-download grant/);
 assert.match(download['x-permission'],/statement-download grant/);
 assert.equal(list.responses['200'].content['application/json'].schema.$ref,'#/components/schemas/FinanceStatementPage');
 assert.equal(download.responses['200'].content['text/csv'].schema.format,'binary');
 assert.equal(download.responses['200'].headers['Cache-Control'].schema.const,'no-store');
 const source=api.components.schemas.FinanceStatementSource;
 for(const field of ['agencyTermsVersionId','postingDate','postedAt','effectiveAt','dueDate','ageDaysAtEnd','pastDueAtEnd'])
  assert.ok(source.required.includes(field));
});

test('statement grant is a distinct current agency permission',()=>{
 const request=api.paths['/agencies/{agencyId}/permission-requests'].post;
 assert.ok(request.requestBody.content['application/json'].schema.properties.permission.enum.includes('statement-download'));
 assert.ok(api.components.schemas.AgencyPermissionGrant.properties.permission.enum.includes('statement-download'));
 const types=readFileSync(new URL('../contracts/generated/finance-statements.ts',import.meta.url),'utf8');
 assert.match(types,/sourceHash: string/);
 assert.match(types,/pastDueAtEnd: boolean/);
});
