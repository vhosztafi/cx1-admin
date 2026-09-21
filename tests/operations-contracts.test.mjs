import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
const schema=JSON.parse(await readFile(new URL('../contracts/schemas/operations.schema.json',import.meta.url),'utf8'));
const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);ajv.addSchema(schema);
const valid=(name,value)=>{const check=ajv.getSchema(`${schema.$id}#/$defs/${name}`);assert.ok(check,name);return check(value);};
const id='11111111-1111-4111-8111-111111111111';

test('file staging receipt is distinct from document version and excludes storage identities',()=>{
 const upload={id,subjectRecordId:id,name:'fictional.pdf',mediaType:'application/pdf',byteLength:100,sha256:'a'.repeat(64),state:'pending',createdAt:'2026-09-21T12:00:00Z',verifiedAt:null,failureCode:null};
 assert.ok(valid('OpsFileUpload',upload));
 for(const extra of [{fileId:id},{storageKey:'ready/example.bin'},{documentId:id},{byteLength:20971521},{mediaType:'text/html'},{state:'sent'}])assert.equal(valid('OpsFileUpload',{...upload,...extra}),false);
});
test('operational subjects and assignments use closed typed alternatives',()=>{
 assert.ok(valid('OpsSubjectWrite',{kind:'policy',parentId:id}));
 for(const value of [{kind:'support-flag',parentId:id},{kind:'policy',parentId:id,agencyId:id},{kind:'policy',parentId:'bad'}])assert.equal(valid('OpsSubjectWrite',value),false);
 assert.ok(valid('OpsAssignment',{kind:'team',teamId:id}));assert.equal(valid('OpsAssignment',{kind:'team',teamId:id,ownerId:id}),false);
});
test('task writes preserve source types and exclude status actor or completed checklist injection',()=>{
 const task={subjectRecordId:id,typeCode:'complaint',title:'Chase evidence',priority:'normal',assignment:{kind:'user',ownerId:id},dueOn:'2026-10-01'};
 assert.ok(valid('OpsTaskWrite',task));assert.ok(valid('OpsTaskWrite',{...task,typeCode:'agency-onboarding'}));
 for(const extra of [{state:'completed'},{actorId:id},{checklist:[{completed:true}]},{premium:'1.00'}])assert.equal(valid('OpsTaskWrite',{...task,...extra}),false);
 assert.ok(valid('OpsTaskTransition',{state:'awaiting-information',reason:'Waiting for agency response'}));assert.equal(valid('OpsTaskTransition',{state:'completed',reason:'   '}),false);
});
test('bulk commands are bounded and require each selected record version',()=>{
 const command={tasks:[{id,etag:'"opaque"'}],reason:'Agreed review date',dueOn:'2026-10-01'};
 assert.ok(valid('OpsTaskBulkDue',command));assert.equal(valid('OpsTaskBulkDue',{...command,tasks:[]}),false);
 assert.equal(valid('OpsTaskBulkDue',{...command,tasks:Array.from({length:101},()=>command.tasks[0])}),false);
 assert.equal(valid('OpsTaskBulkDue',{...command,tasks:[{id}]}),false);
});
test('incomplete incident drafts are product specific without invented occurrence or caller source version',()=>{
 for(const productCode of ['motor-trade-road-risks','motor-trade-combined','commercial-combined'])assert.ok(valid('OpsIncidentDraftWrite',{policyId:id,productCode}));
 const cc={policyId:id,productCode:'commercial-combined',commercialSubject:{kind:'property',locationId:id,coverCode:'buildings'}};
 assert.ok(valid('OpsIncidentDraftWrite',cc));assert.equal(valid('OpsIncidentDraftWrite',{...cc,motorSubject:{kind:'registered-vehicle',vehicleId:id}}),false);
 assert.equal(valid('OpsIncidentDraftWrite',{...cc,kind:'road-accident'}),false);
 assert.equal(valid('OpsIncidentDraftWrite',{...cc,versionId:id}),false);assert.equal(valid('OpsIncidentDraftWrite',{...cc,paid:'100.00'}),false);
});
test('task updates cannot reparent records and blank message drafts have valid saved read models',()=>{
 assert.equal(valid('OpsTaskUpdateWrite',{subjectRecordId:id,typeCode:'complaint',title:'Review',priority:'normal',assignment:{kind:'unassigned'}}),false);
 assert.ok(valid('OpsMessage',{id,threadId:id,body:'',recipientContactIds:[],attachmentVersionIds:[],state:'draft',createdAt:'2026-09-21T12:00:00Z',authorLabel:'Demo user'}));
});
test('occurrence preserves date and approximate precision and rejects fake exact precision',()=>{
 const date={occurredOn:'2026-09-01',timeZone:'Europe/London',precision:'date'};
 assert.ok(valid('OpsOccurrence',date));assert.equal(valid('OpsOccurrence',{...date,occurredAt:'2026-09-01T00:00:00Z'}),false);
 assert.ok(valid('OpsOccurrence',{...date,precision:'approximate',approximateLocalTime:'14:30'}));
 assert.equal(valid('OpsOccurrence',{...date,precision:'approximate',approximateLocalTime:'25:00'}),false);
 assert.equal(valid('OpsOccurrence',{...date,precision:'exact'}),false);
});
test('audience and pack schemas require explicit relationship and immutable selected versions',()=>{
 assert.ok(valid('OpsThreadWrite',{subject:'Agency update',visibility:'agency',relationshipId:id}));
 assert.equal(valid('OpsThreadWrite',{subject:'Agency update',visibility:'agency'}),false);
 assert.equal(valid('OpsThreadWrite',{subject:'Internal',visibility:'internal',relationshipId:id}),false);
 const pack={documentVersionIds:[id],recipientContactIds:[id],subject:'Your documents',body:'Please find the selected versions.'};
 assert.ok(valid('OpsPackWrite',pack));assert.equal(valid('OpsPackWrite',{...pack,documentIds:[id]}),false);assert.equal(valid('OpsPackWrite',{...pack,recipientContactIds:[]}),false);
});
test('generated OpenAPI uses final operational schemas after legacy form modifiers',async()=>{
 const api=JSON.parse(await readFile(new URL('../contracts/openapi.json',import.meta.url),'utf8'));
 assert.equal(api.paths['/incidents'].post.requestBody.content['application/json'].schema.$ref,'#/components/schemas/OpsIncidentDraftWrite');
 assert.equal(api.paths['/tasks'].post.requestBody.content['application/json'].schema.$ref,'#/components/schemas/OpsTaskWrite');
 for(const path of ['/tasks/bulk-due-date','/tasks/bulk-completion','/tasks/{taskId}/comments','/incidents/{incidentId}/contact'])assert.ok(api.paths[path],path);
 assert.equal(api.paths['/incidents'].post['x-runtime-status'],'phase-9-contract-only');
});
test('document generation separates internal and agency audience shape',()=>{
 const input={kind:'policy-schedule',source:{kind:'policy-version',policyVersionId:id},templateVersionId:id,visibility:'internal',reason:'Requested copy'};
 assert.ok(valid('OpsDocumentGenerate',input));
 assert.equal(valid('OpsDocumentGenerate',{...input,relationshipId:id}),false);
 assert.equal(valid('OpsDocumentGenerate',{...input,visibility:'agency'}),false);
 assert.ok(valid('OpsDocumentGenerate',{...input,visibility:'agency',relationshipId:id}));
});

test('document generation pins quotation terms and constrains source kinds',()=>{
 const input={kind:'quotation',source:{kind:'quote-revision',quoteRevisionId:id},templateVersionId:id,visibility:'internal',reason:'Requested exact terms'};
 assert.equal(valid('OpsDocumentGenerate',input),false);
 assert.ok(valid('OpsDocumentGenerate',{...input,source:{...input.source,quoteTermsVersionId:id}}));
 assert.ok(valid('OpsDocumentGenerate',{...input,kind:'statement-of-fact',documentId:id}));
 assert.equal(valid('OpsDocumentGenerate',{...input,kind:'policy-certificate'}),false);
 assert.equal(valid('OpsDocumentGenerate',{...input,kind:'policy-schedule',source:{kind:'servicing-terms',termsVersionId:id}}),false);
});

test('document version status and exact content routes use verified read contracts',async()=>{
 const api=JSON.parse(await readFile(new URL('../contracts/openapi.json',import.meta.url),'utf8'));
 assert.equal(api.paths['/document-versions/{versionId}'].get.responses[200].content['application/json'].schema.$ref,'#/components/schemas/OpsDocumentVersion');
 for(const path of ['/document-versions/{versionId}','/document-versions/{versionId}/content','/document-versions/{versionId}/preview']){
  const operation=api.paths[path].get;
  assert.equal(operation['x-runtime-status'],'phase-9-07-api-verified');
  assert.ok(operation.responses[200].headers['Cache-Control']);
  assert.ok(operation.responses[200].headers['X-Content-Type-Options']);
  assert.ok(operation.responses[503]);
 }
});
