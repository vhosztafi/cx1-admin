export function addServicingSubmissions({schemas,ref,route,paths}) {
 const id={type:'string',format:'uuid'},etag={type:'string',minLength:14,maxLength:14};
 const object=properties=>({type:'object',additionalProperties:false,properties,required:Object.keys(properties)});
 const nullable=value=>({anyOf:[value,{type:'null'}]});
 const reason={type:'string',minLength:10,maxLength:2000,pattern:'\\S'};
 schemas.ServicingSubmissionRequest=object({cycleId:id,revisionId:id,reason});
 schemas.ServicingSubmissionReceipt=object({id,draftId:id,revisionId:id,cycleId:id,draftEtag:etag});
 schemas.ServicingSubmissionView=object({id,cycleId:id,revisionId:id,ratingId:id,inputHash:{type:'string',pattern:'^[a-f0-9]{64}$'},reason,
  submittedBy:id,submittedAt:{type:'string',format:'date-time'},applicable:{type:'boolean'}});
 schemas.ServicingSubmissionPage=object({draftId:id,draftEtag:etag,assessedAt:{type:'string',format:'date-time'},currentCycleId:nullable(id),
  current:nullable(ref('ServicingSubmissionView')),items:{type:'array',maxItems:50,items:ref('ServicingSubmissionView')},nextCursor:nullable({type:'string',minLength:1,maxLength:2048})});
 const root='/drafts/{draftId}';
 route('post',`${root}/submit`,'submitPolicyDraft','policy-draft-write','ServicingSubmissionRequest','ServicingSubmissionReceipt',{status:201});
 const command=paths[`${root}/submit`].post;command['x-runtime-status']='phase-7-06-command-implemented';command.responses=structuredClone(command.responses);
 delete command.responses[201].headers.Location;command.responses[201].headers.ETag={description:'Strong updated draft command version.',schema:etag};
 command.description='Persist one immutable underwriting handoff for the current owned draft revision and unexpired rating. Requires current policy-draft-write scope, editing lease, CSRF, strong If-Match and Idempotency-Key. Current scope and lease are checked before exact receipt replay. A different key cannot submit the same cycle twice. Outstanding proof remains independently blocking approval and issue; this command does not create approval or external delivery. Response is no-store.';
 route('get',`${root}/submissions`,'listServicingSubmissions','policy-read',undefined,'ServicingSubmissionPage');
 const read=paths[`${root}/submissions`].get;read['x-runtime-status']='phase-7-06-read-implemented';read.responses=structuredClone(read.responses);
 for(const response of Object.values(read.responses))delete response.headers?.ETag;
 read.parameters=read.parameters.filter(p=>p.in!=='query');read.parameters.push(
  {name:'pageSize',in:'query',schema:{type:'integer',minimum:1,maximum:50,default:25}},
  {name:'cursor',in:'query',schema:{type:'string',minLength:1,maxLength:2048}});
 read.description='Current scoped policy-read access is required for no-store handoff history. Current submission is returned separately from history pages. Applicable means matching current valid rating, not underwriting approval. Signed submitted-time/ID keyset cursors bind actor, route, page size and draft ETag for 15 minutes. Historical submissions remain after rerating or abandonment.';
}
