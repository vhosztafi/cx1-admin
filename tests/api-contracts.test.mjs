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
test('every inline request, response and parameter schema compiles strictly',()=>{
 for(const op of operations){
  const schemas=[...op.parameters.map(p=>p.schema),...Object.values(op.requestBody?.content??{}).map(c=>c.schema),...Object.values(op.responses).flatMap(response=>Object.values(response.content??{}).map(c=>c.schema))];
  for(const schema of schemas)assert.equal(typeof ajv.compile(relocate(schema)),'function',op.operationId);
 }
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
test('safe agency instructions exclude internal support categorisation and origin data',()=>{
 const validate=ajv.getSchema(`${rootId}#/$defs/SafeSupportInstruction`);
 const safe={id:'11111111-1111-4111-8111-111111111111',personId:'22222222-2222-4222-8222-222222222222',instruction:'Please offer written follow-up.',reviewOn:'2027-01-01'};
 assert.ok(validate(safe));
 assert.equal(validate({...safe,internalCategory:'Health'}),false);
 assert.equal(validate({...safe,originRelationshipId:'33333333-3333-4333-8333-333333333333'}),false);
});
test('per-change effective dates are typed cover changes, not arbitrary delayed driver edits',()=>{
 const validate=ajv.getSchema(`${rootId}#/$defs/CoverChangeSchedule`);
 const change={changeId:'11111111-1111-4111-8111-111111111111',effectiveAt:'2026-10-01T00:00:00+01:00',action:'replace',section:{id:'22222222-2222-4222-8222-222222222222',code:'stock-custody',limit:'125000.00',excess:'500.00'}};
 assert.ok(validate(change));assert.equal(validate({...change,driverId:'33333333-3333-4333-8333-333333333333'}),false);
});
test('servicing issue requires the policy aggregate version and local profile cannot change access',()=>{
 const validateIssue=ajv.getSchema(`${rootId}#/$defs/DraftIssueWrite`);
 const command={ratingResultId:'11111111-1111-4111-8111-111111111111',acceptanceId:'22222222-2222-4222-8222-222222222222',targetRevision:4,reason:'Accepted adjustment'};
 assert.equal(validateIssue(command),false);assert.ok(validateIssue({...command,policyEtag:'"v7"'}));
 const validateProfile=ajv.getSchema(`${rootId}#/$defs/ProfileWrite`);
 const profile={fullName:'Demo User',displayName:'Demo',telephone:'0114 000 0000',jobTitle:'Underwriter',outOfOffice:false,taskDigest:'daily-0800'};
 assert.ok(validateProfile(profile));assert.equal(validateProfile({...profile,roleCodes:['system-admin']}),false);
 const access=getOperation('requestUserAccessChange');assert.equal(access.responses[200].content['application/json'].schema.$ref,'#/components/schemas/AccessChangeRequest');
});
test('reviewed control mappings point to real source controls and defined operations',async()=>{
 const inventory=JSON.parse(await readFile(new URL('../docs/design/control-inventory.json',import.meta.url),'utf8'));
 const map=JSON.parse(await readFile(new URL('../docs/design/api-control-map.json',import.meta.url),'utf8'));
 assert.equal(map.sourceSha256,inventory.sourceSha256);
 assert.equal(map.total,inventory.controls.length);
 assert.equal(new Set(map.controls.map(c=>c.controlId)).size,map.total);
 for(const row of map.controls){
  assert.ok(inventory.controls.some(c=>c.id===row.controlId));
  if(row.status==='reviewed'){
   assert.ok(row.reason.length>20);
   if(row.disposition!=='client-only')assert.ok(row.operationIds.length>0);
   for(const operationId of row.operationIds)getOperation(operationId);
  }
 }
 assert.equal(map.reviewed,map.controls.filter(c=>c.status==='reviewed').length);
 assert.equal(map.complete,map.reviewed===map.total);
});
test('onboarding field mappings resolve to real request properties',async()=>{
 const rows=JSON.parse(await readFile(new URL('../docs/design/reviewed-api-controls.json',import.meta.url),'utf8'));
 const request=getOperation('saveAgencyDraft').requestBody.content['application/json'].schema;
 const dereference=s=>s.$ref?.startsWith('#/components/schemas/')?document.components.schemas[s.$ref.split('/').at(-1)]:s;
 for(const row of rows.filter(r=>r.requestField)){
  let schema=request;
  for(const field of row.requestField.split('.')){
   schema=dereference(schema).properties?.[field];assert.ok(schema,`${row.controlId}: missing ${row.requestField}`);
  }
 }
});
test('draft onboarding and incidents permit incomplete forms without weakening value types',()=>{
 const agency=ajv.getSchema(`${rootId}#/$defs/AgencyWrite`);
 assert.ok(agency({legalName:'Fictional Brokers',address:{postcode:'S1 1AA'},mainContact:{name:'Demo Contact'},compliance:{tobaStatus:'sent'}}));
 assert.equal(agency({commercialTerms:{feeShareBasisPoints:10.5}}),false);
 const incident=ajv.getSchema(`${rootId}#/$defs/IncidentDraftWrite`);
 const proposal={policyId:'11111111-1111-4111-8111-111111111111',versionId:'22222222-2222-4222-8222-222222222222',contact:{name:'Demo Contact'}};
 assert.ok(incident(proposal));assert.equal(ajv.getSchema(`${rootId}#/$defs/IncidentWrite`)(proposal),false);
 assert.equal(incident({...proposal,occurredAt:'yesterday'}),false);
});
test('reviewed agency option meanings match prototype choices and actual DTO enums',async()=>{
 const mappings=await read('examples/agency-option-mapping.json');
 const rendered=JSON.parse(await readFile(new URL('../docs/design/source/prototype-render-data.json',import.meta.url),'utf8'));
 const p=document.components.schemas.AgencyWrite.properties;
 const targets={'Entity type':p.entityType,'Regulatory status':p.regulatoryStatus,'Client money basis':p.clientMoneyBasis,'Commission basis':p.commercialTerms.properties.commissionBasis,'Statement cycle':p.settlement.properties.statementCycle,'Premium collection':p.settlement.properties.premiumCollection,'Commission settlement':p.settlement.properties.commissionSettlement};
 for(const [label,mapping] of Object.entries(mappings)){
  const source=rendered.items.find(item=>item.method==='pNewAgency'&&item.label===label&&item.options?.length);
  assert.ok(source,label);assert.deepEqual(Object.keys(mapping),source.options,label);
  assert.deepEqual(new Set(Object.values(mapping)),new Set(targets[label].enum),label);
 }
});
test('reviewed detail bindings resolve to policy fields and categorical answers retain source detail',async()=>{
 const reviews=JSON.parse(await readFile(new URL('../docs/design/reviewed-api-controls.json',import.meta.url),'utf8'));
 const catalog=await read('examples/prototype-detail-questions.json');
 const definitions=policy.$defs;
 const dereference=s=>s.$ref?.startsWith('#/$defs/')?definitions[s.$ref.split('/').at(-1)]:s;
 function fields(schema,name){
  schema=dereference(schema);
  if(schema.properties?.[name])return [schema.properties[name]];
  return [...(schema.oneOf??[]),...(schema.anyOf??[])].flatMap(s=>fields(s,name));
 }
 for(const row of reviews)for(const binding of row.fieldBindings??[]){
  const parts=binding.targetPath.split('.');const last=parts.pop();
  for(const final of last.split('+')){
   let candidates=[policy];
   for(const part of [...parts,final]){
    const isArray=part.endsWith('[]');const name=part.replace('[]','');
    candidates=candidates.flatMap(s=>fields(s,name)).map(s=>isArray?dereference(s).items:s).filter(Boolean);
   }
   assert.ok(candidates.length,`${row.controlId}: ${binding.targetPath}`);
  }
  if(binding.questionId)assert.ok(catalog.questions.some(q=>q.questionId===binding.questionId));
 }
 assert.equal(new Set(catalog.questions.map(q=>q.questionId)).size,catalog.questions.length);
 for(const q of catalog.questions){
  if(q.kind==='boolean')assert.ok(q.sourceOptions.every(option=>/^(Yes|No)(\b|$)/.test(option)),q.questionId);
  if(q.kind==='reference')assert.ok(q.sourceOptions.length>0,q.questionId);
 }
});
