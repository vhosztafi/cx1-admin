import {readFile,writeFile} from 'node:fs/promises';
const o=(properties,required=Object.keys(properties))=>({type:'object',additionalProperties:false,properties,required});
const t=(maxLength=1000)=>({type:'string',minLength:1,maxLength});
const a=items=>({type:'array',items,maxItems:1000});
const e=(...values)=>({type:'string',enum:values});
const id={type:'string',format:'uuid'},instant={type:'string',format:'date-time'},date={type:'string',format:'date'};
const hash={type:'string',pattern:'^[a-f0-9]{64}$'},amount={type:'string',pattern:'^(0|[1-9][0-9]{0,12})\\.[0-9]{2}$'},count={type:'integer',minimum:0};
const api=JSON.parse(await readFile('contracts/openapi.json','utf8'));
// Copy only explicitly selected safe DTOs and their dependency graph, rebasing
// references. This never imports Actor, support flags or credential schemas.
const defs={};
function apiRef(name){if(!api.components.schemas[name])throw new Error(`Missing API schema ${name}`);if(!defs[name]){defs[name]={};defs[name]=rebase(api.components.schemas[name]);}return {$ref:`#/$defs/${name}`};}
function rebase(value){if(Array.isArray(value))return value.map(rebase);if(!value||typeof value!=='object')return value;
 if(value.$ref?.startsWith('#/components/schemas/'))return apiRef(value.$ref.split('/').at(-1));
 if(value.$ref?.startsWith('./schemas/'))return {$ref:value.$ref.replace('./schemas/','./')};
 return Object.fromEntries(Object.entries(value).map(([k,v])=>[k,rebase(v)]));}
const source=o({recordId:id,versionId:id,contentHash:hash});
const file=o({documentVersionId:id,sha256:hash,bytes:count,contentType:t(100)});
const ports={
 rating:{request:o({source,productVersionId:id,binderVersionId:id,ratingRuleVersionId:id,proposal:{$ref:'./policy-draft.schema.json'},effectiveSlices:a(o({effectiveAt:instant,proposal:{$ref:'./policy-draft.schema.json'}}))}),result:apiRef('RatingResult')},
 'document-generation':{request:o({source,templateVersionId:id,kind:t(100),audience:e('internal','agency','insurer'),merge:o({policyReference:t(40),insuredName:t(200),agencyName:t(200),startsAt:instant,endsAt:instant,sections:a(o({label:t(200),value:t(4000)}))})}),result:file},
 'document-storage':{request:o({temporaryObjectId:id,destinationDocumentVersionId:id,expectedSha256:hash,expectedBytes:count,contentType:t(100)}),result:o({documentVersionId:id,storageObjectId:id,sha256:hash,bytes:count,state:{const:'ready'}})},
 email:{request:o({messageId:id,templateVersionId:id,recipients:a(o({contactId:id,email:{type:'string',format:'email'},displayName:t(200)})),subject:t(300),body:t(20000),attachments:a(file)}),result:o({deliveryReference:t(100),completedAt:instant,recipients:a(o({contactId:id,status:e('delivered','rejected'),code:t(100)}))})},
 lookup:{request:apiRef('LookupRequest'),result:apiRef('LookupResult')},
 mid:{request:o({action:e('add','change','remove'),source,registration:t(20),itemKind:e('vehicle','trade-plate'),policyReference:t(40),startsAt:instant,endsAt:instant,productCode:e('motor-trade-road-risks','motor-trade-combined')}),result:o({status:e('accepted','rejected'),reference:t(100),acknowledgedAt:instant,reasonCodes:a(t(100))})},
 claims:{request:o({incidentId:id,source,productCode:e('motor-trade-road-risks','motor-trade-combined','commercial-combined'),incident:apiRef('IncidentWrite')}),result:o({providerReference:t(100),state:e('received','open','closed','rejected'),asOf:instant,paid:amount,reserved:amount,reasonCodes:a(t(100))})},
 payment:{request:o({obligationKind:e('refund','broker-remuneration'),obligationId:id,paymentKey:id,amount,currency:{const:'GBP'},payeeReference:t(100)}),result:o({state:e('confirmed','rejected','unknown'),paymentKey:id,providerReference:t(100),amount,currency:{const:'GBP'},asOf:instant,reasonCode:t(100)})},
 bordereau:{request:o({batchId:id,providerId:id,from:date,to:date,export:file,validationVersion:t(100)}),result:o({status:e('accepted','rejected'),reference:t(100),submittedAt:instant,exportSha256:hash,rowErrors:a(o({rowId:id,code:t(100),message:t(1000)}))})}
};
// Retained v1 claims envelopes remain valid; the incident revision projection is versioned.
ports.claims.request={oneOf:[ports.claims.request,apiRef('OpsClaimsSnapshot')]};
ports.claims.result={oneOf:[ports.claims.result,apiRef('OpsClaimsProviderSummary')]};
const alternatives=[];
for(const [kind,port] of Object.entries(ports))for(const direction of ['request','result']){
 alternatives.push(o({kind:{const:kind},direction:{const:direction},operationId:id,operationKey:t(200),sourceVersionId:id,scenarioVersionId:id,correlationId:id,recordedAt:instant,payload:port[direction]}));
}
alternatives.push(o({kind:e(...Object.keys(ports)),direction:{const:'failure'},operationId:id,operationKey:t(200),sourceVersionId:id,scenarioVersionId:id,correlationId:id,recordedAt:instant,payload:o({code:t(100),classification:e('rejected','transient','unknown-outcome'),retryable:{type:'boolean'},safeMessage:t(1000),providerReference:t(100)},['code','classification','retryable','safeMessage'])}));
const schema={$schema:'https://json-schema.org/draft/2020-12/schema',$id:'https://schemas.cover-mga.example/adapters/1.0',title:'Internal durable adapter envelopes',oneOf:alternatives,$defs:defs};
await writeFile('contracts/schemas/adapters.schema.json',JSON.stringify(schema,null,2)+'\n');
console.log(`Generated ${Object.keys(ports).length} typed adapter request/result pairs`);
