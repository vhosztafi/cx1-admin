export function addCancellationReview({schemas,ref,route,paths}) {
 for(const unused of ['ServicingCancellationApproval','ServicingCancellationPreview','ServicingPreviewWrite'])delete schemas[unused];
 const id={type:'string',format:'uuid'},instant={type:'string',format:'date-time'},etag={type:'string',pattern:'^"[A-Za-z0-9+/]{11}="$'};
 const object=properties=>({type:'object',additionalProperties:false,properties,required:Object.keys(properties)});
 const nullable=schema=>({anyOf:[schema,{type:'null'}]});
 const text=maxLength=>({type:'string',maxLength}),money={type:'string',pattern:'^-?(0|[1-9][0-9]{0,12})\\.[0-9]{2}$'};
 const hash={type:'string',pattern:'^[0-9a-f]{64}$'},reason={type:'string',minLength:10,maxLength:2000};
 const codes={enum:['premium','tax','fee','commission','fee-share']};
 const movement=object({code:codes,ordinal:{type:'integer',minimum:1,maximum:1000},amount:money,startsAt:instant,endsAt:instant,originalComponentId:id});
 const line=object({code:codes,ordinal:{type:'integer',minimum:1,maximum:1000},accountCode:{enum:['agency-receivable','relationship-receivable','insurer-payable','fee-income','broker-remuneration-payable']},
  debit:money,credit:money,startsAt:instant,endsAt:instant});
 const posting=object({premium:money,tax:money,fee:money,commission:money,feeShare:money,grossDue:money,invoiceDue:money,netDue:money,brokerPayable:money,
  debtorKind:{enum:['agency','relationship']},settlement:{enum:['net-remittance','separate-payment']},movements:{type:'array',maxItems:1505,items:movement},lines:{type:'array',maxItems:3010,items:line}});
 schemas.CancellationReviewView=object({draftId:id,revisionId:id,baseVersionId:id,draftEtag:etag,previewId:nullable(id),approvalId:nullable(id),previewHash:hash,
  ruleVersion:text(60),reasonCode:{enum:['','insured-request','non-payment','non-disclosure','trade-ceased','insurer-instruction']},effectiveAt:instant,supportedFrom:instant,
  noticeEffectiveFrom:nullable({type:'string',format:'date'}),amounts:nullable(object({posting,retainedFee:money,retainedFeeShare:money})),blockers:{type:'array',maxItems:100,items:text(100)},canApprove:{type:'boolean'},daysOnCover:{type:'integer',minimum:0,maximum:366},termDays:{type:'integer',minimum:1,maximum:366},premiumChargedToDate:nullable(money)});
 schemas.CancellationReviewReceipt=object({draftId:id,resourceId:id,draftEtag:etag});
 schemas.CancellationPrepare=object({previewHash:hash});
 schemas.CancellationApprove=object({previewId:id,previewHash:hash,reason});
 schemas.CancellationEvidenceReview=object({outcome:{enum:['accepted','rejected']},reason});
 schemas.CancellationEvidencePage=object({draftId:id,draftEtag:etag,items:{type:'array',maxItems:100,items:object({id,revisionId:id,fileId:id,fileName:text(200),
  purpose:{enum:['cancellation-request','cancellation-notice','cancellation-reason','insurer-instruction']},noticeDeliveredAt:nullable(instant),reviewId:nullable(id),
  reviewState:{enum:['unreviewed','accepted','rejected','authority-expired']},reviewReason:nullable(reason)})}});
 const root='/drafts/{draftId}';
 for(const [method,suffix,name,capability,input,output] of [
  ['get','cancellation-preview','getCancellationPreview','policy-read',undefined,'CancellationReviewView'],
  ['post','cancellation-preview','persistCancellationPreview','policy-draft-write','CancellationPrepare','CancellationReviewReceipt'],
  ['post','cancellation-approvals','approveCancellation','underwriting-decide-within-authority','CancellationApprove','CancellationReviewReceipt'],
  ['get','cancellation-evidence','readCancellationEvidence','policy-read',undefined,'CancellationEvidencePage'],
  ['post','cancellation-evidence/uploads','uploadCancellationEvidence','underwriting-evidence-write',undefined,'CancellationReviewReceipt'],
  ['post','cancellation-evidence/{evidenceId}/reviews','reviewCancellationEvidence','underwriting-evidence-review','CancellationEvidenceReview','CancellationReviewReceipt'],
 ]) {
  route(method,`${root}/${suffix}`,name,capability,input,output,{status:method==='get'?200:201,lease:method!=='get'});
  const operation=paths[`${root}/${suffix}`][method];operation['x-runtime-status']='phase-7-13-implemented';
  operation.description='Cancellation review uses original posted component intervals, current evidence, notice delivery and explicit permitted underwriting authority. It creates no rating cycle, policy issue or cash payment. Current actor and policy scope are checked before receipt replay; approval also rechecks current grant and required requester separation. Fresh commands require strong If-Match, X-Edit-Lease and an idempotency key. Proposal, ledger, evidence and configuration changes invalidate the retained preview. Responses are no-store.';
 }
 paths[`${root}/cancellation-evidence/uploads`].post.requestBody={required:true,content:{'multipart/form-data':{schema:{type:'object',additionalProperties:false,
  required:['file','fileName','contentType','purpose'],properties:{file:{type:'string',format:'binary',maxLength:10485760},fileName:{type:'string',minLength:1,maxLength:200},
   contentType:{enum:['application/pdf','image/png','image/jpeg','text/plain']},purpose:schemas.CancellationEvidencePage.properties.items.items.properties.purpose,noticeDeliveredAt:instant}}}}};
}
