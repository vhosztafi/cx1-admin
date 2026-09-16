import {addQuoteSupportContracts} from './openapi-quote-support.mjs';
// Phase 5 capture contracts. Future workflow operations remain separate; this
// document is not evidence that the runtime quote routes have been implemented.
export function addQuoteContracts({schemas:s,ref:r,text:t,enumeration:e,object:o,array:a,id,instant,boolean:b,integer,operation:op,list,paths}) {
  const product=e('motor-trade-road-risks','motor-trade-combined');
  const state=e('draft','withdrawn');
  const hash={type:'string',pattern:'^[a-f0-9]{64}$'};
  s.QuoteCaptureProposal={$ref:'./schemas/quote-draft.schema.json'};
  s.QuoteIdentityResult=o({id});
  s.QuoteCreateRequest=o({relationshipId:id,productVersionId:id,matchSubmissionId:id,proposal:r('QuoteCaptureProposal')},['relationshipId','productVersionId']);
  s.QuoteSaveRequest=o({proposal:r('QuoteCaptureProposal'),reason:t(1000)},['proposal']);
  s.QuoteCloneRequest=o({sourceRevisionId:id,relationshipId:id,confirmedTermsId:id,reason:{...t(1000),pattern:'\\S'}},['sourceRevisionId','relationshipId','reason']);
  s.QuoteCloneTerms=o({sourceRevisionId:id,relationshipId:id,agencyTermsVersionId:id,version:{type:'integer',minimum:1},effectiveFrom:{type:'string',format:'date'},confirmationRequired:b});
  s.QuoteCaptureSummary=o({id,reference:t(40),relationshipId:id,clientId:id,agencyId:id,clientName:t(),agencyName:t(),productCode:product,state,revisionId:id,revisionNumber:{type:'integer',minimum:1},updatedAt:instant});
  s.QuoteReadiness=o({quoteId:id,revisionId:id,ready:b,issues:a(o({path:t(500),code:t(100),message:t(1000),category:e('capture','evidence','eligibility','matching','configuration'),severity:e('error','warning'),questionId:t(200),relatedPath:t(500)},['path','code','message','category','severity']))});
  s.QuoteCaptureRevision=o({id,quoteId:id,number:{type:'integer',minimum:1},productVersionId:id,agencyTermsVersionId:id,questionSetVersion:t(100),referenceDataVersion:t(100),proposal:r('QuoteCaptureProposal'),proposalHash:hash,savedAt:instant,savedByLabel:t(),reason:t(1000)},['id','quoteId','number','productVersionId','agencyTermsVersionId','questionSetVersion','referenceDataVersion','proposal','proposalHash','savedAt','savedByLabel']);
  s.QuoteCaptureVersions=o({schemaVersion:t(100),questionSetVersion:t(100),referenceDataVersion:t(100)});
  s.QuoteCaptureView=o({...s.QuoteCaptureSummary.properties,productVersionId:id,captureVersions:r('QuoteCaptureVersions'),proposal:r('QuoteCaptureProposal'),captureClosed:b,captureClosedAt:{anyOf:[instant,{type:'null'}]},captureClosedReason:{anyOf:[t(1000),{type:'null'}]},capabilities:o({canSave:b,canClone:b,canWithdraw:b,canAttachEvidence:b}),readiness:r('QuoteReadiness')});
  s.QuoteCaptureProduct=o({productVersionId:id,productCode:product,displayName:t(),versionLabel:t(100),questionSetVersion:t(100),referenceDataVersion:t(100),captureEligible:b,unavailableReason:t(1000)},['productVersionId','productCode','displayName','versionLabel','questionSetVersion','referenceDataVersion','captureEligible']);
  const replace=(method,path,...args)=>{delete paths[path]?.[method];op(method,path,...args);};
  const replaceList=(path,...args)=>{delete paths[path]?.get;list(path,...args);};
  replace('post','/quotes','createQuote','quote-capture',{input:r('QuoteCreateRequest'),output:r('QuoteIdentityResult'),status:201});
  replaceList('/quotes','listQuotes','quote-read',r('QuoteCaptureSummary'),[['q',t(200)],['productCode',product],['agencyId',id],['clientId',id],['status',state],['sort',e('reference','updated','start')],['direction',e('asc','desc')]]);
  replace('get','/quotes/{quoteId}','getQuote','quote-read',{output:r('QuoteCaptureView')});
  replace('put','/quotes/{quoteId}/proposal','saveQuoteProposal','quote-capture',{existing:true,input:r('QuoteSaveRequest'),output:r('QuoteIdentityResult')});
  replace('post','/quotes/{quoteId}/withdraw','withdrawQuote','quote-capture',{existing:true,input:o({reason:t(1000)}),output:r('QuoteIdentityResult')});
  replace('post','/quotes/{quoteId}/clone','cloneQuote','quote-capture',{existing:true,input:r('QuoteCloneRequest'),output:r('QuoteIdentityResult'),status:201});
  op('get','/quotes/{quoteId}/clone-terms','getQuoteCloneTerms','quote-capture',{query:[['sourceRevisionId',id],['relationshipId',id]],output:r('QuoteCloneTerms')});
  for(const parameter of paths['/quotes/{quoteId}/clone-terms'].get.parameters.filter(x=>x.in==='query'))parameter.required=true;
  delete paths['/quotes/{quoteId}/validate'];
  op('get','/quotes/{quoteId}/readiness','validateQuote','quote-read',{output:r('QuoteReadiness'),summary:'Assess current revision readiness without changing quote state'});
  replaceList('/quotes/{quoteId}/revisions','listQuoteRevisions','quote-read',r('QuoteCaptureRevision'));
  op('get','/quotes/{quoteId}/revisions/{revisionId}','getQuoteRevision','quote-read',{output:r('QuoteCaptureRevision')});
  op('get','/quote-products','listQuoteProducts','quote-read',{query:[['relationshipId',id]],output:o({items:a(r('QuoteCaptureProduct'))})});
  paths['/quote-products'].get.parameters.find(parameter=>parameter.name==='relationshipId').required=true;
  // Retain the already reviewed compare route and operation identity.
  const comparison=paths['/quotes/{quoteId}/compare'].get;
  for(const parameter of comparison.parameters.filter(parameter=>parameter.in==='query'))parameter.required=true;
  // Serialized JSON preserves scalar type and complete prior values. A missing
  // side differs from a present side containing the JSON literal null.
  const side=o({path:t(2000),json:{type:'string',minLength:1,maxLength:1048576}});
  s.QuoteRevisionChange={oneOf:[
    o({kind:{const:'added'},path:t(2000),itemId:id,after:side},['kind','path','after']),
    o({kind:{const:'removed'},path:t(2000),itemId:id,before:side},['kind','path','before']),
    o({kind:e('changed','reordered'),path:t(2000),itemId:id,before:side,after:side},['kind','path','before','after']),
  ]};
  s.QuoteRevisionComparison=o({quoteId:id,leftRevisionId:id,rightRevisionId:id,changes:{...a(r('QuoteRevisionChange')),maxItems:100},totalChanges:integer,nextCursor:t(2048)},['quoteId','leftRevisionId','rightRevisionId','changes','totalChanges']);
  comparison.responses[200].content={'application/json':{schema:r('QuoteRevisionComparison')}};
  comparison.parameters.push({name:'cursor',in:'query',required:false,schema:t(2048)},{name:'pageSize',in:'query',required:false,schema:{type:'integer',minimum:1,maximum:100,default:25}});
  comparison.description+=' Compare revisions belonging to this quote under current authorization. Child UUIDs determine identity; before/after paths are JSON pointers into their respective snapshots. JSON strings retain exact typed values. Paginate complete changes with a protected cursor bound to both revision IDs and current scope; never truncate a value.';
  const supportPaths=addQuoteSupportContracts({schemas:s,ref:r,text:t,enumeration:e,object:o,array:a,id,instant,operation:op,list,paths});
  for(const path of [...supportPaths,'/quotes/{quoteId}/clone-terms','/quotes','/quotes/{quoteId}','/quotes/{quoteId}/proposal','/quotes/{quoteId}/withdraw','/quotes/{quoteId}/clone','/quotes/{quoteId}/readiness','/quotes/{quoteId}/revisions','/quotes/{quoteId}/revisions/{revisionId}','/quotes/{quoteId}/compare','/quote-products'])
    for(const operation of Object.values(paths[path])) {
      operation['x-runtime-status']='planned-phase-05';
      if(operation.operationId==='listQuoteProducts') {
        operation['x-runtime-status']='implemented-capture-selection';
        operation.description+=' Offers are the current explicit Motor Trade capture catalogue. Valid empty capture settings revoke all offers; missing, malformed or dangling configuration returns 503. Eligibility is calculated for the selected relationship and does not imply rating or progression readiness.';
      }
      if(['createQuote','saveQuoteProposal','getQuote','validateQuote'].includes(operation.operationId)) {
        operation['x-runtime-status']='implemented-capture-only';
        operation['x-readiness-status']='partial-fail-closed';
        if(['createQuote','saveQuoteProposal'].includes(operation.operationId)) {
          operation.responses[413]={...operation.responses[400],description:'Quote request exceeds the UTF-8 byte limit'};
          operation.responses[415]={...operation.responses[400],description:'JSON request content type required'};
        }
        operation.description+=' Draft create/read/save are available. Readiness reports structural and term issues but remains blocked until semantic, evidence and matching assessments are implemented. Optional matchSubmissionId currently returns 409 without creating a quote.';
      }
      operation.description+=' Current stored authority and relationship eligibility precede replay lookup. Proposal bytes are limited to 1 MiB UTF-8; strict JSON and semantic validation apply. Reads never use command receipts to cache confidential snapshots. Capture eligibility is separate from rating readiness.';
      for(const [status,response] of Object.entries(operation.responses))operation.responses[status]={...response,headers:{...response.headers,'Cache-Control':{description:'Confidential quote response; never cache.',schema:{type:'string',const:'no-store'}}}};
    }
}
