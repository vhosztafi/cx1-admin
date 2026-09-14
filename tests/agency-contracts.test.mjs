import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';

const read=async path=>JSON.parse(await readFile(new URL(`../${path}`,import.meta.url),'utf8'));
const doc=await read('contracts/openapi.json');
const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);ajv.addFormat('binary',true);
const root='https://contracts.cover-mga.example/agency-tests';
function relocate(value){
 if(Array.isArray(value))return value.map(relocate);
 if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).map(([key,item])=>[key,key==='$ref'?item.replace('#/components/schemas/',`${root}#/$defs/`):relocate(item)]));
 return value;
}
ajv.addSchema({$id:root,$defs:relocate(doc.components.schemas)});
const schema=name=>ajv.getSchema(`${root}#/$defs/${name}`);
const op=(path,method='post')=>doc.paths[path][method];
const body=path=>ajv.compile(relocate(op(path).requestBody.content['application/json'].schema));
const uuid='11111111-1111-4111-8111-111111111111';
const time='2026-09-14T09:00:00Z';
const product={productVersionId:uuid,effectiveFrom:'2026-09-14',brokerCommissionBasisPoints:1250};
const terms=()=>({effectiveFrom:'2026-09-14',reason:'Fictional renewal of agreed terms',commercialTerms:{effectiveFrom:'2026-09-14',commissionBasis:'per-product',feeSharing:'none',volumeCommitmentMode:'none',minimumPremiumOverrideMode:'none',referralRouting:'standard-internal-underwriting'},settlement:{statementCycle:'monthly',method:'bank-transfer',premiumCollection:'agency',commissionSettlement:'net-remittance'},paymentTermsDays:30,creditLimit:'25000.00',products:[product]});
const invitation={id:uuid,userId:uuid,agencyId:uuid,email:'fictional@example.test',role:'broker-admin',createdAt:time};

test('all 24 fixed wizard option families exactly preserve source labels and values',async()=>{
 const rendered=await read('docs/design/source/prototype-render-data.json');
 const mappings=await read('contracts/examples/agency-option-mapping.json');
 const targets=await read('contracts/examples/agency-option-targets.json');
 const fixed=rendered.items.filter(x=>x.method==='pNewAgency'&&x.options?.length&&x.label!=='Relationship manager');
 assert.equal(fixed.length,24);assert.deepEqual(new Set(Object.keys(targets)),new Set(fixed.map(x=>x.label)));
 for(const item of fixed){
  assert.deepEqual(Object.keys(mappings[item.label]),item.options);
  const target=targets[item.label].split('.').reduce((s,k)=>s.properties[k],doc.components.schemas.AgencyWrite);
  assert.deepEqual(new Set(target.enum),new Set(Object.values(mappings[item.label])));
 }
 assert.ok(op('/agency-relationship-managers','get'));assert.ok(op('/agency-product-catalog','get'));
 assert.ok(op('/agencies','get').parameters.some(x=>x.name==='relationshipManagerId'));
});
test('drafts remain incomplete and bounded without accepting authority or obsolete boolean answers',()=>{
 const validate=schema('AgencyWrite');
 for(const value of [{},{mainContact:{name:'Fictional contact'}},{commercialTerms:{commissionBasis:'flat-rate'}},{arrangesGeneralInsurance:'unchecked',compliance:{beneficialOwnershipVerified:'refer'}}])assert.ok(validate(value),JSON.stringify(validate.errors));
 for(const value of [{state:'active'},{legalName:null},{legalName:'x'.repeat(201)},{arrangesGeneralInsurance:true},{territory:'UK-and-EEA'},{correspondencePreference:'post'},{compliance:{beneficialOwnershipVerified:true}},{compliance:{verifiedBy:uuid}},{creditLimit:'-1.00'},{paymentTermsDays:365},{roles:['system-admin']}])assert.equal(validate(value),false,JSON.stringify(value));
 assert.equal(schema('AgencyDraftSave')({details:{},onboardingStep:7}),false);
 assert.equal(schema('AgencyDraftSave')({details:{},onboardingStep:1,state:'active'}),false);
});
test('product inputs reject caller-owned identities and invalid commission arithmetic',()=>{
 const validate=schema('AgencyProductWrite');assert.ok(validate(product));
 for(const value of [{...product,id:uuid},{...product,agencyId:uuid},{...product,termsVersionId:uuid},{...product,brokerCommissionBasisPoints:-1},{...product,brokerCommissionBasisPoints:10001},{...product,brokerCommissionBasisPoints:12.5},{...product,effectiveFrom:time}])assert.equal(validate(value),false);
});
test('complete terms require mode-selected values and bounded nonnegative money',()=>{
 const validate=schema('AgencyTermsWrite');assert.ok(validate(terms()),JSON.stringify(validate.errors));
 for(const [mode,value,selected] of [['commissionBasis','flatCommissionBasisPoints','flat-rate'],['feeSharing','feeShareBasisPoints','agreed-split'],['volumeCommitmentMode','volumeCommitment','target-tiered'],['minimumPremiumOverrideMode','minimumPremiumOverride','capacity-provider-agreed']]){
  const input=terms();input.commercialTerms[mode]=selected;assert.equal(validate(input),false,mode);
  input.commercialTerms[value]=value.endsWith('BasisPoints')?1500:'100.00';assert.ok(validate(input),JSON.stringify(validate.errors));
 }
 for(const mutate of [x=>x.products=[],x=>x.products=[product,product,product,product],x=>delete x.settlement,x=>x.creditLimit='100.001',x=>x.creditLimit='100000000000000000.00',x=>x.commercialTerms.referralRouting='relationship-manager',x=>x.approvedBy=uuid]){const x=terms();mutate(x);assert.equal(validate(x),false);}
});
test('evidence attestations cannot forge verification, provider results or another storage location',()=>{
 const validate=schema('AgencyEvidenceWrite');const input={kind:'toba',fileId:uuid,notes:'Fictional signature attested'};assert.ok(validate(input));
 for(const patch of [{kind:'fca'},{state:'verified'},{verifiedAt:time},{attestedBy:uuid},{agencyId:uuid},{documentId:uuid},{filePath:'C:/secret'},{inputFingerprint:'a'.repeat(64)}])assert.equal(validate({...input,...patch}),false);
 for(const kind of ['fca','financial-check','sanctions','ownership'])assert.ok(schema('AgencyCheckWrite')({kind}));
 assert.equal(schema('AgencyCheckWrite')({kind:'fca',result:'passed'}),false);
 assert.deepEqual(doc.components.schemas.AgencyEvidence.properties.kind.enum,['fca','toba','professional-indemnity','financial-check','sanctions','ownership','dpa','client-money']);
 const upload=op('/agencies/{agencyId}/evidence-files').requestBody.content['multipart/form-data'].schema;
 assert.equal(upload.properties.file.maxLength,10485760);
 assert.equal(op('/agencies/{agencyId}/evidence-files/{fileId}/content','get').responses[200].headers['X-Content-Type-Options'].schema.const,'nosniff');
});
test('staged invitations have no expiry or delivery and issued invitations do not claim delivery acceptance',()=>{
 const validate=schema('Invitation');const staged={...invitation,state:'staged'};assert.ok(validate(staged));
 for(const patch of [{expiresAt:time},{issuedAt:time},{notificationId:uuid},{invitationToken:'secret'},{tokenHash:'a'.repeat(64)},{state:'sent'}])assert.equal(validate({...staged,...patch}),false);
 const pending={...invitation,state:'pending',issuedAt:time,expiresAt:'2026-09-28T09:00:00Z',notificationId:uuid};assert.ok(validate(pending));
 const missing={...pending};delete missing.expiresAt;assert.equal(validate(missing),false);
 assert.ok(validate({...invitation,state:'revoked',revokedAt:time}));
 assert.equal(schema('AgencyNotification')({id:uuid,agencyId:uuid,kind:'invitation',state:'accepted',attempts:1,createdAt:time,retryAllowed:false}),false);
});
test('agency user writes accept exactly one broker role and never internal access or reassignment',async()=>{
 const source=await readFile(new URL('../docs/design/source/prototype-template.txt',import.meta.url),'utf8');
 for(const kind of ['aguser','invite']){const section=source.split(`if (m.kind === '${kind}')`)[1].split('return {wrapStyle: wrap')[0];assert.ok(section.includes('Broker administrator'));assert.ok(section.includes('Broker user (read only)'));}
 for(const role of ['broker-admin','broker-user','broker-readonly'])assert.ok(schema('AgencyInvitationWrite')({email:invitation.email,displayName:'Fictional User',role}));
 for(const value of [{role:'system-admin'},{role:'broker'},{roles:['broker-admin','underwriter']},{agencyId:uuid},{state:'active'},{approvedBy:uuid}])assert.equal(schema('AgencyInvitationWrite')({email:invitation.email,displayName:'Fictional User',role:'broker-user',...value}),false);
 assert.equal(schema('AgencyUserWrite')({displayName:'Fictional User',role:'broker-user',reason:'Edit',email:'changed@example.test'}),false);
});
test('state commands create proposals and decisions reject caller-selected approvers',()=>{
 for(const action of ['activate','suspend','reactivate']){
  const path=`/agencies/{agencyId}/${action}`;assert.ok(op(path).responses[202]);assert.equal(op(path).responses[200],undefined);
  assert.equal(body(path)({reason:'Fictional request',approvedBy:uuid}),false);
  assert.ok(op(path).parameters.some(x=>x.name==='If-Match'&&x.required));
 }
 const path='/agency-state-requests/{requestId}/decision';assert.ok(body(path)({outcome:'approve',reason:'Independent review'}));
 for(const patch of [{requestedBy:uuid},{approvedBy:uuid},{baseVersion:'forged'},{state:'applied'}])assert.equal(body(path)({outcome:'approve',reason:'Review',...patch}),false);
 const request={id:uuid,agencyId:uuid,requestedBy:uuid,reason:'Fictional request',baseVersion:'opaque',inputFingerprint:'a'.repeat(64),createdAt:time,state:'pending',requestKind:'activation',stateRequested:'active'};
 assert.ok(schema('AgencyStateRequest')(request));delete request.baseVersion;assert.equal(schema('AgencyStateRequest')(request),false);
});
test('demo reveal is internal, development-only, CSRF-protected and never cached',()=>{
 const reveal=op('/invitations/{invitationId}/demo-link');assert.equal(reveal['x-permission'],'internal-agency-user-admin-development-only');assert.equal(reveal['x-idempotency'],'not-cached');assert.deepEqual(reveal.security,[{Session:[],Csrf:[]}]);assert.equal(reveal.responses[200].headers['Cache-Control'].schema.const,'no-store');
 assert.ok(body('/auth/invitations/accept')({invitationToken:'A'.repeat(43),password:'FictionalPassword123!'}));
 for(const patch of [{role:'system-admin'},{agencyId:uuid},{password:'short'},{invitationToken:'wrong'}])assert.equal(body('/auth/invitations/accept')({invitationToken:'A'.repeat(43),password:'FictionalPassword123!',...patch}),false);
});
test('shared projections exclude internal evidence, arbitrary permissions, hidden counts and fabricated balances',()=>{
 const client={id:uuid,relationshipId:uuid,reference:'CL-DEMO',legalName:'Fictional Traders'};assert.ok(schema('AgencySharedClient')(client));
 for(const patch of [{internalNotes:'secret'},{otherAgencyId:uuid},{hiddenCount:3},{matchScore:100}])assert.equal(schema('AgencySharedClient')({...client,...patch}),false);
 assert.equal(schema('AgencySharedInstruction')({id:uuid,personId:uuid,instruction:'Allow more time',internalCategory:'health'}),false);
 assert.equal(schema('AgencySharedProduct')({productCode:'motor-trade-road-risks',name:'Road Risks',effectiveFrom:'2026-09-14',available:false,eligibilitySettingVersionId:uuid}),false);
 assert.ok(schema('AgencyUnavailableSection')({kind:'statements',state:'unavailable',owningPhase:10,message:'Available in finance phase'}));
 assert.equal(schema('AgencyUnavailableSection')({kind:'statements',state:'unavailable',owningPhase:10,message:'Pending',balance:'0.00'}),false);
 assert.equal(body('/agencies/{agencyId}/permission-requests')({permission:'underwriting-admin',reason:'Forged'}),false);
 for(const [path,methods] of Object.entries(doc.paths).filter(([path])=>path.startsWith('/agency-context')))for(const operation of Object.values(methods)){assert.equal(operation['x-permission'],'active-own-agency');assert.equal(operation.parameters.some(p=>p.name==='agencyId'),false);}
});
test('evidence writes return ID-only receipts and readiness is a fresh uncached read',()=>{
 for(const [suffix,status] of [['evidence-files','201'],['evidence','201'],['checks','202']]) {
  const operation=doc.paths[`/agencies/{agencyId}/${suffix}`].post;
  assert.deepEqual(Object.keys(operation.responses[status].content['application/json'].schema.properties),['id']);
  assert.equal(doc.paths[`/agencies/{agencyId}/${suffix}/{recordId}`].get['x-permission'],'agency-read');
 }
 const validation=doc.paths['/agencies/{agencyId}/validate'].post;
 assert.equal(validation['x-idempotency'],'not-cached');assert.equal(validation.parameters.some(x=>x.name==='Idempotency-Key'),false);
 assert.equal(validation.parameters.some(x=>x.name==='If-Match'&&x.required),true);
 assert.equal(doc.components.schemas.AgencyChecklist.properties.items.maxItems,80);
});
test('notification reads exclude delivery secrets and retry returns only a stable identity',()=>{
 const notification=schema('AgencyNotification');
 const value={id:uuid,agencyId:uuid,kind:'activation',state:'exhausted',attempts:6,createdAt:'2026-09-14T10:00:00Z',retryAllowed:true,etag:'"version"'};
 assert.equal(notification(value),true);
 for(const secret of ['token','password','protectedPayload','recipient','contentHash'])assert.equal(notification({...value,[secret]:'secret'}),false);
 const route='/agencies/{agencyId}/notifications/{notificationId}';
 assert.equal(op(route,'get')['x-permission'],'agency-admin');
 assert.deepEqual(Object.keys(op(route+'/retry').responses['202'].content['application/json'].schema.properties),['id']);
 assert.ok(op(route+'/retry').parameters.some(x=>x.name==='If-Match'&&x.required));
});

test('reviewed agency actions use agency-specific routes and every inventory control remains mapped',async()=>{
 const inventory=await read('docs/design/control-inventory.json'),reviews=await read('docs/design/reviewed-api-controls.json');
 const operations=new Set(Object.values(doc.paths).flatMap(x=>Object.values(x).map(x=>x.operationId)));
 const agencyControls=inventory.controls.filter(x=>['pNewAgency','pAgency','pAgents','pPortal'].includes(x.method)||(x.method==='modalVals'&&x.tabs.some(t=>['aguser','invite'].includes(t))));
 assert.ok(agencyControls.some(x=>x.tabs.includes('aguser')));assert.ok(agencyControls.some(x=>x.tabs.includes('invite')));
 for(const control of agencyControls){const row=reviews.find(x=>x.controlId===control.id);assert.ok(row,control.id);for(const id of row.operationIds)assert.ok(operations.has(id),id);}
 assert.deepEqual(reviews.find(x=>x.controlId==='CTL-e7a0096dd263').operationIds,['updateAgencyUser','deactivateAgencyUser','reactivateAgencyUser']);
 assert.deepEqual(reviews.find(x=>x.controlId==='CTL-2aefbeec3f5b').operationIds,['decideAgencyPermission']);
 for(const action of ['getAgencySharingPreview','uploadAgencyEvidenceFile','downloadAgencyEvidenceFile','listAgencyActivity','listAgencyNotifications','listAgencyTermsVersions','requestAgencyTermsChange'])assert.ok(operations.has(action),action);
});
