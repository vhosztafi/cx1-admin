import {addServicingRatingContracts} from './openapi-servicing-rating.mjs';
import {addServicingProofReads} from './openapi-servicing-proof-reads.mjs';
import {readFileSync} from 'node:fs';
import {servicingDefinitions} from './servicing-contract-model.mjs';

// Separate names prevent incomplete servicing capture shapes from weakening
// existing quote/issued-policy schemas in the shared OpenAPI components.
export function addServicingContracts({schemas,ref,operation,paths}) {
 const quote=JSON.parse(readFileSync(new URL('../contracts/schemas/quote-draft.schema.json',import.meta.url),'utf8'));
 const definitions=servicingDefinitions(quote);
 const nameFor=name=>name.startsWith('Servicing')?name:`ServicingCapture${name}`;
 const rewrite=value=>{
  if(Array.isArray(value))return value.map(rewrite);
  if(!value||typeof value!=='object')return value;
  return Object.fromEntries(Object.entries(value).map(([key,item])=>[key,key==='$ref'&&item.startsWith('#/$defs/')?ref(nameFor(item.slice(8))).$ref:rewrite(item)]));
 };
 for(const [name,schema] of Object.entries(definitions)) schemas[nameFor(name)]=rewrite(schema);
 const route=(method,path,name,permission,input,output='CommandResult',options={})=>{
  const previous=paths[path]?.[method];
  if(previous&&previous.operationId!==name)throw new Error(`Servicing operation ID change: ${previous.operationId}`);
  delete paths[path]?.[method];
  operation(method,path,name,permission,{existing:method!=='get',lease:method!=='get',...(input?{input:ref(input)}:{}),output:ref(output),...options});
  const entry=paths[path][method];
  entry['x-runtime-status']='phase-7-pending';
  entry.description+=' Servicing contract only until the owning plan implements this route. Current identity, capability and policy scope are checked before replay; receipt replay does not grant authority. Fresh mutations recheck the strong ETag, lease generation and full immutable decision inputs. Responses are no-store.';
  for(const response of Object.values(entry.responses)) {
   response.headers={...response.headers,'Cache-Control':{description:'Personal policy and servicing information is never cacheable.',schema:{type:'string',const:'no-store'}}};
  }
 };
 addServicingRatingContracts({schemas,ref,route,paths});
 const root='/drafts/{draftId}';
 route('get','/terms/{termId}/drafts','listPolicyDrafts','policy-read',undefined,'ServicingDraftList');
 route('get',root,'getPolicyDraft','policy-read',undefined,'ServicingDraft');
 route('post','/terms/{termId}/drafts','createPolicyDraft','policy-draft-write','ServicingDraftCreate','ServicingDraft',{lease:false,status:201});
 route('post',`${root}/lease`,'acquireDraftLease','policy-draft-write','ServicingLeaseAcquire','ServicingDraft',{lease:false});
 route('put',`${root}/lease`,'renewDraftLease','policy-draft-write',undefined,'ServicingDraft');
 route('delete',`${root}/lease`,'releaseDraftLease','policy-draft-write',undefined,'ServicingDraft');
 route('put',`${root}/proposal`,'savePolicyDraft','policy-draft-write','ServicingProposal','ServicingDraft');
 route('post',`${root}/abandon`,'abandonPolicyDraft','policy-draft-write','ServicingReason','ServicingDraft');
 for(const [path,method] of [['/terms/{termId}/drafts','get'],['/terms/{termId}/drafts','post'],[root,'get'],[`${root}/lease`,'post'],[`${root}/lease`,'put'],[`${root}/lease`,'delete'],[`${root}/proposal`,'put'],[`${root}/abandon`,'post']]) {
  const op=paths[path][method];
  op.responses=structuredClone(op.responses);
  op['x-runtime-status']='phase-7-03-implemented';
  op.description='Persistent servicing draft or editing lease. Current internal identity, capability and policy ownership are checked before receipt replay. Mutations require the current strong ETag; editing additionally requires a live holder-bound X-Edit-Lease fence. Takeover requires policy-draft-takeover and a reason. Renew/release take no body. Lease changes return the draft and its new ETag. Responses are no-store. Capture does not grant authority to rate or issue.';
  for(const response of Object.values(op.responses)) response.headers={...response.headers,ETag:{description:'Strong term version for listing/creation; strong draft version for draft commands.',schema:{type:'string'}}};
 }
 route('post',`${root}/acceptances`,'recordDraftAcceptance','policy-acceptance','ServicingAcceptance','CommandResult',{status:201});
 route('get',`${root}/editor`,'getPolicyDraftEditor','policy-read',undefined,'ServicingEditor');
 const editor=paths[`${root}/editor`].get;
 editor['x-runtime-status']='phase-7-04-implemented';
 editor.description='Current scoped saved draft projection against its immutable issued base. Returns typed base/proposed capture, identity-based differences, cumulative effective slices and capture blockers. Re-evaluated with current authority and time; never rating or issue authority. Responses are no-store and carry the strong draft ETag.';
 editor.responses=structuredClone(editor.responses);
 editor.responses[200].headers={...editor.responses[200].headers,ETag:{description:'Strong draft version for this saved editor projection.',schema:{type:'string'}}};
 route('post',`${root}/issue`,'issuePolicyDraft','policy-issue-within-authority','ServicingIssueWrite','ServicingIssueResult',{status:201});
 route('get',`${root}/cancellation-preview`,'getCancellationPreview','cancellation-review',undefined,'ServicingCancellationPreview');
 const writes=[
  ['evidence','attachDraftEvidence','underwriting-evidence-write','ServicingEvidence'],
  ['evidence/{evidenceId}/review','reviewServicingEvidence','underwriting-evidence-review','ServicingEvidenceReview'],
  ['evidence/{evidenceId}/withdraw','withdrawDraftEvidence','underwriting-evidence-write','ServicingReason'],
  ['referrals/{referralId}/decisions','decideServicingReferral','underwriting-decide-within-authority','ServicingDecision'],
  ['referrals/decisions','decideServicingReferrals','underwriting-decide-within-authority','ServicingSelectedDecision'],
  ['capacity','createServicingCapacity','underwriting-escalate','ServicingCapacity'],
  ['capacity/{caseId}/submissions','submitServicingCapacity','underwriting-escalate','ServicingCapacitySubmission'],
  ['capacity/{caseId}/responses','recordServicingCapacityResponse','underwriting-capacity-response','ServicingCapacityResponse'],
  ['capacity/{caseId}/actions','recordServicingCapacityAction','underwriting-escalate','ServicingCapacityAction'],
  ['capacity/{caseId}/conditions/{conditionId}/resolve','resolveServicingCapacityCondition','underwriting-evidence-review','ServicingConditionResolution'],
  ['terms','prepareServicingTerms','policy-draft-submit','ServicingTermsWrite'],
  ['terms/{termsId}/delivery','deliverServicingTerms','policy-draft-submit','ServicingDelivery'],
  ['cancellation-preview','persistCancellationPreview','cancellation-review','ServicingPreviewWrite'],
  ['cancellation-approvals','approveCancellation','cancellation-approve','ServicingCancellationApproval'],
 ];
 for(const [suffix,name,permission,input] of writes)route('post',`${root}/${suffix}`,name,permission,input);
 route('put',`${root}/experience`,'recordRenewalExperience','policy-draft-write','ServicingExperience');
 route('post',`${root}/evidence/uploads`,'uploadServicingEvidenceFile','underwriting-evidence-write',undefined,'CommandResult',{status:201});
 paths[`${root}/evidence/uploads`].post.requestBody={
  required:true,
  content:{'multipart/form-data':{schema:{
   type:'object',additionalProperties:false,required:['fileName','contentType','file'],
   properties:{
    fileName:{type:'string',minLength:1,maxLength:200},
    contentType:{type:'string',enum:['application/pdf','image/png','image/jpeg','text/plain']},
    file:{type:'string',format:'binary',maxLength:10485760},
   },
  }}},
 };
 paths[`${root}/evidence/uploads`].post.description+=' Exactly one file, at most 10 MiB plus 16 KiB multipart overhead, matching supplied metadata; validate actual bytes using existing evidence rules. Reject paths and unknown form fields.';
 addServicingProofReads({schemas,ref,route,paths});
}
