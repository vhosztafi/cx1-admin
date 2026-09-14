import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';

const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const doc=await read('openapi.json');
const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);ajv.addFormat('binary',true);
const root='https://contracts.cover-mga.example/party-tests';
function relocate(value){
 if(Array.isArray(value))return value.map(relocate);
 if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).map(([k,v])=>[k,k==='$ref'?v.replace('#/components/schemas/',`${root}#/$defs/`):relocate(v)]));
 return value;
}
ajv.addSchema({$id:root,$defs:relocate(doc.components.schemas)});
const schema=name=>ajv.getSchema(`${root}#/$defs/${name}`);
const uuid='11111111-1111-4111-8111-111111111111';
const other='22222222-2222-4222-8222-222222222222';
const consent=state=>({state,email:state==='given',telephone:false,recordedAt:'2026-09-14T00:00:00Z',source:'Fictional demo contact form'});
const identity={legalName:'Fictional Demo Traders Ltd',entityType:'limited-company',address:{line1:'1 Fictional Road',town:'Sheffield',postcode:'S1 1AA',country:'GB'}};
const flag=()=>({typeCode:'vulnerability',internalCategory:'health',internalInstruction:'Offer written follow-up.',consentBasis:'verbal-consent',reviewOn:'2027-01-01',reason:'Fictional requested support.',visibleRelationshipIds:[]});

test('party identifier documentation does not mutate shared UUID schemas',()=>{
 for(const name of ['Actor','Job','PolicySummary','CommandResult'])assert.equal(doc.components.schemas[name].properties.id.description,undefined,name);
 assert.match(doc.components.schemas.ContactWrite.properties.personId.description,/same-client/);
 assert.equal(doc.components.schemas.Contact.properties.personId.description,undefined);
});

test('three consent states survive serialization without conflating not-asked and withheld',()=>{
 const validate=schema('MarketingConsent');
 for(const state of ['given','withheld','not-asked']){
  const value=JSON.parse(JSON.stringify(consent(state)));assert.ok(validate(value),JSON.stringify(validate.errors));assert.equal(value.state,state);
 }
 assert.equal(validate({...consent('given'),email:false}),false);
 assert.equal(validate({...consent('withheld'),telephone:true}),false);
 assert.equal(validate({...consent('not-asked'),email:true}),false);
 const missing=consent('not-asked');delete missing.state;assert.equal(validate(missing),false);
 assert.equal(validate({...consent('not-asked'),actorId:uuid}),false);
});
test('relationship contact preserves a declared full name without invented name components',()=>{
 const value={fullName:'Fictional Alex van der Example',role:'Director',isPrimary:true,marketingConsent:consent('not-asked')};
 assert.ok(schema('ContactWrite')(value));assert.equal(value.firstName,undefined);
 assert.ok(schema('Contact')({...value,id:uuid,personId:other,relationshipId:uuid}));
 assert.equal(schema('Contact')({...value,id:uuid}),false);
 assert.equal(schema('ContactWrite')({...value,relationshipId:uuid}),false);
});
test('support writes require explicit unique grants and reject declined-consent detail',()=>{
 const validate=schema('FlagWrite');assert.ok(validate(flag()));
 assert.equal(validate({...flag(),consentBasis:'declined'}),false);
 assert.equal(validate({...flag(),visibleRelationshipIds:[uuid]}),false);
 assert.ok(validate({...flag(),visibleRelationshipIds:[uuid],agencyInstruction:'Please send written updates.'}));
 assert.equal(validate({...flag(),visibleRelationshipIds:[uuid,uuid],agencyInstruction:'Please send written updates.'}),false);
 assert.equal(validate({...flag(),internalCategory:'invented'}),false);
});
test('flag mutations and safe views cannot contain internal detail or hidden totals',()=>{
 for(const [path,method,status] of [['/relationships/{relationshipId}/people/{personId}/flags','post',201],['/flags/{flagId}','put',200],['/flags/{flagId}/end','post',200]]){
  assert.equal(doc.paths[path][method].responses[status].content['application/json'].schema.$ref,'#/components/schemas/PartyMutationReceipt');
 }
 assert.ok(schema('PartyMutationReceipt')({id:uuid}));
 for(const key of ['internalCategory','reason','snapshot','agencyInstruction','etag'])assert.equal(schema('PartyMutationReceipt')({id:uuid,[key]:'hidden'}),false,key);
 const safe={id:uuid,personId:other,instruction:'Please send written updates.',reviewOn:'2027-01-01'};
 for(const key of ['internalInstruction','internalCategory','consentBasis','reason','originRelationshipId','hiddenFlagCount'])assert.equal(schema('SafeSupportInstruction')({...safe,[key]:'hidden'}),false,key);
 const list=doc.paths['/relationships/{relationshipId}/support-instructions/preview'].get;
 assert.equal(list.responses[200].content['application/json'].schema.properties.items.items.$ref,'#/components/schemas/SafeSupportInstruction');
});
test('match review uses a real intake identity and bounded evidence before quote capture',()=>{
 const value={id:uuid,submissionId:other,candidateClientId:uuid,confidence:'medium',state:'pending',ruleVersionId:uuid,
  submission:{id:other,reference:'DEMO-INTAKE-01',agencyId:uuid,agencyName:'Fictional Agency',identity,createdAt:'2026-09-14T00:00:00Z'},
  rule:{id:uuid,version:1,duplicateQuotePolicy:'refer',requireReview:true,summary:'Fictional review rule.'},
  signals:[{code:'postcode',summary:'Matching declared postcode.',submittedValue:'S1 1AA',candidateValue:'S1 1AA',weight:'moderate',result:'match'}]};
 const validate=schema('MatchReview');assert.ok(validate(value),JSON.stringify(validate.errors));
 assert.ok(validate({...value,quoteId:uuid}));
 const missing=structuredClone(value);delete missing.submissionId;assert.equal(validate(missing),false);
 assert.equal(validate({...value,signals:[{...value.signals[0],candidateValue:'x'.repeat(501)}]}),false);
 assert.equal(validate({...value,signals:[{...value.signals[0],internalSupportNeed:'hidden'}]}),false);
});
test('client summary distinguishes unavailable business records from verified empty records',()=>{
 const validate=schema('ClientSummary');const value={...identity,id:uuid,reference:'CN-DEMO-01',agencies:[],records:{state:'unavailable'}};
 assert.ok(validate(value));assert.ok(validate({...value,records:{state:'available',quoteCount:0,policyCount:0}}));
 assert.equal(validate({...value,records:{state:'unavailable',policyCount:0}}),false);
 assert.equal(validate({...value,records:{state:'available'}}),false);
 const activity={id:uuid,occurredAt:'2026-09-14T00:00:00Z',actorLabel:'Demo servicing',eventType:'contact.updated',summary:'Contact updated.'};
 assert.ok(schema('ClientActivity')(activity));assert.equal(schema('ClientActivity')({...activity,reason:'Restricted flag reason.'}),false);
});
test('party command concurrency names the actual parent or child resource',()=>{
 for(const [path,method,target] of [
  ['/clients/{clientId}','put','client'],['/clients/{clientId}/relationships','post','client'],
  ['/relationships/{relationshipId}/contacts','post','relationship'],['/relationships/{relationshipId}/contacts/{contactId}','put','contact'],
  ['/relationships/{relationshipId}/people/{personId}/flags','post','origin relationship'],['/flags/{flagId}','put','flag'],['/matches/{matchId}/decisions','post','match review']]){
  const op=doc.paths[path][method];assert.equal(op['x-etag-resource'],target);assert.ok(op.parameters.some(p=>p.name==='If-Match'&&p.required));
 }
});
test('party examples validate against the actual request and response operations',async()=>{
 const operations=Object.values(doc.paths).flatMap(Object.values);
 for(const example of await read('examples/api/parties.json')){
  const op=operations.find(o=>o.operationId===example.operationId);assert.ok(op);
  const target=example.direction==='request'?op.requestBody.content['application/json'].schema:op.responses[example.status].content['application/json'].schema;
  const validate=ajv.compile(relocate(target));assert.equal(validate(example.body),example.valid,`${example.name}: ${JSON.stringify(validate.errors)}`);
 }
});
