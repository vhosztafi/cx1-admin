export function addServicingCapacity({schemas:s,ref:r,route,paths}) {
 const id={type:'string',format:'uuid'},instant={type:'string',format:'date-time'},etag={type:'string',minLength:14,maxLength:14};
 const text=(maxLength=2000,minLength=1)=>({type:'string',minLength,maxLength});
 const e=(...values)=>({type:'string',enum:values}),o=properties=>({type:'object',additionalProperties:false,properties,required:Object.keys(properties)});
 const n=value=>({anyOf:[value,{type:'null'}]}),a=(items,maxItems=50,minItems=0)=>({type:'array',items,minItems,maxItems});
 const reason={...text(2000,10),pattern:'\\S'},body={...text(10000),pattern:'\\S'},seq={type:'integer',minimum:1},bool={type:'boolean'};
 const base={cycleId:id,caseEtag:etag},cursor=n(text(2048));
 s.ServicingCapacityCreateRequest=o({cycleId:id,referralId:id,referralEtag:etag,reason});
 s.ServicingCapacitySubmitRequest=o({...base,body,reason,evidenceAssociationIds:{...a(id,20),uniqueItems:true},scenarioVersionId:id});
 s.ServicingCapacityReplyRequest=o({...s.ServicingCapacitySubmitRequest.properties,responseId:id});
 s.ServicingCapacityChaseRequest=o({...base,submissionId:id,body,reason});
 s.ServicingCapacityActionRequest=o({...base,action:e('withdraw','reopen'),reason});
 s.ServicingCapacityAssignRequest=o({...base,assignedUserId:id,reason});
 s.ServicingDatedCarrierCondition=o({definition:r('UnderwritingConditionWrite'),effectiveDates:{...a(instant,100,1),uniqueItems:true}});
 s.ServicingCarrierResponseDefinition={oneOf:['approve','approve-with-conditions','query','decline'].map(outcome=>{
  const approval=outcome.startsWith('approve'),conditional=outcome==='approve-with-conditions';
  return o({outcome:{const:outcome},validFrom:approval?instant:{type:'null'},validTo:approval?instant:{type:'null'},
   authorisedLimits:a(r('UnderwritingCapacityExtension'),approval?20:0,approval?1:0),conditions:a(r('ServicingDatedCarrierCondition'),conditional?20:0,conditional?1:0)});
 })};
 s.ServicingCapacityResponseRequest=o({...base,submissionId:id,evidenceAssociationId:id,definition:r('ServicingCarrierResponseDefinition'),body,
  providerUnderwriter:text(200),providerReference:text(100),receivedAt:instant,reason});
 s.ServicingCapacityReceipt=o({id,draftId:id,revisionId:id,cycleId:id,draftEtag:etag});
 s.ServicingCapacityQueuedReceipt=o({...s.ServicingCapacityReceipt.properties,submissionId:id,jobId:id,state:{const:'queued'}});
 s.ServicingCapacitySummary=o({id,draftId:id,cycleId:id,revisionId:id,ratingId:id,referralId:id,providerId:id,providerLabel:text(),ruleCode:text(),dimension:text(),riskItemId:n(id),
  state:e('draft','queued','sent','queried','approved','conditional','declined','failed','superseded'),etag,reason,raisedAt:instant,currentSubmissionId:n(id),currentResponseId:n(id),assignedUserId:n(id)});
 s.ServicingCapacityOption=o({id,label:text()});
 s.ServicingCapacitySubmissionView=o({id,sequence:seq,body,reason,contextHash:{type:'string',pattern:'^[a-f0-9]{64}$'},workId:id,jobState:text(),attempts:{type:'integer',minimum:0},
  errorCode:n(text()),scenarioVersionId:id,submittedAt:instant,responseDueAt:instant,evidenceIds:a(id,20)});
 s.ServicingCapacityMessageView=o({id,sequence:seq,submissionId:id,kind:e('submission','query-reply','chase'),body,recordedBy:id,recordedByLabel:text(),recordedAt:instant});
 s.ServicingCapacityResponseView=o({id,sequence:seq,submissionId:id,provenance:e('supplied-response','demo-provider'),outcome:e('approve','approve-with-conditions','query','decline'),body,
  providerUnderwriter:text(200),providerReference:text(100),applicationState:e('applied','superseded'),evidenceAssociationId:n(id),evidenceReviewId:n(id),receivedAt:instant,recordedAt:instant,definition:r('ServicingCarrierResponseDefinition')});
 s.ServicingCarrierConditionView=o({id,sequence:seq,code:text(),kind:text(),etag,wording:text(),definition:r('UnderwritingConditionWrite'),effectiveDates:a(instant,100,1),satisfied:bool});
 s.ServicingCarrierResolutionView=o({id,sequence:seq,associationId:id,reviewId:id,outcome:e('satisfied','rejected'),reason,actorId:id,authorityVersionId:id,grantId:id,recordedAt:instant});
 s.ServicingCapacityDetail=o({case:r('ServicingCapacitySummary'),policyId:id,draftEtag:etag,current:bool,canWrite:bool,ready:bool,blockers:a(text(),100),
  submission:n(r('ServicingCapacitySubmissionView')),response:n(r('ServicingCapacityResponseView')),conditions:a(r('ServicingCarrierConditionView'),20),
  scenarios:a(r('ServicingCapacityOption')),seniors:a(r('ServicingCapacityOption')),similarCases:a(r('ServicingCapacitySummary'),10)});
 s.ServicingCapacityPage=o({draftId:id,policyId:id,draftEtag:etag,items:a(r('ServicingCapacitySummary')),nextCursor:cursor});
 for(const [name,item] of [['Submissions','ServicingCapacitySubmissionView'],['Messages','ServicingCapacityMessageView'],['Responses','ServicingCapacityResponseView'],['Resolutions','ServicingCarrierResolutionView']])
  s[`ServicingCapacity${name}Page`]=o({draftEtag:etag,items:a(r(item)),nextCursor:cursor});
 const root='/drafts/{draftId}',cases=`${root}/capacity`,one=`${cases}/{caseId}`;
 delete paths[`${one}/conditions/{conditionId}/resolve`];
 for(const [path,name,permission,input,status,output='ServicingCapacityReceipt'] of [
  [cases,'createServicingCapacity','underwriting-escalate','ServicingCapacityCreateRequest',201],
  [`${one}/submissions`,'submitServicingCapacity','underwriting-escalate','ServicingCapacitySubmitRequest',202,'ServicingCapacityQueuedReceipt'],
  [`${one}/query-replies`,'replyServicingCapacity','underwriting-escalate','ServicingCapacityReplyRequest',202,'ServicingCapacityQueuedReceipt'],
  [`${one}/chases`,'chaseServicingCapacity','underwriting-escalate','ServicingCapacityChaseRequest',201],
  [`${one}/assignment`,'assignServicingCapacity','underwriting-escalate','ServicingCapacityAssignRequest',200],
  [`${one}/actions`,'recordServicingCapacityAction','underwriting-escalate','ServicingCapacityActionRequest',200],
  [`${one}/responses`,'recordServicingCapacityResponse','underwriting-record-capacity','ServicingCapacityResponseRequest',201],
  [`${root}/capacity-conditions/{conditionId}/resolutions`,'resolveServicingCapacityCondition','underwriting-decide-within-authority','ServicingResolveProofRequest',200],
 ]) {
  route('post',path,name,permission,input,output,{status});const op=paths[path].post;op['x-runtime-status']='phase-7-07-command-implemented';op.responses=structuredClone(op.responses);
  op.responses[status].headers.ETag={description:'Strong updated parent draft command version.',schema:etag};
  if(path!==cases)delete op.responses[status].headers.Location;
  op.description='Persistent servicing carrier command. Requires current owned cycle, effective authority, provider, editing lease, CSRF, strong parent and child versions and Idempotency-Key. Current authority and proof are rechecked before exact receipt replay. Replies create a new immutable submission; responses bind exact submitted context and accepted response proof. Conditions require separately reviewed current-purpose evidence and explicit resolution. Assignment conveys no authority. Withdraw/reopen retain history while removing applicability. All responses are no-store; no real provider delivery.';
 }
 for(const [path,name,output,paged] of [
  [cases,'listServicingCapacity','ServicingCapacityPage',true],
  [one,'getServicingCapacity','ServicingCapacityDetail',false],
  [`${one}/submissions`,'listServicingCapacitySubmissions','ServicingCapacitySubmissionsPage',true],
  [`${one}/messages`,'listServicingCapacityMessages','ServicingCapacityMessagesPage',true],
  [`${one}/responses`,'listServicingCapacityResponses','ServicingCapacityResponsesPage',true],
  [`${one}/conditions/{conditionId}/resolutions`,'listServicingCarrierResolutions','ServicingCapacityResolutionsPage',true],
 ]) {
  route('get',path,name,'policy-read',undefined,output);const op=paths[path].get;op['x-runtime-status']='phase-7-07-read-implemented';op.responses=structuredClone(op.responses);
  for(const response of Object.values(op.responses))delete response.headers?.ETag;
  op.parameters=op.parameters.filter(p=>p.in!=='query');
  if(paged)op.parameters.push({name:'pageSize',in:'query',schema:{type:'integer',minimum:1,maximum:50,default:25}},{name:'cursor',in:'query',schema:text(2048)});
  op.description='Current scoped policy-read access is required. No-store case details and immutable retained history; no file bytes. Signed cursors bind actor, route, page size and parent draft version for 15 minutes. Similar cases are limited to this policy/provider/binder/rule and never grant authority. Ready describes current carrier extent and condition proof only, not overall issue eligibility. Historical responses remain visible after withdrawal, reopening or rerating.';
 }
}
