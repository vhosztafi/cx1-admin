export function addServicingProofCommands({schemas,ref,route,paths}) {
 const id={type:'string',format:'uuid'},etag={type:'string',minLength:14,maxLength:14},hash={type:'string',pattern:'^[a-f0-9]{64}$'};
 const text=(maxLength=2000,minLength=1)=>({type:'string',minLength,maxLength,pattern:'\\S'});
 const enumeration=(...values)=>({type:'string',enum:values});
 const object=(properties,required=Object.keys(properties))=>({type:'object',additionalProperties:false,properties,required});
 const root='/drafts/{draftId}';
 schemas.ServicingProofReceipt=object({id,draftId:id,revisionId:id,cycleId:id,draftEtag:etag});
 schemas.ServicingAttachProofRequest=object({cycleId:id,fileId:id,requirementCode:schemas.ServicingProofRequirement.properties.code,riskItemId:id,inputFingerprint:hash,reason:text(2000,10)},['cycleId','fileId','requirementCode','inputFingerprint','reason']);
 schemas.ServicingReviewProofRequest=object({cycleId:id,associationEtag:etag,outcome:enumeration('accepted','rejected'),expectedFingerprint:hash,reason:text(2000,10)});
 schemas.ServicingWithdrawProofRequest=object({cycleId:id,associationEtag:etag,reason:text(2000,10)});
 schemas.ServicingReferralDecisionItem={oneOf:['approve','approve-with-conditions','query','decline','reopen'].map(outcome=>object({
  referralId:id,etag,outcome:{const:outcome},reason:text(2000,10),
  ...(outcome==='approve-with-conditions'||outcome==='query'?{conditions:{type:'array',minItems:1,maxItems:20,items:ref('UnderwritingConditionWrite')}}:{}),
  ...(outcome==='query'?{question:text(2000,10)}:{}),
 }))};
 schemas.ServicingSelectedReferralDecisionRequest=object({cycleId:id,decisions:{type:'array',minItems:1,maxItems:50,items:ref('ServicingReferralDecisionItem')}});
 schemas.ServicingSingleReferralDecisionRequest=object({cycleId:id,decision:ref('ServicingReferralDecisionItem')});
 schemas.ServicingResolveProofRequest=object({cycleId:id,conditionEtag:etag,evidenceAssociationId:id,outcome:enumeration('satisfied','rejected'),reason:text(2000,10)});
 // Replace the unimplemented planning spellings with the actual owned
 // association resource and append-only review collection.
 delete paths[`${root}/evidence/{evidenceId}/review`];delete paths[`${root}/evidence/{evidenceId}/withdraw`];
 const uploadBody=structuredClone(paths[`${root}/evidence/uploads`].post.requestBody);
 for(const [suffix,name,permission,input,status] of [
  ['evidence/uploads','uploadServicingEvidenceFile','underwriting-evidence-write',undefined,201],
  ['evidence','attachDraftEvidence','underwriting-evidence-write','ServicingAttachProofRequest',201],
  ['evidence/{associationId}/reviews','reviewServicingEvidence','underwriting-evidence-review','ServicingReviewProofRequest',200],
  ['evidence/{associationId}/withdraw','withdrawDraftEvidence','underwriting-evidence-write','ServicingWithdrawProofRequest',200],
  ['referrals/decisions','decideServicingReferrals','underwriting-decide-within-authority','ServicingSelectedReferralDecisionRequest',200],
  ['referrals/{referralId}/decisions','decideServicingReferral','underwriting-decide-within-authority','ServicingSingleReferralDecisionRequest',200],
  ['conditions/{conditionId}/resolutions','resolveServicingReferralCondition','underwriting-decide-within-authority','ServicingResolveProofRequest',200],
 ]) {
  const path=`${root}/${suffix}`;route('post',path,name,permission,input,'ServicingProofReceipt',{status});
  const op=paths[path].post;op['x-runtime-status']='phase-7-06-command-implemented';op.responses=structuredClone(op.responses);
  op.description='Persistent servicing proof/referral command. Requires current identity, capability and policy scope before receipt replay, a strong draft If-Match ETag, holder-bound X-Edit-Lease and Idempotency-Key. Review and resolution require current grants. Proof binds the exact current cycle/revision/rating and purpose fingerprint; review and resolution also require a strong child ETag. Selected decisions are atomic; conditional readiness retains its proof dependencies. Responses are no-store and return the updated draft command ETag.';
  op.responses[status].headers.ETag={description:'Strong updated draft command version.',schema:etag};
  delete op.responses[status].headers.Location;
  if(suffix==='evidence/uploads') {
   op.requestBody=uploadBody;op.description+=' Exactly one screened file, at most 10 MiB plus 16 KiB multipart overhead, matching supplied filename/media metadata; unknown or duplicate fields are rejected.';
   op.responses[201].headers.Location={description:'Scoped attachment download URL.',schema:{type:'string'}};
  }
 }
}
