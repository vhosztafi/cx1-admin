export function addRenewalLifecycle({schemas,ref,route,paths}) {
 const id={type:'string',format:'uuid'},instant={type:'string',format:'date-time'},etag={type:'string',pattern:'^"[A-Za-z0-9+/]{11}="$'};
 const object=properties=>({type:'object',additionalProperties:false,properties,required:Object.keys(properties)});
 const nullable=schema=>({anyOf:[schema,{type:'null'}]});
 schemas.RenewalLifecycleView=object({policyId:id,termId:id,etag,ruleSettingVersionId:id,
  timeline:object({invitationDueAt:instant,expiringEnd:instant,renewalInception:instant,autoLapseAt:instant}),
  state:{enum:['not-due','due','overdue','invited','accepted','issued','lapsed','cancelled']},canLapse:{type:'boolean'},
  lapseEventId:nullable(id),lapseReason:nullable({type:'string',minLength:10,maxLength:1000}),lapseMode:nullable({enum:['manual','automatic']}),
  recordedAt:nullable(instant),notificationState:nullable({enum:['pending','leased','succeeded','failed']}),notificationAttempts:{type:'array',maxItems:18,
   items:object({number:{type:'integer',minimum:1,maximum:18},startedAt:instant,endedAt:nullable(instant),outcome:{type:'string',maxLength:40},errorCode:nullable({type:'string',maxLength:100})})}});
 schemas.RenewalLapseRequest=object({reason:{type:'string',minLength:10,maxLength:1000}});
 schemas.RenewalLapseReceipt=object({id,policyId:id,termId:id,termEtag:etag,effectiveAt:instant,recordedAt:instant,mode:{enum:['manual','automatic']},notificationId:id});
 for(const [method,path,name,input,output,status] of [
  ['get','/terms/{termId}/renewal-lifecycle','readRenewalLifecycle',undefined,'RenewalLifecycleView',200],
  ['post','/terms/{termId}/lapse','lapseRenewal','RenewalLapseRequest','RenewalLapseReceipt',201],
 ]) {
  route(method,path,name,method==='get'?'policy-read':'policy-draft-write',input,output,{status,lease:false});
  const operation=paths[path][method];operation['x-runtime-status']='phase-7-12-implemented';
  operation.description='Current policy scope and capability are checked before receipt replay. Retain one immutable lapse per expiring term, with a reason and persistent demo notification. Accepted or issued renewals cannot lapse. Effective lapse is the original expiry and never truncates existing cover or creates a new term. A strong term If-Match and Idempotency-Key protect fresh commands; durable term deduplication survives receipt expiry. Responses are no-store.';
  operation.responses=structuredClone(operation.responses);
  operation.responses[status].headers.ETag={description:'Strong current term ETag.',schema:etag};
  if(method==='post')operation.responses[200]={...structuredClone(operation.responses[201]),description:'The existing immutable lapse, without another event or notification.'};
 }
}
