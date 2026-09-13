import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const document=await read('openapi.json');
const operations=Object.entries(document.paths).flatMap(([path,methods])=>Object.entries(methods).map(([method,op])=>({path,method,...op})));
const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);ajv.addFormat('binary',true);
const policy=await read('schemas/policy.schema.json'),draft=await read('schemas/policy-draft.schema.json');
ajv.addSchema(policy);ajv.addSchema(draft);
const rootId='https://contracts.cover-mga.example/api-schemas';
function relocate(value){
 if(Array.isArray(value))return value.map(relocate);
 if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).map(([key,v])=>[key,key==='$ref'?v.replace('#/components/schemas/',`${rootId}#/$defs/`).replace('./schemas/policy.schema.json',policy.$id).replace('./schemas/policy-draft.schema.json',draft.$id):relocate(v)]));
 return value;
}
ajv.addSchema({$id:rootId,$defs:relocate(document.components.schemas)});
const getOperation=id=>{const result=operations.find(op=>op.operationId===id);assert.ok(result,`Unknown operation ${id}`);return result;};
test('API component schemas all compile strictly, including both external policy schemas',()=>{
 for(const name of Object.keys(document.components.schemas))assert.equal(typeof ajv.getSchema(`${rootId}#/$defs/${name}`),'function',name);
});
test('every defined operation has unique identity, permission and correct path parameters',()=>{
 assert.equal(new Set(operations.map(op=>op.operationId)).size,operations.length);
 for(const op of operations){
  assert.ok(op['x-permission']);
  assert.deepEqual(op.parameters.filter(p=>p.in==='path').map(p=>p.name).sort(),[...op.path.matchAll(/\{(\w+)\}/g)].map(m=>m[1]).sort());
  assert.ok(op.parameters.filter(p=>p.in==='path').every(p=>p.required));
 }
});
test('cookie mutations require CSRF and secret-bearing account endpoints are never replay cached',()=>{
 for(const op of operations){
  if(op.method!=='get')assert.ok(op.security.every(s=>'Csrf' in s),op.operationId);
  if(op.path.startsWith('/account')||op.path.startsWith('/auth/')){
   assert.equal(op['x-idempotency'],'not-cached');
   assert.ok(!op.parameters.some(p=>p.name==='Idempotency-Key'));
  }
  if(op['x-idempotency']==='required')assert.ok(op.parameters.some(p=>p.name==='Idempotency-Key'&&p.required));
 }
 for(const name of ['savePolicyDraft','issuePolicyDraft']){
  const op=getOperation(name);for(const header of ['If-Match','X-Edit-Lease','Idempotency-Key'])assert.ok(op.parameters.some(p=>p.name===header&&p.required));
 }
});
test('request and response examples validate against actual operation DTOs',async()=>{
 const examples=await read('examples/api/core.json');
 for(const example of examples){
  const op=getOperation(example.operationId);
  const schema=example.direction==='request'?op.requestBody.content['application/json'].schema:op.responses[example.status].content[example.status>=400?'application/problem+json':'application/json'].schema;
  const validate=ajv.compile(relocate(schema));
  assert.equal(validate(example.body),example.valid,`${example.name}: ${JSON.stringify(validate.errors)}`);
 }
});
test('read contract never exposes storage paths or credential internals',()=>{
 const banned=new Set(['passwordHash','mfaSecretCiphertext','tokenHash','storageKey','securityStamp']);
 function check(value){if(!value||typeof value!=='object')return;for(const [key,item] of Object.entries(value)){assert.ok(!banned.has(key),key);check(item);}}
 for(const op of operations)if(op.method==='get')check(op.responses);
 for(const name of ['Actor','SessionView','DocumentVersion','Job'])check(document.components.schemas[name]);
});
