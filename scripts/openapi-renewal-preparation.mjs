export function addRenewalPreparation({schemas,ref,route,paths}) {
 const id={type:'string',format:'uuid'},instant={type:'string',format:'date-time'},date={type:'string',format:'date'};
 const text=(maxLength=2000,minLength=1)=>({type:'string',minLength,maxLength});
 const object=(properties,required=Object.keys(properties))=>({type:'object',additionalProperties:false,properties,required});
 const nullable=schema=>({anyOf:[schema,{type:'null'}]});
 const offset={type:'integer',enum:[0,60]},months={type:'integer',minimum:1,maximum:12},etag={type:'string',minLength:14,maxLength:14};
 const clock={type:'string',pattern:'^(?:[01][0-9]|2[0-3]):[0-5][0-9]$'};
 const intent={timeZone:{const:'Europe/London'},localStartDate:date,localStartTime:clock,utcOffsetMinutes:offset,endUtcOffsetMinutes:offset};
 schemas.RenewalTermIntent={oneOf:[
  object({kind:{const:'annual'},...intent},['kind','timeZone','localStartDate','localStartTime','utcOffsetMinutes']),
  object({kind:{const:'short-period'},...intent,localEndDate:date,localEndTime:clock},['kind','timeZone','localStartDate','localStartTime','utcOffsetMinutes','localEndDate','localEndTime']),
 ]};
 schemas.RenewalPreparationRequest=object({termMonths:months,endUtcOffsetMinutes:offset},['termMonths']);
 schemas.RenewalPreparationReceipt=object({draftId:id,resourceId:id,kind:{const:'renewal'},updatedAt:instant});
 schemas.RenewalExperienceReviewRequest=object({outcome:{enum:['accepted','rejected']},reason:text(2000,10)});
 schemas.RenewalExperienceRecord=object({...schemas.ServicingExperience.properties,id,sequence:{type:'integer',minimum:1},recordedAt:instant,recordedBy:id});
 schemas.RenewalExperienceReview=object({id,experienceVersionId:id,outcome:{enum:['accepted','rejected']},reason:text(2000,10),authorityVersionId:id,authorityGrantId:id,recordedAt:instant,recordedBy:id});
 schemas.RenewalExperienceView=object({draftId:id,evidenceFileId:nullable(id),experience:nullable(ref('RenewalExperienceRecord')),review:nullable(ref('RenewalExperienceReview'))});
 schemas.RenewalPreparationPreview=object({policyId:id,expiringTermId:id,baseVersionId:id,termEtag:etag,
  term:object({kind:{enum:['annual','short-period']},startsAt:instant,endsAt:instant,timeZone:{const:'Europe/London'}}),termIntent:ref('RenewalTermIntent'),
  productVersionId:id,binderVersionId:id,agencyTermsVersionId:id,ruleSettingVersionId:id,ruleVersion:text(60),fairValueAssessmentId:nullable(id),
  fairValueEvidenceFileId:nullable(id),fairValueSatisfied:{type:'boolean'},fairValueState:{enum:['pass','refer','fail','unavailable']},brokerArrearsState:{const:'unavailable'}});
 schemas.RenewalPreparationSelection=object({id,sequence:{type:'integer',minimum:1},termMonths:months,
  term:structuredClone(schemas.RenewalPreparationPreview.properties.term),productVersionId:id,binderVersionId:id,agencyTermsVersionId:id,
  ruleSettingVersionId:id,fairValueAssessmentId:nullable(id),preparedAt:instant});
 schemas.RenewalPreparationWorkspace=object({draftId:id,draftEtag:etag,assessedAt:instant,preparation:nullable(ref('RenewalPreparationSelection')),
  eligibility:nullable(ref('RenewalPreparationPreview')),allowedTermMonths:{type:'array',items:months,maxItems:12,uniqueItems:true},
  defaultTermMonths:nullable(months),expiringAnnualPremium:{type:'string',pattern:'^(0|[1-9][0-9]{0,12})\\.[0-9]{2}$'},
  expiringStartsAt:instant,expiringEndsAt:instant,current:{type:'boolean'},blockers:{type:'array',items:text(100),maxItems:100}});
 const root='/drafts/{draftId}/renewal';
 // Replace the planning-only experience spelling with the implemented owned resource.
 delete paths['/drafts/{draftId}/experience'];
 for(const [method,path,name,permission,input,output,status] of [
  ['get','/terms/{termId}/renewal-preview','previewRenewalPreparation','policy-read',undefined,'RenewalPreparationPreview',200],
  ['get',`${root}/experience`,'readRenewalExperience','policy-read',undefined,'RenewalExperienceView',200],
  ['get',`${root}/preparation`,'readRenewalPreparation','policy-read',undefined,'RenewalPreparationWorkspace',200],
  ['post',`${root}/preparation`,'prepareRenewal','quote-rate','RenewalPreparationRequest','RenewalPreparationReceipt',201],
  ['post',`${root}/experience/uploads`,'uploadRenewalExperienceEvidence','underwriting-evidence-write',undefined,'RenewalPreparationReceipt',201],
  ['put',`${root}/experience`,'recordRenewalExperience','underwriting-evidence-write','ServicingExperience','RenewalPreparationReceipt',201],
  ['post',`${root}/experience/{experienceId}/reviews`,'reviewRenewalExperience','underwriting-evidence-review','RenewalExperienceReviewRequest','RenewalPreparationReceipt',201],
 ]) {
  route(method,path,name,permission,input,output,{status});const op=paths[path][method];op.responses=structuredClone(op.responses);
  op['x-runtime-status']=method==='get'?'phase-7-11-read-implemented':'phase-7-11-command-implemented';
  op.description='Persistent scoped renewal preparation and supplied experience. Current identity, policy permission and review grants are checked before receipt replay. New commands require a strong draft If-Match, holder-bound X-Edit-Lease and Idempotency-Key. Changes invalidate existing rating authority. Unknown experience remains absent; reviews approve exact immutable supplied versions. Responses are no-store.';
  op.responses[status].headers.ETag={description:method==='get'?'Strong current term or draft version.':'Strong updated draft version.',schema:etag};
  delete op.responses[status].headers.Location;
  if(name==='previewRenewalPreparation')op.parameters.push({name:'termMonths',in:'query',required:false,schema:months},{name:'endUtcOffsetMinutes',in:'query',required:false,schema:offset});
  if(name==='uploadRenewalExperienceEvidence')op.requestBody=structuredClone(paths['/drafts/{draftId}/evidence/uploads'].post.requestBody);
 }
}
