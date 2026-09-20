import {closed as o, uid as id, money, label as t, instant, hash, choice as e, bounded, many, conditionSchema} from './underwriting-contract-model.mjs';
import {readFileSync} from 'node:fs';

const date={type:'string',format:'date'};
const reason={...t(2000),minLength:10};
const signedMoney={type:'string',pattern:'^-?(0|[1-9][0-9]{0,12})\\.[0-9]{2}$'};
const kind=e('adjustment','renewal','cancellation');
const r=name=>({$ref:`#/$defs/${name}`});

// These contracts describe capture and immutable outputs separately. Schema
// validity never grants ownership, underwriting authority or permission to issue.
export function servicingDefinitions(quote) {
 const defs=structuredClone(quote.$defs);
 const payloads={};
 for(const [key,name] of Object.entries({driver:'Driver',vehicle:'Vehicle',premises:'Premises',business:'Business',cover:'Cover'})) {
  const payload=structuredClone(defs[name]);
  // The change envelope owns the stable target ID. Nested rows retain theirs.
  delete payload.properties.id;
  payload.required=(payload.required??[]).filter(x=>x!=='id');
  payloads[key]=payload;
 }
 payloads.policyholder=structuredClone(quote.properties.insured);
 defs.ServicingEffectiveIntent=o({localDate:date,localTime:{type:'string',pattern:'^(?:[01][0-9]|2[0-3]):[0-5][0-9]$'},timeZone:{const:'Europe/London'},utcOffsetMinutes:bounded(0,60)},['localDate','localTime','timeZone']);
 defs.ServicingEffectiveIntent.properties.utcOffsetMinutes={type:'integer',enum:[0,60]};
 defs.ServicingChange={oneOf:Object.entries(payloads).flatMap(([target,payload])=>{
  const common={changeId:id,riskItemId:id,kind:{const:target}};
  const effective=target==='cover'?{effectiveIntent:r('ServicingEffectiveIntent')}:{};
  const vehicle=target==='vehicle'?{specifiedVehicle:o({selected:{type:'boolean'},required:{type:'boolean'}})}:{};
  const write=o({...common,operation:e('add','update'),payload,payloadMode:{const:'replace'},...effective,...vehicle},[...Object.keys(common),'operation','payload']);
  write.allOf=[{if:{properties:{payloadMode:{const:'replace'}},required:['payloadMode']},then:{properties:{operation:{const:'update'}}}}];
  return [write,o({...common,operation:{const:'remove'},...effective,...vehicle},[...Object.keys(common),'operation'])];
 })};
 const commercial=JSON.parse(readFileSync(new URL('../contracts/schemas/commercial-combined.schema.json',import.meta.url),'utf8'));
 const prefix='CommercialServicing';
 function scoped(value) {
  if(Array.isArray(value))return value.map(scoped);
  if(!value||typeof value!=='object')return value;
  return Object.fromEntries(Object.entries(value).map(([key,item])=>[key,key==='$ref'&&item.startsWith('#/$defs/')?`#/$defs/${prefix}${item.slice(8)}`:scoped(item)]));
 }
 for(const [name,value] of Object.entries(commercial.$defs))defs[prefix+name]=scoped(value);
 const commercialPayloads=Object.fromEntries(Object.entries({location:'Location',business:'Business',bi:'BusinessInterruption',liability:'Liability',wage:'Wage',loss:'Loss',cover:'Cover',insured:'Insured'}).map(([kind,name])=>{
  const payload=structuredClone(defs[prefix+name]);delete payload.properties.id;payload.required=(payload.required??[]).filter(x=>x!=='id');return [kind,payload];
 }));
 commercialPayloads.property=o(Object.fromEntries(['buildings','contents','stock','maximumEstimatedLoss'].map(key=>[key,commercial.$defs.Location.properties[key]])),[]);
 commercialPayloads.declarations=o({declarations:r(prefix+'Declarations'),materialFacts:commercial.$defs.Risk.properties.materialFacts},[]);
 defs.ServicingChange.oneOf.push(...Object.entries(commercialPayloads).flatMap(([target,payload])=>{
  const common={changeId:id,riskItemId:id,kind:{const:'commercial-'+target}};
  const row=['location','wage','loss'].includes(target),effective=target==='cover'?{effectiveIntent:r('ServicingEffectiveIntent')}:{};
  const write=o({...common,operation:row?e('add','update'):{const:'update'},payload,payloadMode:{const:'replace'},...effective},[...Object.keys(common),'operation','payload']);
  write.allOf=[{if:{properties:{payloadMode:{const:'replace'}},required:['payloadMode']},then:{properties:{operation:{const:'update'}}}}];
  return row?[write,o({...common,operation:{const:'remove'}},[...Object.keys(common),'operation'])]:[write];
 }));
 defs.ServicingProposal=o({schemaVersion:{const:'1.0'},baseVersionId:id,reason,requestedBy:{oneOf:[o({kind:{const:'internal'}}),o({kind:e('insured','broker'),name:t(200)})]},commonEffectiveIntent:r('ServicingEffectiveIntent'),changes:many(r('ServicingChange'),100)});
 defs.ServicingProposal.properties.dateBasis=e('shared','per-cover-change');
 defs.ServicingProposal.properties.cancellationReasonCode=e('insured-request','non-payment','non-disclosure','trade-ceased','insurer-instruction');
 defs.ServicingLeaseAcquire={oneOf:[o({mode:{const:'acquire'}}),o({mode:{const:'takeover'},reason})]};
 defs.ServicingLease=o({id,holderId:id,generation:bounded(1,2147483647),leaseToken:t(256),expiresAt:instant});
 defs.ServicingCancellationApproval=o({previewId:id,previewHash:hash,reason});
 defs.ServicingExperience=o({observationStartsOn:date,observationEndsOn:date,claimCount:bounded(0,100000),paid:money,outstanding:money,earnedPremium:money,sourceCode:e('insured','agency','administrator'),sourceReference:t(200),evidenceAssociationId:id});
 defs.ServicingAcceptance=o({termsId:id,deliveryId:id,accepter:t(200),receivedAt:instant,channel:e('email','telephone','written'),evidenceAssociationId:id});
 defs.ServicingDecision=o({cycleId:id,ratingId:id,outcome:e('approve','conditional','query','decline','reopen'),reason,conditions:many(conditionSchema,20)},['cycleId','ratingId','outcome','reason']);
 defs.ServicingSelectedDecision=o({...defs.ServicingDecision.properties,selected:many(o({id,etag:t(200)}),50,1)},[...defs.ServicingDecision.required,'selected']);
 defs.ServicingEvidence=o({fileVersionId:id,cycleId:id,purposeCode:t(60),riskItemId:id},['fileVersionId','cycleId','purposeCode']);
 defs.ServicingEvidenceReview=o({cycleId:id,outcome:e('accepted','rejected'),reason});
 defs.ServicingReason=o({reason});
 defs.ServicingCapacity=o({cycleId:id,referralId:id,dimension:e('annual-premium','stock','vehicle','cover'),riskItemId:id},['cycleId','referralId','dimension']);
 defs.ServicingCapacitySubmission=o({reason,message:t(10000),evidenceAssociationIds:many(id,20)});
 defs.ServicingCapacityResponse=o({submissionId:id,outcome:e('approved','conditional','declined','information-required'),message:t(10000),receivedAt:instant,evidenceAssociationIds:many(id,20),conditions:many(conditionSchema,20)});
 defs.ServicingCapacityAction={oneOf:[o({action:e('withdraw','reopen'),reason}),o({action:{const:'assign'},reason,assignedUserId:id})]};
 defs.ServicingConditionResolution=o({responseId:id,evidenceAssociationIds:many(id,20,1),reason});
 defs.ServicingTermsWrite=o({cycleId:id,ratingId:id});
 defs.ServicingTerms=o({id,draftId:id,revisionId:id,baseVersionId:id,cycleId:id,ratingId:id,termsHash:hash,inputHash:hash,templateVersionId:id,agencyTermsVersionId:id,preparedAt:instant,preparedBy:id,schedule:many(r('ServicingScheduleSlice'),100,1),movements:many(r('ServicingMovement'),100),currency:{const:'GBP'}});
 defs.ServicingDelivery=o({recipientContactId:id,proofAssociationIds:many(id,20)});
 defs.ServicingPreviewWrite=o({previewHash:hash});
 defs.ServicingScheduleSlice=o({effectiveAt:instant,ordinal:bounded(0,99),changeIds:many(id,100,1)});
 defs.ServicingMovement=o({originalComponentId:id,coverageStartsOn:date,coverageEndsOn:date,premium:signedMoney,tax:signedMoney,commission:signedMoney,fee:signedMoney,netDue:signedMoney});
 defs.ServicingCancellationPreview=o({draftId:id,revisionId:id,baseVersionId:id,ruleVersion:t(100),previewHash:hash,effectiveAt:instant,movements:many(r('ServicingMovement'),1000),netCredit:money,blockers:many(t(100),100)});
 defs.ServicingIssueResult=o({policyId:id,policyReference:t(40),draftId:id,draftEtag:{type:'string',pattern:'^"[A-Za-z0-9+/]{11}="$'},termId:id,transactionId:id,decisionId:id,
  versionId:id,versionIds:many(id,100,1),obligationId:id,journalId:id,accountingPeriodId:id,postingDate:date,currency:{const:'GBP'},amountDue:money,amountCredit:money,netAmount:signedMoney,
  documentRequestIds:many(id,300,2),midIntentIds:many(id,100,0),processedAt:instant});
 defs.ServicingIssueWrite=o({cycleId:id,ratingId:id,termsVersionId:id,acceptanceId:id,termsHash:hash,assuranceHash:hash,reason:{...t(1000),minLength:10}});
 defs.ServicingIssueDecision={oneOf:[
  o({id,draftId:id,policyId:id,baseTermId:id,baseVersionId:id,revisionId:id,kind:e('adjustment','renewal'),cycleId:id,ratingId:id,termsId:id,acceptanceId:id,effectiveAt:instant,inputHash:hash,createdAt:instant,createdBy:id}),
  o({id,draftId:id,policyId:id,baseTermId:id,baseVersionId:id,revisionId:id,kind:{const:'cancellation'},cancellationPreviewId:id,cancellationApprovalId:id,effectiveAt:instant,inputHash:hash,createdAt:instant,createdBy:id}),
 ]};
 defs.ServicingDraftCreate=o({kind,baseVersionId:id,commonEffectiveIntent:r('ServicingEffectiveIntent'),reason});
 defs.ServicingDraft=o({id,policyId:id,baseTermId:id,baseVersionId:id,revisionId:id,kind,state:e('draft','rating','referral','quoted','accepted','issued','abandoned','lapsed'),proposal:r('ServicingProposal'),createdAt:instant,updatedAt:instant});
 defs.ServicingDraft.properties.lease={anyOf:[o({...defs.ServicingLease.properties,active:{type:'boolean'}}),{type:'null'}]};
 defs.ServicingDraft.required.push('lease');
 // Optional for retained pre-upgrade command receipts; current reads include it.
 defs.ServicingDraft.properties.context=o({policyReference:t(40),preparedBy:o({id,label:t(200)})});
 defs.ServicingDraft.properties.context.properties.baseTermPremium=money;
 defs.ServicingDraft.properties.context.properties.productCode=e('motor-trade-road-risks','motor-trade-combined','commercial-combined');
 const capture=structuredClone(quote); delete capture.$schema; delete capture.$id; delete capture.$defs;
 const commercialCapture=scoped(commercial);delete commercialCapture.$schema;delete commercialCapture.$id;delete commercialCapture.$defs;
 defs.ServicingEditorCapture={oneOf:[capture,commercialCapture]};
 const side=o({path:{type:'string',maxLength:2000},json:{type:'string',maxLength:2097152}});
 defs.ServicingEditorChange={oneOf:[
  o({kind:{const:'added'},path:t(2000),itemId:id,after:side},['kind','path','after']),
  o({kind:{const:'removed'},path:t(2000),itemId:id,before:side},['kind','path','before']),
  o({kind:{const:'changed'},path:t(2000),itemId:id,before:side,after:side},['kind','path','before','after'])
 ]};
 defs.ServicingEditor=o({draftId:id,revisionId:id,clientId:id,
  captureVersions:o({schemaVersion:t(50),questionSetVersion:t(100),referenceDataVersion:t(100)}),
  assessment:o({base:r('ServicingEditorCapture'),proposed:r('ServicingEditorCapture'),changes:many(r('ServicingEditorChange'),100000),
   readinessIssues:many(o({code:t(200),path:{type:'string',maxLength:2000},questionId:{anyOf:[t(200),{type:'null'}]}}),100),
   slices:many(o({effectiveAt:instant,proposed:r('ServicingEditorCapture'),changeIds:many(id,100)}),100)})});
 defs.ServicingDraftList=o({termId:id,policyId:id,items:many(o({id,kind,state:e('draft','abandoned','issued','lapsed'),currentRevisionId:id,baseVersionId:id,updatedAt:instant}),10000)});
 return defs;
}

export function servicingSchema(quote) {
 return {$schema:'https://json-schema.org/draft/2020-12/schema',$id:'https://schemas.cover-mga.example/servicing/1.0',title:'Closed servicing capture and lifecycle contracts',...r('ServicingProposal'),$defs:servicingDefinitions(quote)};
}
