import {closed as o,uid as id,instant,hash,label as t,choice as e} from './underwriting-contract-model.mjs';

export function addPolicyHistoryContracts({schemas:s,ref:r,operation,paths}) {
 const nullable=value=>({anyOf:[value,{type:'null'}]});
 const array=items=>({type:'array',items});
 const integer={type:'integer',minimum:0};
 const etag={type:'string',pattern:'^"[A-Za-z0-9+/]{11}="$'};
 const coverage=e('not-covered','scheduled','active','expired','cancelled');
 s.PolicyHistoryVersion=o({id,termId:id,transactionId:id,termNumber:integer,versionSequence:integer,transactionSequence:integer,sliceOrdinal:integer,
  kind:e('new-business','adjustment','renewal','cancellation'),effectiveAt:instant,processedAt:instant,contentHash:hash,reason:t(2000),actorId:nullable(id),
  applicability:e('not-yet-known','selected','not-yet-effective','different-term','superseded'),actorLabel:t(200),obligationId:id,
  amountDue:{type:'string',pattern:'^-?(0|[1-9][0-9]{0,12})\\.[0-9]{2}$'},documentRequests:array(o({id,kind:t(100),state:t(30)}))});
 s.PolicyHistoryView=o({policyId:id,reference:t(100),effectiveAt:instant,knownAt:instant,selectedVersionId:nullable(id),coverageState:coverage,
  versions:array(r('PolicyHistoryVersion')),policyEtag:etag});
 s.PolicyVersionComparison=o({policyId:id,beforeVersionId:id,afterVersionId:id,beforeHash:hash,afterHash:hash,changes:array(r('QuoteRevisionChange'))});
 s.PolicyCloneTerms=o({policyId:id,versionId:id,relationshipId:id,termsId:id,termsVersion:integer,policyEtag:etag});
 s.PolicyCloneInput=o({versionId:id,relationshipId:id,confirmedTermsId:id,reason:t(2000)});
 s.PolicyCloneResult=o({quoteId:id,revisionId:id,lineageId:id,sourcePolicyId:id,sourceVersionId:id,quoteEtag:etag});
 const riskEntry=item=>o({versionId:id,termId:id,versionSequence:integer,effectiveAt:instant,processedAt:instant,contentHash:hash,item:nullable(item)});
 s.PolicyRiskHistory={oneOf:['drivers','vehicles'].map(kind=>o({policyId:id,kind:{const:kind},itemId:id,
  versions:array(riskEntry({$ref:`./schemas/issued-policy.schema.json#/$defs/${kind==='drivers'?'Driver':'Vehicle'}`}))}))};
 s.PolicyReconstructionInput={oneOf:[o({effectiveAt:instant,knownAt:instant,versionId:id,contentHash:hash,reason:t(2000)}),
  o({effectiveAt:instant,knownAt:instant,reason:t(2000)})]};
 const selection={policyId:id,termId:id,versionId:nullable(id),contentHash:nullable(hash),effectiveAt:instant,knownAt:instant,coverageState:coverage};
 s.PolicyReconstructionResult=o({requestId:id,...selection,state:{const:'pending'}});
 s.PolicyReconstructionView=o({id,...selection,manifestHash:hash,actorId:id,reason:t(2000),createdAt:instant,state:e('pending','running','completed','failed','quarantined')});
 // Replace the unimplemented global-version commands with policy-scoped routes.
 delete paths['/versions/{versionId}/compare']; delete paths['/versions/{versionId}/clone-quote'];
 const route=(method,path,name,permission,output,options={})=>{
  delete paths[path]?.[method];
  operation(method,path,name,permission,{output:r(output),existing:method!=='get',...options});
  const entry=paths[path][method];entry['x-runtime-status']='phase-7-15-in-progress';
  entry.description+=' Current identity and policy ownership are checked before reading or replaying a command. Mutations require a strong policy ETag and idempotency key. No edit lease is needed for immutable policy selections. Responses are no-store.';
  for(const response of Object.values(entry.responses))response.headers={...response.headers,'Cache-Control':{schema:{type:'string',const:'no-store'},description:'Policy data is not cacheable.'}};
  return entry;
 };
 route('get','/policies/{policyId}/history','getPolicyHistory','policy-read','PolicyHistoryView',{query:[['effectiveAt',instant],['knownAt',instant]]})
  .description+=' Either omit both cutoffs or provide both with explicit timezone offsets.';
 route('get','/terms/{termId}/versions','listPolicyVersions','policy-read','PolicyHistoryView');
 const risk=route('get','/policies/{policyId}/risk/{kind}/{itemId}/history','getPolicyRiskHistory','policy-read','PolicyRiskHistory');
 risk.parameters.find(x=>x.name==='kind').schema=e('drivers','vehicles');
 for(const [suffix,name,permission,output,query] of [
  ['compare','comparePolicyVersions','policy-read','PolicyVersionComparison',[['beforeVersionId',id],['afterVersionId',id]]],
  ['clone-terms','getPolicyCloneTerms','quote-capture','PolicyCloneTerms',[['versionId',id],['relationshipId',id]]],
 ])route('get',`/policies/{policyId}/${suffix}`,name,permission,output,{query}).parameters.filter(x=>x.in==='query').forEach(x=>x.required=true);
 route('get','/policies/{policyId}/reconstructions','listPolicyReconstructions','policy-read','PolicyReconstructionList');
 s.PolicyReconstructionList=array(r('PolicyReconstructionView'));
 route('post','/policies/{policyId}/clone','clonePolicyToQuote','quote-capture','PolicyCloneResult',{input:r('PolicyCloneInput'),status:201});
 const exported=route('post','/terms/{termId}/as-at/export','exportPolicyReconstruction','policy-draft-write','PolicyReconstructionResult',{input:r('PolicyReconstructionInput'),status:201});
 delete exported.responses['201'].headers.Location;
 exported.description+=' Persists a reconstruction manifest for Phase 9 rendering; it does not generate or download a PDF. Omit versionId and contentHash together when nothing was known at the chosen cutoffs.';
}
