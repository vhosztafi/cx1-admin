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
const quoteDraft=await read('schemas/quote-draft.schema.json');
const issuedPolicy=await read('schemas/issued-policy.schema.json');
ajv.addSchema(policy);ajv.addSchema(draft);ajv.addSchema(quoteDraft);ajv.addSchema(issuedPolicy);
const rootId='https://contracts.cover-mga.example/api-schemas';
function relocate(value){
 if(Array.isArray(value))return value.map(relocate);
 if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).map(([key,v])=>[key,key==='$ref'?v.replace('#/components/schemas/',`${rootId}#/$defs/`).replace('./schemas/policy.schema.json',policy.$id).replace('./schemas/policy-draft.schema.json',draft.$id).replace('./schemas/quote-draft.schema.json',quoteDraft.$id).replace('./schemas/issued-policy.schema.json',issuedPolicy.$id):relocate(v)]));
 return value;
}
ajv.addSchema({$id:rootId,$defs:relocate(document.components.schemas)});
const getOperation=id=>{const result=operations.find(op=>op.operationId===id);assert.ok(result,`Unknown operation ${id}`);return result;};

test('implemented servicing proof reads have bounded signed paging and closed response contracts',()=>{
 for(const name of ['listServicingEvidenceFiles','listDraftEvidence','listServicingEvidenceEvents','listServicingReferrals','listServicingReferralDecisions']) {
  const op=getOperation(name);assert.equal(op['x-runtime-status'],'phase-7-06-read-implemented');assert.equal(op['x-permission'],'policy-read');
  assert.equal(op.parameters.find(x=>x.name==='pageSize').schema.maximum,50);
  assert.equal(op.parameters.find(x=>x.name==='cursor').schema.maxLength,2048);
  assert.equal(op.responses['200'].headers.ETag,undefined);
  assert.equal(op.responses['200'].headers['Cache-Control'].schema.const,'no-store');
 }
 assert.equal(getOperation('listDraftEvidence').parameters.find(x=>x.name==='cycleId').required,true);
 const download=getOperation('downloadServicingEvidenceFile');assert.equal(download['x-permission'],'policy-read');
 assert.equal(download.responses['200'].headers['X-Content-Type-Options'].schema.const,'nosniff');
 assert.ok(download.responses['200'].content['application/pdf']);assert.ok(download.responses['200'].headers['Content-Disposition']);
 for(const name of ['ServicingFilePage','ServicingAssociationPage','ServicingReviewPage','ServicingDecisionPage','ServicingReferralPage','ServicingProofRequirements'])
  assert.equal(document.components.schemas[name].additionalProperties,false);
 assert.equal(getOperation('uploadServicingEvidenceFile')['x-runtime-status'],'phase-7-06-command-implemented');
});

test('servicing proof commands require owned cycle, fingerprints, child versions and closed decision payloads',()=>{
 const id='10000000-0000-4000-8000-000000000001',etag='"AAAAAAAAAAA="',fingerprint='a'.repeat(64),reason='Fictional reviewed evidence';
 const check=name=>ajv.getSchema(`${rootId}#/$defs/${name}`);
 const attach=check('ServicingAttachProofRequest'),review=check('ServicingReviewProofRequest'),resolve=check('ServicingResolveProofRequest');
 const input={cycleId:id,fileId:id,requirementCode:'warranty-acknowledgement',inputFingerprint:fingerprint,reason};
 assert.ok(attach(input),JSON.stringify(attach.errors));assert.equal(attach({...input,approved:true}),false);
 assert.equal(attach({...input,inputFingerprint:undefined}),false);
 assert.ok(review({cycleId:id,associationEtag:etag,outcome:'accepted',expectedFingerprint:fingerprint,reason}));
 assert.equal(review({cycleId:id,outcome:'accepted',expectedFingerprint:fingerprint,reason}),false);
 assert.ok(resolve({cycleId:id,conditionEtag:etag,evidenceAssociationId:id,outcome:'satisfied',reason}));
 const decisions=check('ServicingSelectedReferralDecisionRequest');
 const decision={referralId:id,etag,outcome:'query',reason,question:'Please supply trading proof',conditions:[{code:'provide-trading-history'}]};
 assert.ok(decisions({cycleId:id,decisions:[decision]}),JSON.stringify(decisions.errors));
 assert.equal(decisions({cycleId:id,decisions:[{...decision,question:undefined}]}),false);
 assert.equal(decisions({cycleId:id,decisions:Array(51).fill(decision)}),false);
 for(const name of ['uploadServicingEvidenceFile','attachDraftEvidence','reviewServicingEvidence','withdrawDraftEvidence','decideServicingReferrals','decideServicingReferral','resolveServicingReferralCondition']) {
  const op=getOperation(name);assert.equal(op['x-runtime-status'],'phase-7-06-command-implemented');
  for(const header of ['If-Match','Idempotency-Key','X-Edit-Lease'])assert.equal(op.parameters.find(x=>x.name===header)?.required,true,`${name} ${header}`);
 }
 assert.equal(document.paths['/drafts/{draftId}/evidence/{evidenceId}/review'],undefined);
});
test('API component schemas all compile strictly, including both external policy schemas',()=>{
 for(const name of Object.keys(document.components.schemas))assert.equal(typeof ajv.getSchema(`${rootId}#/$defs/${name}`),'function',name);
});

test('quote create and save DTOs reject policy authority while accepting incomplete capture proposals',()=>{
 const create=ajv.getSchema(`${rootId}#/$defs/QuoteCreateRequest`),save=ajv.getSchema(`${rootId}#/$defs/QuoteSaveRequest`);
 const id='00000000-0000-4000-8000-000000000001';
 const proposal={schemaVersion:'1.0',productCode:'motor-trade-road-risks'};
 assert.equal(create({relationshipId:id,productVersionId:id}),true);
 assert.equal(create({relationshipId:id,productVersionId:id,matchSubmissionId:id,proposal}),true);
 assert.equal(save({proposal}),true);
 for(const field of ['agencyId','clientId','actorId','state','premium'])assert.equal(create({relationshipId:id,productVersionId:id,[field]:id}),false,field);
 for(const field of ['productVersionId','premium','provenance','term'])assert.equal(save({proposal:{...proposal,[field]:{}}}),false,field);
 assert.equal(save({proposal:{...proposal,insured:{clientId:id}}}),false);
 assert.equal(save({proposal:{...proposal,productCode:'commercial-combined'}}),false);
});

test('quote command receipts expose only identities and retain concurrency, CSRF and replay requirements',()=>{
 for(const name of ['createQuote','saveQuoteProposal','withdrawQuote','cloneQuote']) {
  const operation=getOperation(name);const success=operation.responses[name==='createQuote'||name==='cloneQuote'?201:200];
  assert.equal(success.content['application/json'].schema.$ref,'#/components/schemas/QuoteIdentityResult');
  assert.ok(success.headers.ETag);
  if(name==='createQuote'||name==='cloneQuote')assert.ok(success.headers.Location);
  assert.ok(operation.parameters.some(parameter=>parameter.name==='Idempotency-Key'&&parameter.required));
  if(name!=='createQuote')assert.ok(operation.parameters.some(parameter=>parameter.name==='If-Match'&&parameter.required));
  assert.ok(operation.security.every(security=>'Csrf' in security));
 }
 const receipt=ajv.getSchema(`${rootId}#/$defs/QuoteIdentityResult`);
 const id='00000000-0000-4000-8000-000000000001';
 assert.equal(receipt({id}),true);assert.equal(receipt({id,proposal:{}}),false);
});

test('quote readiness and revision discovery are scoped reads with required context',()=>{
 const readiness=getOperation('validateQuote');assert.equal(readiness.method,'get');assert.equal(readiness.path,'/quotes/{quoteId}/readiness');
 assert.equal(readiness['x-idempotency'],'not-cached');assert.ok(!document.paths['/quotes/{quoteId}/validate']);
 const product=getOperation('listQuoteProducts');assert.ok(product.parameters.some(parameter=>parameter.name==='relationshipId'&&parameter.required));
 const revision=getOperation('getQuoteRevision');assert.deepEqual(revision.parameters.filter(parameter=>parameter.in==='path').map(parameter=>parameter.name),['quoteId','revisionId']);
 const comparison=getOperation('compareQuoteRevisions');assert.ok(comparison.parameters.filter(parameter=>['leftRevisionId','rightRevisionId'].includes(parameter.name)).every(parameter=>parameter.required));
 for(const name of ['getQuote','validateQuote','getQuoteRevision','listQuoteProducts']) {
  const operation=getOperation(name);assert.equal(operation['x-runtime-status'],['getQuote','validateQuote'].includes(name)?'implemented-capture-only':name==='listQuoteProducts'?'implemented-capture-selection':'planned-phase-05');
  assert.equal(operation.responses[200].headers['Cache-Control'].schema.const,'no-store');
 }
});

test('quote lookup requests restrict target kinds and never accept arbitrary provider URLs',()=>{
 const check=ajv.getSchema(`${rootId}#/$defs/QuoteLookupRequest`);
 const id='00000000-0000-4000-8000-000000000001';
 const request={kind:'vehicle',revisionId:id,scope:'vehicle',riskItemId:id,scenario:'success'};
 assert.equal(check(request),true);
 assert.equal(check({...request,url:'https://example.com'}),false);
 assert.equal(check({...request,target:{kind:'insured'}}),false);
 assert.equal(check({...request,query:{registration:'DEMO 01',providerResult:{}}}),false);
 assert.equal(check({...request,scenario:'live-provider'}),false);
 assert.equal(check({...request,inputFingerprint:'invalid'}),false);
});

test('candidate and manual lookup selection require persisted identity and exclusive provenance',()=>{
 const check=ajv.getSchema(`${rootId}#/$defs/QuoteLookupSelectionRequest`);
 const id='00000000-0000-4000-8000-000000000001';
 const base={revisionId:id,inputFingerprint:'a'.repeat(64)};
 assert.equal(check({...base,lookupId:id,candidateId:id}),true);
 assert.equal(check({...base,lookupId:id,candidateId:id,values:{}}),false);
 const manual={...base,lookupId:id,manualReason:'No lookup match'};
 assert.equal(check(manual),true);
 const missing=structuredClone(manual);delete missing.manualReason;assert.equal(check(missing),false);
 assert.equal(check({...manual,candidateId:id}),false);
 assert.equal(check({...manual,values:{...manual.values,premium:'100.00'}}),false);
});

test('pending and failed quote lookups cannot expose successful candidates',()=>{
 const check=ajv.getSchema(`${rootId}#/$defs/QuoteLookupView`);
 const id='00000000-0000-4000-8000-000000000001';
 const view={id,revisionId:id,inputFingerprint:'a'.repeat(64),kind:'vehicle',scope:'vehicle',riskItemId:id,scenario:'success',state:'pending',candidates:[],attempts:0,source:null,asOf:null,createdAt:'2026-09-16T12:00:00Z',completedAt:null,workState:'pending',errorCode:null,nextAttemptAt:null,selectedRevisionId:null};
 assert.equal(check(view),true);
 const candidate={id,label:'Fictional vehicle match',patch:{make:'Example',model:'Demo'}};
 for(const state of ['pending','no-match','failed'])assert.equal(check({...view,state,candidates:[candidate]}),false);
 assert.equal(check({...view,state:'succeeded'}),false);
 assert.equal(check({...view,state:'succeeded',candidates:[candidate]}),true);
 assert.equal(check({...view,kind:'address',state:'succeeded',candidates:[candidate]}),false);
 assert.equal(check({...view,rawProviderResponse:'private'}),false);
});

test('quote evidence requires revision/file identity and cannot forge verification or a storage locator',()=>{
 const check=ajv.getSchema(`${rootId}#/$defs/QuoteEvidenceAttachRequest`);
 const id='00000000-0000-4000-8000-000000000001';
 const request={revisionId:id,inputFingerprint:'a'.repeat(64),requirementCode:'previous-insurance',fileId:id,reason:'Fictional evidence'};
 assert.equal(check(request),true);
 for(const field of ['verified','state','storagePath','externalUrl','documentVersionId'])assert.equal(check({...request,[field]:'forged'}),false);
 const upload=getOperation('uploadQuoteEvidenceFile');
 const fileSchema=upload.requestBody.content['multipart/form-data'].schema;
 assert.equal(fileSchema.properties.file.maxLength,10485760);
 assert.deepEqual(fileSchema.properties.contentType.enum,['application/pdf','image/png','image/jpeg','text/plain']);
 for(const name of ['startQuoteLookup','selectQuoteLookup','uploadQuoteEvidenceFile','attachQuoteEvidence','withdrawQuoteEvidence']) {
  const operation=getOperation(name);assert.ok(operation.parameters.some(parameter=>parameter.name==='If-Match'&&parameter.required));
  assert.ok(operation.parameters.some(parameter=>parameter.name==='Idempotency-Key'&&parameter.required));
  const response=operation.responses[name==='startQuoteLookup'?202:['uploadQuoteEvidenceFile','attachQuoteEvidence'].includes(name)?201:200];
  assert.equal(response.content['application/json'].schema.$ref,'#/components/schemas/QuoteIdentityResult');
 }
 assert.match(getOperation('withdrawQuoteEvidence').parameters.find(parameter=>parameter.name==='If-Match').description,/evidence ETag/);
 const download=getOperation('downloadQuoteEvidenceFile').responses[200];assert.ok(download.content['application/octet-stream']);assert.equal(download.headers['X-Content-Type-Options'].schema.const,'nosniff');
});

test('quote comparison preserves typed before/after values and paginates complete changes',()=>{
 const check=ajv.getSchema(`${rootId}#/$defs/QuoteRevisionChange`);
 const added={kind:'added',path:'/risk/field',after:{path:'/risk/field',json:'null'}};
 assert.equal(check(added),true);assert.equal(check({...added,before:{path:'/risk/field',json:'null'}}),false);
 assert.equal(check({kind:'changed',path:'/risk/field',before:{path:'/risk/field',json:'1'},after:{path:'/risk/field',json:'"1"'}}),true);
 assert.equal(check({kind:'changed',path:'/risk/field',after:{path:'/risk/field',json:'1'}}),false);
 const operation=getOperation('compareQuoteRevisions');
 assert.equal(operation.responses[200].content['application/json'].schema.$ref,'#/components/schemas/QuoteRevisionComparison');
 assert.equal(operation.parameters.find(parameter=>parameter.name==='pageSize').schema.maximum,100);
 assert.equal(operation.parameters.find(parameter=>parameter.name==='cursor').required,false);
 assert.equal(document.components.schemas.QuoteRevisionComparison.properties.changes.maxItems,100);
 assert.equal(operation.responses[200].headers['Cache-Control'].schema.const,'no-store');
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
 const targetPaths=await read('examples/agency-option-targets.json');
 const targets=Object.fromEntries(Object.entries(targetPaths).map(([label,path])=>[label,path.split('.').reduce((s,key)=>s.properties[key],document.components.schemas.AgencyWrite)]));
 for(const [label,mapping] of Object.entries(mappings)){
  const source=rendered.items.find(item=>item.method==='pNewAgency'&&item.label===label&&item.options?.length);
  assert.ok(source,label);assert.deepEqual(Object.keys(mapping),source.options,label);
  assert.deepEqual(new Set(Object.values(mapping)),new Set(targets[label].enum),label);
 }
});
test('reviewed detail bindings resolve to policy fields and categorical answers retain source detail',async()=>{
 const reviews=JSON.parse(await readFile(new URL('../docs/design/reviewed-api-controls.json',import.meta.url),'utf8'));
 const catalogs=await Promise.all(['prototype-detail-questions.json','prototype-quote-questions.json','prototype-quote-value-questions.json'].map(name=>read(`examples/${name}`)));
 const catalog={questions:catalogs.flatMap(c=>c.questions)};
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
test('quote question choices preserve the source options and stable categorical values',async()=>{
 const catalog=await read('examples/prototype-quote-questions.json');
 const source=JSON.parse(await readFile(new URL('../docs/design/source/prototype-render-data.json',import.meta.url),'utf8'));
 const inventory=JSON.parse(await readFile(new URL('../docs/design/control-inventory.json',import.meta.url),'utf8'));
 for(const question of catalog.questions){
  const control=inventory.controls.find(c=>c.id===question.sourceControlId);
  const variants=source.items.filter(item=>item.method===control.method&&item.path===control.path&&item.label===control.label&&item.options?.length);
  assert.ok(variants.some(item=>JSON.stringify(item.options)===JSON.stringify(question.sourceOptions)),question.questionId);
  if(question.kind==='reference'){
   assert.deepEqual(question.referenceValues.map(v=>v.label),question.sourceOptions);
   assert.equal(new Set(question.referenceValues.map(v=>v.value)).size,question.sourceOptions.length);
  }
 }
});
test('servicing input bindings resolve to their actual command payload fields',async()=>{
 const rows=JSON.parse(await readFile(new URL('../docs/design/reviewed-api-controls.json',import.meta.url),'utf8'));
 function hasField(schema,field){
  if(schema.$ref)return hasField(document.components.schemas[schema.$ref.split('/').at(-1)],field);
  if(schema.properties?.[field])return true;
  // Every alternative must expose a common source form field; one permissive
  // branch must not disguise a missing field on another outcome.
  const branches=schema.oneOf??schema.anyOf;
  return Boolean(branches?.length)&&branches.every(branch=>hasField(branch,field));
 }
 for(const row of rows)for(const binding of row.apiFields??[]){
  let schema=getOperation(binding.operationId).requestBody.content['application/json'].schema;
  assert.ok(hasField(schema,binding.field),`${row.controlId}: ${binding.operationId}.${binding.field}`);
 }
 const contact=ajv.getSchema(`${rootId}#/$defs/ContactWrite`);
 const details={fullName:'Alex Morgan Example',role:'Director',isPrimary:true,marketingConsent:{state:'not-asked',email:false,telephone:false,recordedAt:'2026-09-13T12:00:00Z',source:'demo-contact-form'}};
 assert.ok(contact(details));delete details.fullName;assert.equal(contact(details),false);
});
test('Motor Trade Combined step eight is cover rather than previous insurance',async()=>{
 const catalog=await read('examples/prototype-quote-questions.json');
 const cover=catalog.questions.filter(q=>q.stages.includes('Motor Trade Combined:step-8'));
 assert.ok(cover.length>0);
 assert.ok(cover.every(q=>q.targetContainer==='cover.responses'));
 assert.ok(!catalog.questions.some(q=>q.stages.some(s=>s.startsWith('Motor Trade Combined:'))&&q.targetContainer==='risk.previousInsurance.responses'));
});
test('evidence associations retain item scope and withdrawal cannot masquerade as approval',()=>{
 const schema=document.components.schemas.ProposalEvidence;
 assert.ok(schema.properties.riskItemId);assert.ok(!schema.required.includes('riskItemId'));
 assert.ok(schema.properties.state.enum.includes('withdrawn'));
 const decision=getOperation('decideProposalEvidence').requestBody.content['application/json'].schema;
 assert.deepEqual(decision.properties.state.enum,['accepted','rejected']);
 for(const name of ['Quote','Draft']){
  const body=getOperation(`attach${name}Evidence`).requestBody.content['application/json'].schema;
  const attach=body.$ref?document.components.schemas[body.$ref.split('/').at(-1)]:body;
  assert.ok(attach.required.includes('fileId'));assert.ok(attach.properties.riskItemId);
  if(name==='Draft')for(const field of ['cycleId','inputFingerprint','reason'])assert.ok(attach.required.includes(field));
  if(name==='Quote')for(const field of ['revisionId','inputFingerprint'])assert.ok(attach.required.includes(field));
  const withdrawRef=getOperation(`withdraw${name}Evidence`).requestBody.content['application/json'].schema;
  const withdraw=withdrawRef.$ref?document.components.schemas[withdrawRef.$ref.split('/').at(-1)]:withdrawRef;
  assert.deepEqual(withdraw.required,name==='Draft'?['cycleId','associationEtag','reason']:['reason']);assert.equal(withdraw.additionalProperties,false);
 }
});
test('MFA verification and activation are separate contracts with explicit acknowledgement',()=>{
 const confirm=getOperation('confirmMfaEnrolment');
 const request=confirm.requestBody.content['application/json'].schema;
 const check=ajv.compile(request);
 assert.ok(check({code:'012345'}));assert.equal(check({code:'12345'}),false);assert.equal(check({code:'abcdef'}),false);
 const response=confirm.responses['200'].content['application/json'].schema;
 assert.equal(response.properties.state.const,'verified-pending-activation');
 const activate=getOperation('activateMfaEnrolment');
 const acknowledge=ajv.compile(activate.requestBody.content['application/json'].schema);
 assert.ok(acknowledge({recoveryCodesSaved:true}));assert.equal(acknowledge({recoveryCodesSaved:false}),false);assert.equal(acknowledge({}),false);
 assert.ok(getOperation('cancelMfaEnrolment'));
 assert.ok(document.components.schemas.Agency.properties.state.enum.includes('abandoned'));
});
test('every reviewed filter binds to an actual API query parameter',async()=>{
 const rows=JSON.parse(await readFile(new URL('../docs/design/reviewed-api-controls.json',import.meta.url),'utf8'));
 for(const row of rows)for(const binding of row.queryFields??[]){
  assert.ok(getOperation(binding.operationId).parameters.some(p=>p.in==='query'&&p.name===binding.parameter),`${row.controlId}: ${binding.parameter}`);
 }
});
test('incident capture preserves unknown involvement and does not invent a time',()=>{
 const check=ajv.getSchema(`${rootId}#/$defs/IncidentDraftWrite`);
 const value={policyId:'11111111-1111-4111-8111-111111111111',versionId:'22222222-2222-4222-8222-222222222222',occurredOn:'2026-09-01',thirdPartyInvolvement:'unknown',estimatedValueAtRisk:'100.00'};
 assert.ok(check(value));assert.equal(check({...value,approximateLocalTime:'25:00'}),false);
 assert.equal(check({...value,estimatedValueAtRisk:'-1.00'}),false);
 assert.equal(check({...value,thirdPartyInvolvement:false}),false);
});


test('implemented quote capture routes use the stored quote capability and retain partial readiness disclosure',()=>{
 for(const name of ['createQuote','saveQuoteProposal','getQuote','validateQuote']) {
  const operation=getOperation(name);
  assert.equal(operation['x-runtime-status'],'implemented-capture-only');
  assert.equal(operation['x-readiness-status'],'partial-fail-closed');
  assert.equal(operation['x-permission'],['createQuote','saveQuoteProposal'].includes(name)?'quote-capture':'quote-read');
 }
 assert.equal(getOperation('listQuoteProducts')['x-runtime-status'],'implemented-capture-selection');
 for(const name of ['cloneQuote','withdrawQuote'])assert.equal(getOperation(name)['x-runtime-status'],'planned-phase-05');
});

test('readiness can identify distinct missing questions without inventing array positions',()=>{
 const check=ajv.getSchema(`${rootId}#/$defs/QuoteReadiness`);
 const base={path:'/risk/business/responses/answers',code:'required-capture-field',message:'Complete the answer.',category:'capture',severity:'error'};
 const result={quoteId:'11111111-1111-4111-8111-111111111111',revisionId:'22222222-2222-4222-8222-222222222222',ready:false,
  issues:[{...base,questionId:'MTS-03-Q04'},{...base,questionId:'MTS-03-Q06'},base]};
 assert.equal(check(result),true,JSON.stringify(check.errors));
 for(const questionId of [null,'','x'.repeat(201),123])assert.equal(check({...result,issues:[{...base,questionId}]}),false);
 assert.equal(check({...result,issues:[{...base,callerOwnedQuestion:'forged'}]}),false);
});

test('capture views carry explicit immutable form catalogue versions',()=>{
 const check=ajv.getSchema(`${rootId}#/$defs/QuoteCaptureVersions`);
 const value={schemaVersion:'1.0',questionSetVersion:'retained-questions',referenceDataVersion:'retained-references'};
 assert.equal(check(value),true,JSON.stringify(check.errors));
 for(const field of Object.keys(value)) {
  const absent={...value};delete absent[field];assert.equal(check(absent),false);
  for(const invalid of ['',null,1,'x'.repeat(101)])assert.equal(check({...value,[field]:invalid}),false);
 }
 assert.equal(check({...value,currentProductVersion:'invented'}),false);
 assert.ok(document.components.schemas.QuoteCaptureView.required.includes('captureVersions'));
});


test('readiness history diagnostics preserve both the declaration and related incident paths',()=>{
 const validate=ajv.getSchema(`${rootId}#/$defs/QuoteReadiness`);
 const diagnostic={path:'/risk/business/responses/answers',code:'history-declaration-required',message:'Review the history declaration.',category:'capture',severity:'error',questionId:'prototype.quote.36da21d3c935',relatedPath:'/risk/drivers/0/losses/0'};
 const result={quoteId:'51000000-0000-4000-8000-000000000001',revisionId:'51000000-0000-4000-8000-000000000002',ready:false,issues:[diagnostic]};
 assert.equal(validate(result),true,JSON.stringify(validate.errors));
 diagnostic.relatedPath='x'.repeat(501);assert.equal(validate(result),false);
 delete diagnostic.relatedPath;assert.equal(validate(result),true);
});

test('agency policy projection rejects risk and financial facts and has no hidden-data search filters',()=>{
 const check=ajv.getSchema(`${rootId}#/$defs/AgencySharedPolicy`);
 const value={id:'11111111-1111-4111-8111-111111111111',reference:'PL-MT-0000000001',clientName:'Fictional client',
  productCode:'motor-trade-road-risks',state:'scheduled',startsAt:'2026-09-18T08:00:00Z',endsAt:'2027-09-18T08:00:00Z'};
 assert.equal(check(value),true,JSON.stringify(check.errors));
 for(const field of ['registration','premium','risk','snapshot','authority','acceptanceId']) assert.equal(check({...value,[field]:'hidden'}),false);
 for(const path of ['/agency-context/policies','/agencies/{agencyId}/sharing/policies']) {
  const operation=document.paths[path].get;
  const filters=operation.parameters.filter(x=>x.in==='query').map(x=>x.name).sort();
  assert.deepEqual(filters,['cursor','pageSize','q']);
 }
 assert.equal(document.paths['/policies'].get['x-permission'],'policy-discovery-read');
 assert.equal(document.paths['/policies'].get.parameters.some(x=>x.name==='status'),false);
 assert.deepEqual(document.paths['/policies'].get.parameters.find(x=>x.name==='productCode').schema.enum,['motor-trade-road-risks','motor-trade-combined']);
});


test('servicing rating contracts bind saved revision and expose immutable scoped history',()=>{
 const rate=getOperation('ratePolicyDraft'),history=getOperation('listServicingRatings');
 assert.equal(rate['x-permission'],'policy-draft-rate');
 assert.equal(rate['x-runtime-status'],'phase-7-05-implemented');
 assert.ok(rate.parameters.some(x=>x.name==='X-Edit-Lease'&&x.required));
 assert.ok(rate.parameters.some(x=>x.name==='If-Match'&&x.required));
 const validate=ajv.getSchema(`${rootId}#/$defs/ServicingRateRequest`);
 const request={revisionId:'10000000-0000-4000-8000-000000000001',reason:'Rate the saved adjustment'};
 assert.equal(validate(request),true,JSON.stringify(validate.errors));
 for(const key of ['premium','fee','actorId','ruleVersionId'])assert.equal(validate({...request,[key]:'forged'}),false,key);
 assert.equal(history.parameters.find(x=>x.name==='pageSize').schema.maximum,50);
 assert.equal(history.responses[200].headers['Cache-Control'].schema.const,'no-store');
 assert.equal(history.responses[200].headers.ETag,undefined);
 const schema=document.components.schemas.ServicingRatingHistory;
 assert.equal(schema.additionalProperties,false);
 assert.equal(schema.properties.items.maxItems,50);
 assert.ok(schema.properties.nextCursor);
 assert.equal(schema.properties.nextBeforeSequence,undefined);
});

test('servicing current authority keeps grants separate and limits paged live reads',()=>{
 const operation=getOperation('getServicingCurrentAuthority');
 assert.equal(operation['x-permission'],'policy-read');
 assert.equal(operation['x-runtime-status'],'phase-7-06-read-implemented');
 assert.equal(operation.parameters.find(x=>x.name==='pageSize').schema.maximum,5);
 assert.equal(operation.responses[200].headers['Cache-Control'].schema.const,'no-store');
 assert.equal(operation.responses[200].headers.ETag,undefined);
 const schema=document.components.schemas.ServicingCurrentAuthorityPage;
 assert.equal(schema.additionalProperties,false);assert.equal(schema.properties.items.maxItems,5);
 assert.ok(schema.required.includes('assessedAt'));assert.ok(schema.required.includes('canDecide'));
 assert.equal(document.components.schemas.ServicingCurrentGrant.properties.rows.maxItems,100);
});

test('servicing handoff requires exact saved scope and exposes bounded immutable history',()=>{
 const command=getOperation('submitPolicyDraft'),history=getOperation('listServicingSubmissions');
 assert.equal(command['x-permission'],'policy-draft-write');
 assert.equal(command['x-runtime-status'],'phase-7-06-command-implemented');
 for(const name of ['X-Edit-Lease','If-Match','Idempotency-Key'])assert.ok(command.parameters.some(x=>x.name===name&&x.required),name);
 assert.ok(command.security.every(x=>Object.hasOwn(x,'Session')&&Object.hasOwn(x,'Csrf')));
 assert.equal(document.components.securitySchemes.Csrf.name,'X-CSRF-Token');
 const validate=ajv.getSchema(`${rootId}#/$defs/ServicingSubmissionRequest`);
 const value={cycleId:'10000000-0000-4000-8000-000000000001',revisionId:'10000000-0000-4000-8000-000000000002',reason:'Submit saved servicing risk'};
 assert.equal(validate(value),true,JSON.stringify(validate.errors));
 for(const field of ['approved','submittedBy','ratingId','premium','inputHash'])assert.equal(validate({...value,[field]:'forged'}),false);
 assert.equal(validate({...value,reason:'short'}),false);
 assert.equal(command.responses[201].headers.Location,undefined);assert.ok(command.responses[201].headers.ETag);
 assert.equal(history['x-permission'],'policy-read');assert.equal(history.parameters.find(x=>x.name==='pageSize').schema.maximum,50);
 assert.equal(history.responses[200].headers.ETag,undefined);assert.equal(history.responses[200].headers['Cache-Control'].schema.const,'no-store');
 assert.equal(document.components.schemas.ServicingSubmissionPage.properties.items.maxItems,50);
 assert.ok(document.components.schemas.ServicingSubmissionPage.required.includes('current'));
});

test('referral work returns exactly one scoped referral with no unbounded collection inputs',()=>{
 const operation=getOperation('getServicingReferralWork');assert.equal(operation['x-permission'],'policy-read');
 assert.equal(operation['x-runtime-status'],'phase-7-06-read-implemented');assert.equal(operation.parameters.some(x=>x.in==='query'),false);
 const schema=document.components.schemas.ServicingReferralWork;assert.equal(schema.properties.items.minItems,1);assert.equal(schema.properties.items.maxItems,1);
 assert.equal(schema.properties.nextCursor.type,'null');assert.equal(schema.additionalProperties,false);
 assert.equal(operation.responses[200].headers.ETag,undefined);assert.equal(operation.responses[200].headers['Cache-Control'].schema.const,'no-store');
});
