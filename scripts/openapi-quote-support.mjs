export function addQuoteSupportContracts({schemas:s,ref:r,text:t,enumeration:e,object:o,array:a,id,instant,operation:op,list,paths}) {
  const hash={type:'string',pattern:'^[a-f0-9]{64}$'};
  const media=e('application/pdf','image/png','image/jpeg','text/plain');
  const fingerprint={revisionId:id,inputFingerprint:hash};
  const nullable=schema=>({anyOf:[schema,{type:'null'}]});
  const address=o({address:o({houseNumber:t(50),street:t(50),town:t(50),county:t(50),postcode:t(10)})});
  const vehicle=o({make:t(100),model:t(100)}),licence=o({});
  const values={address,vehicle,licence};
  const scenario=e('success','no-match','multiple','reject','fail-once','timeout-after-success');
  const targets=[{kind:{const:'address'},scope:{const:'insured'}},{kind:{const:'address'},scope:e('driver','premises'),riskItemId:id},{kind:{const:'vehicle'},scope:{const:'vehicle'},riskItemId:id},{kind:{const:'licence'},scope:{const:'driver'},riskItemId:id}];
  s.QuoteLookupRequest={oneOf:targets.map(target=>o({revisionId:id,...target,scenario}))};
  s.QuoteLookupCandidate=o({id,label:t(250),patch:{oneOf:Object.values(values)}});
  s.QuoteLookupView=o({id,...fingerprint,kind:e('address','vehicle','licence'),scope:e('insured','driver','vehicle','premises'),riskItemId:nullable(id),scenario,
    state:e('pending','succeeded','no-match','rejected','failed'),createdAt:instant,completedAt:nullable(instant),
    candidates:{...a(r('QuoteLookupCandidate')),maxItems:2},attempts:{type:'integer',minimum:0},workState:e('pending','leased','succeeded','failed'),
    source:nullable(t(100)),asOf:nullable(instant),errorCode:nullable(t(100)),nextAttemptAt:nullable(instant),selectedRevisionId:nullable(id)});
  s.QuoteLookupView.allOf=[{if:{properties:{state:{const:'succeeded'}},required:['state']},then:{properties:{candidates:{type:'array',minItems:1}}},else:{properties:{candidates:{type:'array',maxItems:0}}}}];
  for(const [kind,patch] of Object.entries(values))s.QuoteLookupView.allOf.push({if:{properties:{kind:{const:kind}},required:['kind']},then:{properties:{candidates:{type:'array',items:{type:'object',properties:{patch}}}}}});
  s.QuoteLookupSelectionRequest={oneOf:[o({...fingerprint,lookupId:id,candidateId:id}),o({...fingerprint,lookupId:id,manualReason:{...t(1000),pattern:'\\S'}})]};
  s.QuoteEvidenceFile=o({id,quoteId:id,fileName:t(150),contentType:media,length:{type:'integer',minimum:1,maximum:10485760},sha256:hash,uploadedAt:instant,screeningState:e('pending','accepted','rejected')});
  s.QuoteEvidenceAttachRequest=o({...fingerprint,requirementCode:t(100),riskItemId:id,fileId:id,reason:t(1000)},['revisionId','inputFingerprint','requirementCode','fileId','reason']);
  s.QuoteCaptureEvidence=o({id,quoteId:id,...s.QuoteEvidenceAttachRequest.properties,state:e('current','stale','withdrawn'),createdAt:instant,createdByLabel:t(),etag:t(100),file:r('QuoteEvidenceFile')},['id','quoteId',...s.QuoteEvidenceAttachRequest.required,'state','createdAt','createdByLabel','etag','file']);
  const replace=(method,path,...args)=>{delete paths[path]?.[method];op(method,path,...args);};
  op('post','/quotes/{quoteId}/lookups','startQuoteLookup','quote-capture',{existing:true,input:r('QuoteLookupRequest'),output:r('QuoteIdentityResult'),status:202});
  op('get','/quotes/{quoteId}/lookups','listQuoteLookups','quote-read',{output:o({items:{...a(r('QuoteLookupView')),maxItems:100}})});
  op('get','/quotes/{quoteId}/lookups/{lookupId}','getQuoteLookup','quote-read',{output:r('QuoteLookupView')});
  op('post','/quotes/{quoteId}/lookup-selections','selectQuoteLookup','quote-capture',{existing:true,input:r('QuoteLookupSelectionRequest'),output:r('QuoteIdentityResult')});
  op('post','/quotes/{quoteId}/evidence-files','uploadQuoteEvidenceFile','quote-capture',{existing:true,output:r('QuoteIdentityResult'),status:201});
  paths['/quotes/{quoteId}/evidence-files'].post.requestBody={required:true,content:{'multipart/form-data':{schema:o({file:{type:'string',format:'binary',minLength:1,maxLength:10485760},fileName:t(150),contentType:media})}}};
  list('/quotes/{quoteId}/evidence-files','listQuoteEvidenceFiles','quote-read',r('QuoteEvidenceFile'));
  op('get','/quotes/{quoteId}/evidence-files/{fileId}/content','downloadQuoteEvidenceFile','quote-read',{output:r('QuoteEvidenceFile')});
  const download=paths['/quotes/{quoteId}/evidence-files/{fileId}/content'].get.responses[200];
  download.content={'application/octet-stream':{schema:{type:'string',format:'binary'}}};
  Object.assign(download.headers,{'Content-Disposition':{description:'Attachment with sanitized filename.',schema:t(300)},'X-Content-Type-Options':{description:'Block MIME sniffing.',schema:{const:'nosniff'}}});
  delete paths['/quotes/{quoteId}/evidence'].get;
  list('/quotes/{quoteId}/evidence','listQuoteEvidence','quote-read',r('QuoteCaptureEvidence'),[['revisionId',id]]);
  replace('post','/quotes/{quoteId}/evidence','attachQuoteEvidence','quote-capture',{existing:true,input:r('QuoteEvidenceAttachRequest'),output:r('QuoteIdentityResult'),status:201});
  replace('post','/quotes/{quoteId}/evidence/{evidenceId}/withdraw','withdrawQuoteEvidence','quote-capture',{existing:true,input:o({reason:t(1000)}),output:r('QuoteIdentityResult')});
  paths['/quotes/{quoteId}/evidence/{evidenceId}/withdraw'].post.parameters.find(parameter=>parameter.name==='If-Match').description='Current evidence ETag, not quote ETag; authorize and lock the owning quote before replay.';
  return Object.keys(paths).filter(path=>path.startsWith('/quotes/{quoteId}/')&&(/\/lookups|\/lookup-selections|\/evidence/.test(path)));
}
