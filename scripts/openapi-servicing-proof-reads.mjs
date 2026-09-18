export function addServicingProofReads({schemas,ref,route,paths}) {
 const id={type:'string',format:'uuid'}, instant={type:'string',format:'date-time'}, flag={type:'boolean'};
 const text=(maxLength=2000)=>({type:'string',maxLength}), enumeration=(...values)=>({type:'string',enum:values});
 const nullable=value=>({anyOf:[value,{type:'null'}]}), array=(items,maxItems=50)=>({type:'array',items,maxItems});
 const object=(properties,required=Object.keys(properties))=>({type:'object',additionalProperties:false,properties,required});
 const etag={type:'string',minLength:14,maxLength:14}, hash={type:'string',pattern:'^[a-f0-9]{64}$'}, sequence={type:'integer',minimum:1};
 const purpose=enumeration('motor-trader-proof','no-claims-proof','trading-history','photocard-both-sides','driving-record','premises-security','warranty-acknowledgement');
 const outcome=enumeration('approve','approve-with-conditions','query','decline','reopen');
 const root='/drafts/{draftId}';
 schemas.ServicingProofContext=object({draftId:id,cycleId:id,revisionId:id,ratingId:id,inputHash:hash,pins:object({productVersionId:id,agencyTermsVersionId:id,schemaVersion:text(200),questionSetVersion:text(200),referenceVersion:text(200)})});
 schemas.ServicingProofRequirement=object({code:purpose,label:text(),path:text(500),riskItemId:nullable(id),effectiveDates:array(instant,100),inputFingerprint:hash,context:ref('ServicingProofContext')});
 schemas.ServicingProofRequirements=object({draftId:id,cycleId:id,draftEtag:etag,applicable:flag,requirements:array(object({requirement:ref('ServicingProofRequirement'),satisfied:flag}),5001)});
 schemas.ServicingFileView=object({id,fileName:text(200),contentType:enumeration('application/pdf','image/png','image/jpeg','text/plain'),byteLength:{type:'integer',minimum:1,maximum:10485760},screeningState:{const:'accepted'},screeningMethod:{const:'demo-signature-v1'},createdAt:instant});
 schemas.ServicingAssociationView=object({id,cycleId:id,revisionId:id,ratingId:id,fileId:id,fileName:text(200),code:purpose,riskItemId:nullable(id),inputFingerprint:hash,reason:text(),etag,latestReviewId:nullable(id),reviewOutcome:nullable(enumeration('accepted','rejected')),withdrawn:flag,createdAt:instant});
 schemas.ServicingReviewView=object({id,sequence,kind:enumeration('review','withdrawal'),outcome:nullable(enumeration('accepted','rejected')),reason:text(),actorId:id,authorityVersionId:nullable(id),recordedAt:instant});
 schemas.ServicingDecisionView=object({id,sequence,outcome,reason:text(),question:nullable(text()),actorId:id,authorityVersionId:id,grantId:id,decidedAt:instant,conditions:array(ref('UnderwritingConditionWrite'),20)});
 schemas.ServicingConditionClause=object({effectiveAt:instant,wording:text(8000),endorsementCode:nullable(text(100)),targetIds:array(id,100)});
 schemas.ServicingConditionView=object({id,code:text(60),kind:enumeration('documentary','warranty','risk-change'),satisfied:flag,etag,definition:ref('UnderwritingConditionWrite'),clauses:array(ref('ServicingConditionClause'),100)});
 const requirement=object({ruleCode:text(60),dimension:text(60),targetId:nullable(id),requestedAmount:nullable({type:'number'}),authorisedAmount:nullable({type:'number'})},['ruleCode','dimension']);
 schemas.ServicingReferralView=object({id,sequence,ruleCode:text(60),dimension:text(60),riskItemId:nullable(id),state:enumeration('open','approved','conditional','queried','declined','superseded'),etag,decisionId:nullable(id),decisionReady:flag,conditions:array(ref('ServicingConditionView'),20),reason:text(),requiredAuthority:object({triggers:array(object({effectiveAt:instant,source:enumeration('source','binder','authority'),requirement}),1000)}),decision:nullable(ref('ServicingDecisionView'))});
 const page=item=>object({items:array(ref(item)),nextCursor:nullable(text(2048)),draftEtag:etag});
 schemas.ServicingDatedAuthority=object({effectiveAt:instant,limit:object({code:text(100),label:text(200),requested:text(200),actorLimit:text(200),binderLimit:text(200),actorAllows:flag,binderAllows:flag})});
 schemas.ServicingCurrentGrant=object({grantId:id,authorityVersionId:id,version:text(200),effectiveFrom:instant,effectiveTo:instant,scheduleWithinAuthority:flag,rows:array(ref('ServicingDatedAuthority'),100)});
 schemas.ServicingCurrentAuthorityPage=object({draftId:id,cycleId:id,referralId:id,draftEtag:etag,assessedAt:instant,applicable:flag,canDecide:flag,binder:array(ref('ServicingDatedAuthority'),100),items:array(ref('ServicingCurrentGrant'),5),nextCursor:nullable(text(2048))});
 schemas.ServicingFilePage=page('ServicingFileView');schemas.ServicingAssociationPage=page('ServicingAssociationView');schemas.ServicingReviewPage=page('ServicingReviewView');
 schemas.ServicingDecisionPage=object({...page('ServicingDecisionView').properties,referralId:id,cycleId:id});
 schemas.ServicingReferralPage=object({...page('ServicingReferralView').properties,draftId:id,cycleId:id,applicable:flag});
 for(const [suffix,name,schema,paginated] of [
  ['evidence/requirements','getServicingProofRequirements','ServicingProofRequirements',false],
  ['evidence-files','listServicingEvidenceFiles','ServicingFilePage',true],
  ['evidence','listDraftEvidence','ServicingAssociationPage',true],
  ['evidence/{associationId}/events','listServicingEvidenceEvents','ServicingReviewPage',true],
  ['referrals','listServicingReferrals','ServicingReferralPage',true],
  ['referrals/{referralId}/decisions','listServicingReferralDecisions','ServicingDecisionPage',true],
  ['referrals/{referralId}/authority','getServicingCurrentAuthority','ServicingCurrentAuthorityPage',true],
  ['evidence-files/{fileId}/content','downloadServicingEvidenceFile','ServicingFileView',false],
 ]) {
  const path=`${root}/${suffix}`;route('get',path,name,'policy-read',undefined,schema);
  const op=paths[path].get;op['x-runtime-status']='phase-7-06-read-implemented';op.responses=structuredClone(op.responses);
  for(const response of Object.values(op.responses))delete response.headers?.ETag;
  op.description='Current scoped policy read access is required. Responses are no-store. Historical records remain readable after rating expiry; they do not grant current decision or issue authority. Draft ETags fence commands and cursor context, not response caching.';
  op.parameters=op.parameters.filter(p=>p.in!=='query');
  if(paginated) {
   op.parameters.push({name:'pageSize',in:'query',schema:{type:'integer',minimum:1,maximum:50,default:25}},{name:'cursor',in:'query',schema:{type:'string',minLength:1,maxLength:2048}});
   op.description+=' Pagination uses signed cursors bound to actor scope, route, filters, page size and draft version, expiring after 15 minutes. Files/associations use CreatedAt/Id keysets; events/decisions/referrals use sequence keysets.';
  }
  if(suffix==='evidence')op.parameters.push({name:'cycleId',in:'query',required:true,schema:id});
  if(suffix.endsWith('/authority')) {
   op.parameters.find(p=>p.name==='pageSize').schema={type:'integer',minimum:1,maximum:5,default:5};
   op.description='Live grants of the current actor only, individually compared against the requested referral dimension at every rated effective date. No maxima are combined. Full schedule assessment includes retained conditions, and is not overall issue readiness. Authority changes are reread even when the draft ETag is unchanged. Signed grant-key cursors are scoped to this actor, referral, draft version and page size; a removed cursor grant requires restarting pagination. Responses are no-store.';
  }
  if(suffix.endsWith('/content')) {
   op.responses[200].content=Object.fromEntries(['application/pdf','image/png','image/jpeg','text/plain'].map(type=>[type,{schema:{type:'string',format:'binary',maxLength:10485760}}]));
   op.responses[200].headers['Content-Disposition']={description:'Always an attachment with the screened filename.',schema:{type:'string'}};
   op.responses[200].headers['X-Content-Type-Options']={schema:{type:'string',const:'nosniff'}};
  }
 }
}
