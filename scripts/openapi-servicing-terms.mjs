export function addServicingTerms({schemas:s,ref:r,route,paths}) {
 const id={type:'string',format:'uuid'},instant={type:'string',format:'date-time'},bool={type:'boolean'},hash={type:'string',pattern:'^[a-f0-9]{64}$'},etag={type:'string',minLength:14,maxLength:14};
 const t=(maxLength=200)=>({type:'string',minLength:1,maxLength}),n=value=>({anyOf:[value,{type:'null'}]}),a=(items,maxItems=50,minItems=0)=>({type:'array',items,minItems,maxItems});
 const o=properties=>({type:'object',additionalProperties:false,properties,required:Object.keys(properties)}),seq={type:'integer',minimum:1};
 const channel={enum:['email','written','telephone']},money={type:'string',pattern:'^-?(0|[1-9][0-9]{0,12})\\.[0-9]{2}$'};
 s.ServicingTermsPrepareRequest=o({cycleId:id,ratingId:id,templateVersionId:id});
 s.ServicingTermsSendRequest=o({cycleId:id,termsVersionId:id,recipientContactIds:{...a(id,20,1),uniqueItems:true}});
 s.ServicingAcceptanceRequest=o({cycleId:id,ratingId:id,termsVersionId:id,deliveryId:id,termsHash:hash,assuranceHash:hash,accepterLabel:t(),acceptedAt:instant,channel,evidenceAssociationId:id});
 s.ServicingTermsReceipt=o({id,draftId:id,revisionId:id,cycleId:id,draftEtag:etag});
 s.ServicingTermsRecipient=o({id,name:t(500),email:t(320)});
 // Retained source snapshots have separate versioned domain validators; they are never command inputs.
 const snapshot={type:'object',additionalProperties:true};
 s.ServicingTermsDocument=o({format:{const:'servicing-contract-1'},draftId:id,cycleId:id,revisionId:id,baseVersionId:id,baseTermId:id,policyId:id,ratingId:id,templateVersionId:id,
  inputHash:hash,ratingHash:hash,effectiveDates:a(instant,100,1),slices:a(o({effectiveAt:instant,proposal:r('QuoteCaptureProposal')}),100,1),
  template:o({format:{const:'servicing-template-1'},title:t(300),notice:t(8000)}),ratingInput:snapshot,rating:snapshot,commercialTerms:snapshot,
  conditions:a(o({id,code:t(100),kind:t(30),definition:r('UnderwritingConditionWrite'),effectiveDates:a(instant,100,1)}),100),expiresAt:instant,
  price:o({currency:{const:'GBP'},premium:money,tax:money,fee:money,brokerCommission:money,grossPayable:money,netDue:money}),documentState:{const:'structured-payload'}});
 s.ServicingTermsSnapshot=o({id,sequence:seq,ratingId:id,templateVersionId:id,termsHash:hash,preparedAt:instant,document:r('ServicingTermsDocument'),applicable:bool});
 s.ServicingDeliverySnapshot=o({id,termsVersionId:id,state:{enum:['queued','delivered','failed','superseded']},jobId:id,jobState:{enum:['pending','leased','succeeded','failed']},queuedAt:instant,
  completedAt:n(instant),outcomeCode:n(t()),recipients:a(r('ServicingTermsRecipient'),20,1)});
 s.ServicingAcceptanceSnapshot=o({id,termsVersionId:id,deliveryId:id,accepterLabel:t(),channel,acceptedAt:instant,recordedAt:instant,evidenceAssociationId:id,evidenceReviewId:id});
 s.ServicingTermsView=o({draftId:id,cycleId:id,revisionId:id,ratingId:id,draftEtag:etag,applicable:bool,assuranceHash:hash,canPrepare:bool,canSend:bool,
  blockingCode:n(t()),sendBlockingCode:n(t()),acceptanceApplicable:bool,terms:n(r('ServicingTermsSnapshot')),delivery:n(r('ServicingDeliverySnapshot')),acceptance:n(r('ServicingAcceptanceSnapshot')),
  templates:a(o({id,code:t(),version:seq,title:t(300)})),recipientOptions:a(r('ServicingTermsRecipient'),200)});
 const root='/drafts/{draftId}';delete paths[`${root}/terms`]?.post;delete paths[`${root}/terms/{termsId}/delivery`];
 for(const [suffix,name,input,status] of [['terms/prepare','prepareServicingTerms','ServicingTermsPrepareRequest',201],['terms/send','deliverServicingTerms','ServicingTermsSendRequest',202],['acceptances','recordDraftAcceptance','ServicingAcceptanceRequest',201]]) {
  const path=`${root}/${suffix}`;route('post',path,name,'policy-draft-write',input,'ServicingTermsReceipt',{status});const op=paths[path].post;
  op['x-runtime-status']='phase-7-08-command-implemented';op.responses=structuredClone(op.responses);delete op.responses[status].headers.Location;
  op.responses[status].headers.ETag={schema:etag,description:'Strong updated parent draft command version.'};
  op.description='Current owned rated servicing cycle, identity, authority and reviewed proof are checked before exact receipt replay. Fresh commands require strong If-Match, holder-bound editing lease, CSRF and Idempotency-Key. Body maximum 16 KiB; unknown fields rejected. Send snapshots current scoped recipients and queues persistent demo delivery; queued is not delivered. Acceptance separately binds exact delivered terms, current assurance and accepted current-purpose proof. Acceptance time must include an explicit UTC offset. All responses are no-store.';
 }
 route('get',`${root}/terms`,'getServicingTerms','policy-read',undefined,'ServicingTermsView');const read=paths[`${root}/terms`].get;
 read['x-runtime-status']='phase-7-08-read-implemented';read.responses=structuredClone(read.responses);for(const response of Object.values(read.responses))delete response.headers?.ETag;
 read.parameters=read.parameters.filter(x=>x.in!=='query');read.description='Live scoped servicing terms readiness, exact current contract, delivery and acceptance applicability. Authority and proof are re-evaluated; a retained acceptance does not guarantee it is current. No query parameters. Responses are no-store without a caching ETag; draftEtag fences commands.';
 if(!s.Job.properties.kind.enum.includes('servicing-delivery'))s.Job.properties.kind.enum.push('servicing-delivery');
 s.ServicingTermsHistoryItem=o({id,cycleId:id,revisionId:id,ratingId:id,termsVersionId:id,deliveryId:n(id),recordedAt:instant,state:{enum:['prepared','queued','delivered','failed','superseded','recorded']},label:t()});
 s.ServicingTermsHistoryPage=o({items:a(r('ServicingTermsHistoryItem')),nextCursor:n(t(2048)),draftEtag:etag});
 for(const [path,name,schema,paged] of [[`${root}/terms/history/{kind}`,'listServicingTermsHistory','ServicingTermsHistoryPage',true],[`${root}/terms/history/terms/{termsId}`,'getRetainedServicingTerms','ServicingTermsSnapshot',false]]) {
  route('get',path,name,'policy-read',undefined,schema);const op=paths[path].get;op['x-runtime-status']='phase-7-08-read-implemented';op.responses=structuredClone(op.responses);
  for(const response of Object.values(op.responses))delete response.headers?.ETag;
  op.parameters=op.parameters.filter(x=>x.in!=='query');
  if(paged){op.parameters.find(x=>x.name==='kind').schema={enum:['terms','deliveries','acceptances']};op.parameters.push({name:'pageSize',in:'query',schema:{type:'integer',minimum:1,maximum:50,default:25}},{name:'cursor',in:'query',schema:t(2048)});}
  op.description='Scoped retained servicing history, including old cycles. No-store reads confer no current issue or acceptance eligibility. History pages omit contract payloads and use signed actor, route, size and draft-version bound cursors. One owned immutable contract document is available through the detail route.';
 }
}
