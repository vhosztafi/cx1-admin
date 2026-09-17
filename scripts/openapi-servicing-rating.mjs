// Runtime servicing rating DTOs are distinct from quote rating and issued premiums.
export function addServicingRatingContracts({schemas,ref,route,paths}) {
 const id={type:'string',format:'uuid'}, instant={type:'string',format:'date-time'}, flag={type:'boolean'};
 const text=(maxLength=2000)=>({type:'string',maxLength});
 const enumeration=(...values)=>({type:'string',enum:values});
 const nullable=value=>({anyOf:[value,{type:'null'}]});
 const array=(items,maxItems=1000)=>({type:'array',items,maxItems});
 const object=properties=>({type:'object',additionalProperties:false,properties,required:Object.keys(properties)});
 const count={type:'integer',minimum:0}, sequence={type:'integer',minimum:1};
 const money={type:'string',pattern:'^-?(0|[1-9][0-9]{0,12})\\.[0-9]{2}$'};
 const hash={type:'string',pattern:'^[a-f0-9]{64}$'}, etag={type:'string',minLength:14,maxLength:14};
 schemas.ServicingRateRequest=object({revisionId:id,reason:{type:'string',minLength:10,maxLength:2000}});
 schemas.ServicingRateReceipt=object({id,draftId:id,revisionId:id,jobId:id,state:{const:'queued'},draftEtag:etag});
 schemas.ServicingRatingSlice=object({effectiveAt:instant,coverageEndsAt:instant,changeIds:array(id),annualPremium:money,annualDelta:money,remainingDays:count,annualDays:sequence,premium:money,tax:money,brokerCommission:money});
 schemas.ServicingRatingResult=object({id,outcome:enumeration('rated','rejected'),completedAt:instant,expiresAt:instant,resultHash:hash,currency:{const:'GBP'},baseAnnualPremium:money,premium:money,tax:money,brokerCommission:money,fee:money,grossPayable:money,netDue:money,detailsAvailable:flag,changeIds:array(id),slices:array(ref('ServicingRatingSlice'))});
 schemas.ServicingRatingCycle=object({id,sequence,revisionId:id,baseVersionId:id,requestedAt:instant,state:enumeration('rating-pending','rated','failed','expired','stale','superseded'),storedState:enumeration('rating-pending','rated','failed','superseded'),isCurrent:flag,applicable:flag,workId:id,jobState:enumeration('pending','leased','succeeded','failed'),attempts:count,attemptLimit:{type:'integer',enum:[6,12,18]},errorCode:nullable(text(100)),jobEtag:etag,nextAttemptAt:nullable(instant),inputHash:hash,ruleVersionId:id,agencyTermsVersionId:id,servicingSettingVersionId:nullable(id),supersededAt:nullable(instant),supersededReason:nullable(text()),result:nullable(ref('ServicingRatingResult'))});
 schemas.ServicingRatingHistory=object({draftId:id,revisionId:nullable(id),draftState:enumeration('draft','abandoned','issued'),draftEtag:etag,assessedAt:instant,currentCycleId:nullable(id),current:nullable(ref('ServicingRatingCycle')),items:array(ref('ServicingRatingCycle'),50),nextCursor:nullable(text(2048)),blockers:array(text(100))});
 const root='/drafts/{draftId}';
 route('post',`${root}/rate`,'ratePolicyDraft','policy-draft-rate','ServicingRateRequest','ServicingRateReceipt',{status:202});
 route('get',`${root}/ratings`,'listServicingRatings','policy-read',undefined,'ServicingRatingHistory');
 const rate=paths[`${root}/rate`].post, history=paths[`${root}/ratings`].get;
 for(const op of [rate,history]) {op['x-runtime-status']='phase-7-05-implemented';op.responses=structuredClone(op.responses);}
 rate.description='Queue an immutable rating of the exact saved adjustment revision and cumulative effective slices. Requires current policy scope, strong draft ETag, holder-bound editing lease and idempotency key. Current grants are checked before replay. One configured adjustment fee is charged. Rating does not grant underwriting, acceptance or issue authority. Responses are no-store.';
 rate.responses[202].headers={...rate.responses[202].headers,ETag:{description:'Strong updated draft command version.',schema:etag},Location:{description:'Scoped persisted job resource.',schema:{type:'string'}}};
 history.description='Scoped current rating and immutable cycle history with bounded sequence-keyset pagination. Cursor is signed and bound to actor roles, route, page size and draft command version; it expires after 15 minutes. Current applicability rechecks configuration, revision, base and expiry. draftEtag is a command fence, not a history response validator. Responses are no-store.';
 delete history.responses[200].headers.ETag;
 history.parameters.push({name:'pageSize',in:'query',schema:{type:'integer',minimum:1,maximum:50,default:25}},{name:'cursor',in:'query',schema:{type:'string',minLength:1,maxLength:2048}});
 if(!schemas.Job.properties.kind.enum.includes('servicing-rating'))schemas.Job.properties.kind.enum.push('servicing-rating');
}
