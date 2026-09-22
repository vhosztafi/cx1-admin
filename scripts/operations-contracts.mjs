import {mkdir,writeFile} from 'node:fs/promises';
const obj=(properties,required=Object.keys(properties))=>({type:'object',additionalProperties:false,properties,required});
const text=(maxLength=300)=>({type:'string',minLength:1,maxLength,pattern:'\\S'});
const en=(...values)=>({type:'string',enum:values});
const arr=(items,minItems=0,maxItems=100)=>({type:'array',items,minItems,maxItems,uniqueItems:true});
const ref=name=>({$ref:`#/$defs/${name}`});
const id={type:'string',format:'uuid'},date={type:'string',format:'date'},instant={type:'string',format:'date-time'},bool={type:'boolean'};
const amount={type:'string',pattern:'^(0|[1-9][0-9]{0,12})\\.[0-9]{2}$'},reason=text(1000),hash={type:'string',pattern:'^[a-f0-9]{64}$'};
const types=en('servicing','underwriting-referral','authority-referral','renewal','data-exception','agency-onboarding','complaint','underwriting');
const states=en('open','in-progress','awaiting-information','blocked','completed','cancelled');
const etag={type:'string',minLength:3,maxLength:100,pattern:'^"[!#-~]+"$'};
export function operationalDefinitions(){
 const d={};
 d.OpsSubjectWrite={oneOf:['agency','relationship','quote','policy','servicing-draft'].map(kind=>obj({kind:{const:kind},parentId:id}))};
 d.OpsSubject=obj({id,kind:en('agency','relationship','quote','policy','servicing-draft'),parentId:id,label:text(),href:text(500)});
 d.OpsAssignment={oneOf:[obj({kind:{const:'user'},ownerId:id}),obj({kind:{const:'team'},teamId:id}),obj({kind:{const:'unassigned'}})]};
 d.OpsTaskWrite=obj({subjectRecordId:id,typeCode:types,title:text(),priority:en('low','normal','high','urgent'),assignment:ref('OpsAssignment'),dueOn:date},['subjectRecordId','typeCode','title','priority','assignment']);
 d.OpsTaskUpdateWrite=obj(Object.fromEntries(Object.entries(d.OpsTaskWrite.properties).filter(([key])=>key!=='subjectRecordId')),['typeCode','title','priority','assignment']);
 d.OpsTaskTransition=obj({state:states,reason});
 d.OpsTaskSelection=arr(obj({id,etag}),1);
 d.OpsTaskBulkAssignment=obj({tasks:ref('OpsTaskSelection'),assignment:ref('OpsAssignment'),reason});
 d.OpsTaskBulkDue=obj({tasks:ref('OpsTaskSelection'),dueOn:date,reason});
 d.OpsTaskBulkComplete=obj({tasks:ref('OpsTaskSelection'),reason});
 d.OpsTaskChecklistWrite=obj({items:arr(obj({id,completed:bool}),1),reason});
 d.OpsTextWrite=obj({body:text(8000)});
 d.OpsWorkflowProvenance=obj({ruleCode:text(64),ruleVersionId:id,ruleVersion:{type:'integer',minimum:1},ruleTitle:text(200),sourceKind:en('quote-referral','servicing-referral','quote-query','servicing-query','match-information-request','policy-term','agency-follow-up','job-exception'),sourceEventId:id,createdAt:instant,sourceCondition:en('outstanding','resolved','not-due','unavailable'),sourceChanged:bool});
 d.OpsTask=obj({id,etag,subject:ref('OpsSubject'),assignmentLabel:text(),createdByLabel:text(),reference:text(40),...d.OpsTaskWrite.properties,state:states,createdBy:id,createdAt:instant,updatedAt:instant,overdue:bool,checklist:arr(obj({id,label:text(),required:bool,completed:bool})),completionReason:reason,sourceChanged:bool,workflow:ref('OpsWorkflowProvenance')},['id','etag','subject','assignmentLabel','createdByLabel','reference',...d.OpsTaskWrite.required,'state','createdBy','createdAt','updatedAt','overdue','checklist','sourceChanged']);
 d.OpsTaskEvent=obj({id,taskId:id,sequence:{type:'integer',minimum:1},kind:text(60),reason,actorLabel:text(),recordedAt:instant},['id','taskId','sequence','kind','actorLabel','recordedAt']);
 d.OpsComment=obj({id,taskId:id,body:text(8000),authorLabel:text(),createdAt:instant});
 d.OpsNote=obj({id,subjectRecordId:id,body:text(8000),authorLabel:text(300),createdAt:instant});
 d.OpsThreadWrite={oneOf:[obj({visibility:{const:'internal'},subject:text(300)}),obj({visibility:{const:'agency'},relationshipId:id,subject:text(300)})]};
 d.OpsMessageWrite=obj({body:{type:'string',maxLength:8000},recipientContactIds:arr(id,0,50),attachmentVersionIds:arr(id,0,20)});
 d.OpsThread={oneOf:d.OpsThreadWrite.oneOf.map(branch=>obj({id,subjectRecordId:id,...branch.properties,authorLabel:text(300),createdAt:instant},['id','subjectRecordId',...branch.required,'authorLabel','createdAt']))};
 d.OpsMessage=obj({id,threadId:id,...d.OpsMessageWrite.properties,state:en('draft','queued','sent','failed','superseded'),etag,createdAt:instant,updatedAt:instant,authorLabel:text(),sendJobId:id},['id','threadId',...d.OpsMessageWrite.required,'state','etag','createdAt','updatedAt','authorLabel']);
 d.OpsThreadRelationships=obj({items:arr(obj({id,label:text()})),nextCursor:text(2048)},['items']);
 d.OpsRecipientOptions=obj({items:arr(obj({id,label:text(),email:text(254)})),nextCursor:text(2048)},['items']);
 d.OpsAttachmentOptions=obj({items:arr(ref('OpsDocumentVersion')),nextCursor:text(2048)},['items']);
 d.OpsPackWrite=obj({documentVersionIds:arr(id,1,20),recipientContactIds:arr(id,1,50),subject:text(),body:text(8000)});
 d.OpsDelivery=obj({id,jobId:id,subjectRecordId:id,state:en('queued','delivered','failed','superseded'),createdAt:instant,completedAt:instant,documentVersionIds:arr(id,0,20),recipientLabels:{...arr(text(600),0,50),uniqueItems:false},subject:text(300),body:text(8000),etag,retryAllowed:bool,resendOfId:id,providerOutcome:en('delivered','rejected'),errorCode:text(100)},['id','jobId','subjectRecordId','state','createdAt','documentVersionIds','recipientLabels','subject','body','etag','retryAllowed']);
 d.OpsDeliveryAttempt=obj({id,deliveryId:id,number:{type:'integer',minimum:1},startedAt:instant,endedAt:instant,outcome:en('started','succeeded','rejected','retryable','superseded','quarantined'),errorCode:text(100)},['id','deliveryId','number','startedAt','outcome']);
 d.OpsDeliveryAttempts=obj({items:arr(ref('OpsDeliveryAttempt'),0,18),totalCount:{type:'integer',minimum:0,maximum:18}});
 d.OpsOccurrence={oneOf:[obj({occurredOn:date,timeZone:{const:'Europe/London'},precision:{const:'date'}}),obj({occurredOn:date,timeZone:{const:'Europe/London'},precision:{const:'approximate'},approximateLocalTime:{type:'string',pattern:'^([01][0-9]|2[0-3]):[0-5][0-9]$'}}),obj({occurredOn:date,timeZone:{const:'Europe/London'},precision:{const:'exact'},occurredAt:instant})]};
 d.OpsMotorSubject={oneOf:[obj({kind:{const:'registered-vehicle'},vehicleId:id,driverId:id,driverDeclaration:en('named','not-named','unknown'),drivable:en('yes','no-recovered','unknown')},['kind']),obj({kind:{const:'unregistered-vehicle'},registration:text(20),driverDeclaration:en('not-named','unknown'),drivable:en('yes','no-recovered','unknown')},['kind']),obj({kind:en('stock-or-customer-vehicle','premises','third-party-only'),itemDescription:text(1000),owner:en('insured','customer','third-party'),estimatedValueAtRisk:amount},['kind'])]};
 d.OpsCommercialSubject={oneOf:[obj({kind:{const:'property'},locationId:id,coverCode:en('buildings','contents','stock','business-interruption'),itemDescription:text(1000),owner:en('insured','customer','third-party'),estimatedValueAtRisk:amount},['kind']),obj({kind:{const:'liability'},coverCode:en('public-liability','products-liability','employers-liability'),locationId:id,occupationId:id,itemDescription:text(1000)},['kind'])]};
 const incident={policyId:id,occurrence:ref('OpsOccurrence'),kind:en('road-accident','vehicle-theft','premises-theft','fire','malicious-damage','third-party-injury','customer-vehicle-damage','property-damage','liability','other'),locationDescription:text(1000),policeReference:text(100),thirdPartyInvolvement:en('yes','no','unknown'),thirdPartyName:text(),thirdPartyInsurerOrRegistration:text(),description:text(8000),reportedBy:text(),reportingRoute:en('agency','insured-direct','third-party-or-insurer','police-or-recovery'),bestContactDescription:text(1000),evidenceDocumentVersionIds:arr(id,0,20)};
 d.OpsIncidentDraftWrite={oneOf:[obj({...incident,productCode:en('motor-trade-road-risks','motor-trade-combined'),motorSubject:ref('OpsMotorSubject')},['policyId','productCode']),obj({...incident,kind:en('premises-theft','fire','malicious-damage','third-party-injury','property-damage','liability','other'),productCode:{const:'commercial-combined'},commercialSubject:ref('OpsCommercialSubject')},['policyId','productCode'])]};
 d.OpsOccurrenceClarification=obj({occurrence:ref('OpsOccurrence'),reason});
 d.OpsOccurrenceWindow=obj({from:instant,to:instant,isExact:{type:'boolean'}});
 d.OpsOccurrenceResolution=obj({id,policyId:id,revisionId:id,knownAt:instant,state:en('resolved','ambiguous','partly-uncovered','uncovered','incomplete'),window:ref('OpsOccurrenceWindow'),candidates:arr(obj({versionId:id,sourceHash:hash,from:instant,to:instant,label:text()})),sourceHash:hash},['id','policyId','revisionId','knownAt','state','candidates']);
 d.OpsIncident=obj({id,reference:text(40),revisionId:id,draft:ref('OpsIncidentDraftWrite'),state:en('draft','logged','queued','handed-off','failed'),resolution:ref('OpsOccurrenceResolution'),providerReference:text(100),createdAt:instant,updatedAt:instant,missing:arr(text(100))},['id','reference','revisionId','draft','state','createdAt','updatedAt','missing']);
 d.OpsIncidentDescription=obj({description:text(8000)});
 d.OpsIncidentRevision=obj({id,incidentId:id,number:{type:'integer',minimum:1},draft:ref('OpsIncidentDraftWrite'),contentHash:hash,reason,authorLabel:text(),createdAt:instant});
 d.OpsIncidentSubjectOptions=obj({incidentId:id,revisionId:id,resolutionId:id,versionId:id,sourceHash:hash,vehicles:arr(obj({id,label:text(1000)}),0,1000),drivers:arr(obj({id,label:text(1000)}),0,1000),locations:arr(obj({id,label:text(1000)}),0,1000),occupations:arr(obj({id,label:text(1000)}),0,1000),coverCodes:arr(text(100))});
 d.OpsIncidentHandoff=obj({revisionId:id,resolutionId:id,providerId:id});
 d.OpsClaimsSummary=obj({id,incidentId:id,handoffId:id,asOf:instant,receivedAt:instant,status:en('notified','open','closed','rejected'),paid:{anyOf:[amount,{type:'null'}]},reserved:{anyOf:[amount,{type:'null'}]},currency:{const:'GBP'},providerReference:text(100)});
 d.OpsIncident.properties.administratorSummary=ref('OpsClaimsSummary');
 d.OpsClaimsProviderSummary=obj({providerReference:text(100),eventId:text(200),asOf:instant,status:en('notified','open','closed','rejected'),paid:{anyOf:[amount,{type:'null'}]},reserved:{anyOf:[amount,{type:'null'}]},currency:{const:'GBP'}});
 d.OpsClaimsSummary.properties.providerEventId=text(200);d.OpsClaimsSummary.required.push('providerEventId');
 d.OpsClaimsEvidence=obj({versionId:id,name:text(255),mediaType:en('application/pdf','image/png','image/jpeg'),length:{type:'integer',minimum:1,maximum:20971520},sha256:hash});
 d.OpsCommercialIncidentProjection=obj({format:{const:'commercial-incident-2'},policyId:id,versionId:id,sourceContentHash:hash,occurrence:ref('OpsOccurrence'),applicability:ref('OpsOccurrenceWindow'),knownAt:instant,subject:ref('OpsCommercialSubject')});
 d.OpsClaimsFacts={oneOf:d.OpsIncidentDraftWrite.oneOf.map(branch=>obj(Object.fromEntries(Object.entries(branch.properties).filter(([key])=>key!=='evidenceDocumentVersionIds')),branch.required))};
 d.OpsClaimsSnapshot=obj({format:{const:'claims-handoff-2'},incidentId:id,policyId:id,revisionId:id,revisionHash:hash,resolutionId:id,resolutionHash:hash,sourceVersionId:id,sourceHash:hash,administratorId:id,administratorName:text(),facts:ref('OpsClaimsFacts'),resolution:ref('OpsOccurrenceResolution'),commercial:{anyOf:[ref('OpsCommercialIncidentProjection'),{type:'null'}]},evidence:arr(ref('OpsClaimsEvidence'),0,20),withheldEvidenceCount:{type:'integer',minimum:0,maximum:20}});
 d.OpsClaimsHandoff=obj({id,incidentId:id,revisionId:id,resolutionId:id,sourceVersionId:id,administratorId:id,state:en('queued','acknowledged','rejected','failed','superseded'),outcomeCode:{anyOf:[text(100),{type:'null'}]},providerReference:{anyOf:[text(100),{type:'null'}]},createdAt:instant,completedAt:{anyOf:[instant,{type:'null'}]},submitted:ref('OpsClaimsSnapshot')});
 d.OpsClaimsRequest=obj({id,handoffId:id,purpose:en('handoff','refresh','contact'),jobId:id,state:text(30),errorCode:{anyOf:[text(100),{type:'null'}]},createdAt:instant,completedAt:{anyOf:[instant,{type:'null'}]},body:{anyOf:[text(8000),{type:'null'}]},retryAllowed:bool,etag,attempts:arr(obj({id,number:{type:'integer',minimum:1},startedAt:instant,endedAt:{anyOf:[instant,{type:'null'}]},outcome:text(100),errorCode:{anyOf:[text(100),{type:'null'}]}}),0,18)});
 d.OpsClaimsAdministrators=obj({items:arr(obj({id,label:text()}))});
 const generationSources=[
  {kind:en('statement-of-fact','policy-schedule','policy-certificate','endorsement','cancellation-notice'),source:obj({kind:{const:'policy-version'},policyVersionId:id})},
  {kind:{const:'quotation'},source:obj({kind:{const:'quote-revision'},quoteRevisionId:id,quoteTermsVersionId:id})},
  {kind:{const:'statement-of-fact'},source:obj({kind:{const:'quote-revision'},quoteRevisionId:id,quoteTermsVersionId:id},['kind','quoteRevisionId'])},
  {kind:en('quotation','statement-of-fact','renewal-invitation'),source:obj({kind:{const:'servicing-terms'},termsVersionId:id})}
 ];
 d.OpsDocumentGenerationChoice={oneOf:generationSources.map(source=>obj({...source,label:text(),templateVersionId:id,templateLabel:text()}))};
 d.OpsDocumentGenerationOptions=obj({productCode:en('motor-trade-road-risks','motor-trade-combined','commercial-combined'),sourceKind:en('policy-version','quote-revision','servicing-terms'),sourceVersionId:id,sourceLabel:text(),sourceDate:instant,items:arr(ref('OpsDocumentGenerationChoice'),0,200),nextCursor:text(2048)},['productCode','sourceKind','sourceVersionId','sourceLabel','sourceDate','items']);
 d.OpsDocumentGenerate={oneOf:generationSources.flatMap(source=>[
  obj({...source,templateVersionId:id,reason,documentId:id,visibility:en('internal','insurer')},['kind','source','templateVersionId','reason','visibility']),
  obj({...source,templateVersionId:id,reason,documentId:id,visibility:{const:'agency'},relationshipId:id},['kind','source','templateVersionId','reason','visibility','relationshipId'])
 ])};
 const uploadDocument={kind:en('evidence','quotation','statement-of-fact','policy-schedule','policy-certificate','endorsement','renewal-invitation','cancellation-notice'),uploadId:id,reason,documentId:id};
 d.OpsDocumentUpload={oneOf:[
  obj({...uploadDocument,visibility:en('internal','insurer')},['kind','uploadId','reason','visibility']),
  obj({...uploadDocument,visibility:{const:'agency'},relationshipId:id},['kind','uploadId','reason','visibility','relationshipId'])
 ]};
 d.OpsDocumentVersion=obj({id,documentId:id,number:{type:'integer',minimum:1},kind:text(60),state:en('pending','ready','failed','quarantined'),originalName:text(255),bytes:{type:'integer',minimum:0,maximum:20971520},contentType:en('application/pdf','image/png','image/jpeg'),sha256:hash,sourceVersionId:id,templateVersionId:id,createdAt:instant,withdrawnEffectiveAt:instant,sourceKind:en('policy-version','quote-revision','servicing-terms','upload'),sourceLabel:text(),sourceDate:instant,sourceHash:hash,templateHash:hash,termsHash:hash,quoteTermsVersionId:id,rendererVersion:text(100),projectionVersion:text(100),fontVersion:text(100),pageCount:{type:'integer',minimum:1,maximum:300}},['id','documentId','number','kind','state','originalName','bytes','contentType','createdAt']);
 d.OpsDocument=obj({id,subjectRecordId:id,kind:text(60),visibility:en('internal','agency','insurer'),relationshipId:id,currentVersionId:id,currentVersion:ref('OpsDocumentVersion')},['id','subjectRecordId','kind','visibility']);
 d.OpsTaskAttachment=obj({id,taskId:id,version:ref('OpsDocumentVersion'),authorLabel:text(),createdAt:instant,reason});
 d.OpsTaskAttachments=obj({items:arr(ref('OpsTaskAttachment'),0,20)});
 d.OpsTaskAttachmentWrite=obj({documentVersionId:id,reason});
 d.OpsTaskAttachmentRemove=obj({reason});
 d.OpsFileUpload=obj({id,subjectRecordId:id,name:text(255),mediaType:en('application/pdf','image/png','image/jpeg'),byteLength:{type:'integer',minimum:1,maximum:20971520},sha256:hash,state:en('pending','ready','quarantined'),createdAt:instant,verifiedAt:{anyOf:[instant,{type:'null'}]},failureCode:{anyOf:[text(100),{type:'null'}]}});
 d.OpsMidSubmission=obj({id,intentId:id,policyVersionId:id,jobId:id,state:en('pending','accepted','rejected','failed','superseded'),items:arr(obj({riskItemId:id,kind:en('vehicle','trade-plate'),registration:text(20),action:en('add','change','remove'),effectiveAt:instant}),1),reasonCodes:arr(text(100)),providerReference:text(100)},['id','intentId','policyVersionId','jobId','state','items','reasonCodes']);
 return d;
}
export function operationSchema(){return{$schema:'https://json-schema.org/draft/2020-12/schema',$id:'https://contracts.cover-mga.example/operations-1',title:'Closed operational API contracts; runtime authority remains mandatory',$defs:operationalDefinitions()};}
export function relocateOperational(value){if(Array.isArray(value))return value.map(relocateOperational);if(value&&typeof value==='object')return Object.fromEntries(Object.entries(value).map(([k,v])=>[k,k==='$ref'?v.replace('#/$defs/','#/components/schemas/'):relocateOperational(v)]));return value;}
function ts(s){if(s.$ref)return s.$ref.split('/').at(-1);if(s.const!==undefined)return JSON.stringify(s.const);if(s.enum)return s.enum.map(x=>JSON.stringify(x)).join(' | ');if(s.oneOf||s.anyOf)return(s.oneOf??s.anyOf).map(x=>'('+ts(x)+')').join(' | ');if(s.type==='object')return'{ '+Object.entries(s.properties).map(([k,v])=>`${JSON.stringify(k)}${s.required.includes(k)?'':'?'}: ${ts(v)}`).join('; ')+' }';if(s.type==='array')return`Array<${ts(s.items)}>`;return{integer:'number',number:'number',string:'string',boolean:'boolean',null:'null'}[s.type]??'never';}
export async function writeOperationalContracts(){const schema=operationSchema();await mkdir('contracts/schemas',{recursive:true});await mkdir('contracts/generated',{recursive:true});await writeFile('contracts/schemas/operations.schema.json',JSON.stringify(schema,null,2)+'\n');await writeFile('contracts/generated/operations.ts','// Generated by scripts/operations-contracts.mjs. Runtime validation and authorization are mandatory.\n'+Object.entries(schema.$defs).map(([k,v])=>`export type ${k} = ${ts(v)};`).join('\n')+'\n');}
