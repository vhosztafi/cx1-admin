export function addQuoteSupportContracts({schemas:s,ref:r,text:t,enumeration:e,object:o,array:a,id,instant,operation:op,list,paths}) {
  const hash={type:'string',pattern:'^[a-f0-9]{64}$'};
  const media=e('application/pdf','image/png','image/jpeg','text/plain');
  const fingerprint={revisionId:id,inputFingerprint:hash};
  const address=o({line1:t(200),line2:t(200),town:t(100),county:t(100),postcode:t(10),country:{const:'GB'}},['line1','town','postcode','country']);
  const vehicle=o({registration:t(12),make:t(100),model:t(100),manufactureYear:{type:'integer',minimum:1900,maximum:2200},bodyDescription:t(200),engineCc:{type:'integer',minimum:0},grossWeightKg:{type:'integer',minimum:0}},['registration','make','model','manufactureYear']);
  const licence=o({number:t(40),issuedOn:{type:'string',format:'date'},type:r('QuoteLookupReference')});
  s.QuoteLookupReference=o({collection:t(100),value:{anyOf:[{type:'integer'},t(100)]},label:t(250),version:t(100)});
  const targets={address:{oneOf:[o({kind:{const:'insured'}}),o({kind:e('driver','premises'),riskItemId:id})]},vehicle:o({kind:{const:'vehicle'},riskItemId:id}),licence:o({kind:{const:'driver'},riskItemId:id})};
  const queries={address:o({postcode:t(10),houseNumber:t(50)},['postcode']),vehicle:o({registration:t(12)}),licence:o({number:t(40),dateOfBirth:{type:'string',format:'date'}})};
  const values={address,vehicle,licence};
  const scenario=e('match','no-match','temporary-failure','permanent-failure');
  s.QuoteLookupRequest={oneOf:Object.keys(queries).map(kind=>o({kind:{const:kind},...fingerprint,target:targets[kind],query:queries[kind],scenario},['kind','revisionId','inputFingerprint','target','query']))};
  s.QuoteLookupCandidate={oneOf:Object.entries(values).map(([kind,value])=>o({id,kind:{const:kind},values:value}))};
  s.QuoteLookupView=o({id,quoteId:id,...fingerprint,kind:e('address','vehicle','licence'),state:e('pending','succeeded','no-match','failed'),candidates:{...a(r('QuoteLookupCandidate')),maxItems:50},attempts:{type:'integer',minimum:0},source:t(100),asOf:instant,errorCode:t(100)},['id','quoteId','revisionId','inputFingerprint','kind','state','candidates','attempts','source']);
  s.QuoteLookupView.allOf=[{if:{properties:{state:{const:'succeeded'}},required:['state']},then:{properties:{candidates:{type:'array',minItems:1}}},else:{properties:{candidates:{type:'array',maxItems:0}}}}];
  for(const kind of Object.keys(values))s.QuoteLookupView.allOf.push({if:{properties:{kind:{const:kind}},required:['kind']},then:{properties:{candidates:{type:'array',items:{type:'object',properties:{kind:{const:kind}}}}}}});
  s.QuoteLookupSelectionRequest={oneOf:[
    o({mode:{const:'candidate'},...fingerprint,lookupId:id,candidateId:id}),
    ...Object.entries(values).map(([kind,value])=>o({mode:{const:'manual'},kind:{const:kind},...fingerprint,target:targets[kind],values:value,reason:t(1000),lookupId:id},['mode','kind','revisionId','inputFingerprint','target','values','reason'])),
  ]};
  s.QuoteEvidenceFile=o({id,quoteId:id,fileName:t(150),contentType:media,length:{type:'integer',minimum:1,maximum:10485760},sha256:hash,uploadedAt:instant,screeningState:e('pending','accepted','rejected')});
  s.QuoteEvidenceAttachRequest=o({...fingerprint,requirementCode:t(100),riskItemId:id,fileId:id,reason:t(1000)},['revisionId','inputFingerprint','requirementCode','fileId','reason']);
  s.QuoteCaptureEvidence=o({id,quoteId:id,...s.QuoteEvidenceAttachRequest.properties,state:e('current','stale','withdrawn'),createdAt:instant,createdByLabel:t(),etag:t(100),file:r('QuoteEvidenceFile')},['id','quoteId',...s.QuoteEvidenceAttachRequest.required,'state','createdAt','createdByLabel','etag','file']);
  const replace=(method,path,...args)=>{delete paths[path]?.[method];op(method,path,...args);};
  op('post','/quotes/{quoteId}/lookups','startQuoteLookup','quote-write',{existing:true,input:r('QuoteLookupRequest'),output:r('QuoteIdentityResult'),status:202});
  op('get','/quotes/{quoteId}/lookups/{lookupId}','getQuoteLookup','quote-read',{output:r('QuoteLookupView')});
  op('post','/quotes/{quoteId}/lookup-selections','selectQuoteLookup','quote-write',{existing:true,input:r('QuoteLookupSelectionRequest'),output:r('QuoteIdentityResult')});
  op('post','/quotes/{quoteId}/evidence-files','uploadQuoteEvidenceFile','quote-write',{existing:true,output:r('QuoteIdentityResult'),status:201});
  paths['/quotes/{quoteId}/evidence-files'].post.requestBody={required:true,content:{'multipart/form-data':{schema:o({file:{type:'string',format:'binary',minLength:1,maxLength:10485760},fileName:t(150),contentType:media})}}};
  list('/quotes/{quoteId}/evidence-files','listQuoteEvidenceFiles','quote-read',r('QuoteEvidenceFile'));
  op('get','/quotes/{quoteId}/evidence-files/{fileId}/content','downloadQuoteEvidenceFile','quote-read',{output:r('QuoteEvidenceFile')});
  const download=paths['/quotes/{quoteId}/evidence-files/{fileId}/content'].get.responses[200];
  download.content={'application/octet-stream':{schema:{type:'string',format:'binary'}}};
  Object.assign(download.headers,{'Content-Disposition':{description:'Attachment with sanitized filename.',schema:t(300)},'X-Content-Type-Options':{description:'Block MIME sniffing.',schema:{const:'nosniff'}}});
  delete paths['/quotes/{quoteId}/evidence'].get;
  list('/quotes/{quoteId}/evidence','listQuoteEvidence','quote-read',r('QuoteCaptureEvidence'),[['revisionId',id]]);
  replace('post','/quotes/{quoteId}/evidence','attachQuoteEvidence','quote-write',{existing:true,input:r('QuoteEvidenceAttachRequest'),output:r('QuoteIdentityResult'),status:201});
  replace('post','/quotes/{quoteId}/evidence/{evidenceId}/withdraw','withdrawQuoteEvidence','quote-write',{existing:true,input:o({reason:t(1000)}),output:r('QuoteIdentityResult')});
  paths['/quotes/{quoteId}/evidence/{evidenceId}/withdraw'].post.parameters.find(parameter=>parameter.name==='If-Match').description='Current evidence ETag, not quote ETag; authorize and lock the owning quote before replay.';
  return Object.keys(paths).filter(path=>path.startsWith('/quotes/{quoteId}/')&&(/\/lookups|\/lookup-selections|\/evidence/.test(path)));
}
