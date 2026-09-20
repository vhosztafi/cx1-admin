import {readFileSync} from 'node:fs';
import {closed as o,uid as id,instant,hash,choice as e,many} from './underwriting-contract-model.mjs';
// CC capture joins the shared quote routes through an explicit closed union.
// First issue and capability-shaped advisory exposure reads are implemented.
export function addCommercialContracts({schemas,ref,operation,paths}){
 const draft=JSON.parse(readFileSync(new URL('../contracts/schemas/commercial-combined.schema.json',import.meta.url),'utf8'));
 const issued=JSON.parse(readFileSync(new URL('../contracts/schemas/commercial-combined-issued.schema.json',import.meta.url),'utf8'));
 function install(source,prefix,name){
  const rewrite=x=>{
   if(Array.isArray(x))return x.map(rewrite);if(!x||typeof x!=='object')return x;
   return Object.fromEntries(Object.entries(x).filter(([k])=>!['$schema','$id','$defs'].includes(k)).map(([k,v])=>[k,k==='$ref'&&v.startsWith('#/$defs/')?`#/components/schemas/${prefix}${v.slice(8)}`:rewrite(v)]));
  };
  for(const [key,value] of Object.entries(source.$defs))schemas[prefix+key]=rewrite(value);
  schemas[name]=rewrite(source);
 }
 install(draft,'CommercialCapture','CommercialCaptureDraft');
 install(issued,'CommercialIssued','CommercialIssuedPolicy');
 schemas.UnderwritingIssueResult.properties.commercialExposureDecisionId=id;
 schemas.UnderwritingIssueResult.properties.policyReference={type:'string',pattern:'^PL-(MT|CC)-[0-9]{10}$'};
 schemas.UnderwritingIssueResult.allOf=[{if:{properties:{policyReference:{type:'string',pattern:'^PL-CC-'}},required:['policyReference']},then:{required:['commercialExposureDecisionId'],properties:{commercialExposureDecisionId:id,documentRequestIds:{type:'array',minItems:2,maxItems:3}}},else:{not:{properties:{commercialExposureDecisionId:id},required:['commercialExposureDecisionId']},properties:{documentRequestIds:{type:'array',minItems:3,maxItems:3}}}}];
 const aggregate={type:'string',pattern:'^(0|[1-9][0-9]{0,17})\\.[0-9]{2}$'};
 const conflict=o({district:{type:'string',minLength:1,maxLength:4},startsAt:instant,endsAt:instant,ownSumInsured:aggregate,otherSumInsured:aggregate,resultingSumInsured:aggregate,
  policyCount:{type:'integer',minimum:0},code:e('commercial-exposure-limit-missing','commercial-exposure-limit-ambiguous','commercial-district-capacity-exceeded','commercial-district-authority-exceeded'),
  publishedLimit:aggregate,effectiveLimit:aggregate,headroom:{type:'string',pattern:'^-?(0|[1-9][0-9]{0,17})\\.[0-9]{2}$'},limitVersionId:id,limitHash:hash},
  ['district','startsAt','endsAt','ownSumInsured','otherSumInsured','resultingSumInsured','policyCount','code']);
 schemas.CommercialIssueCapacity=o({format:{const:'commercial-issue-capacity-1'},bookId:id,observedAt:instant,authorityLimit:aggregate,intervals:many(conflict,100,1),truncated:{type:'boolean'}});
 schemas.Problem.properties.commercialCapacity=ref('CommercialIssueCapacity');
 for(const [kind,id] of [['quotes','quoteId'],['drafts','draftId'],['policies','policyId']]){
  const path=`/${kind}/{${id}}/commercial-exposure`,name=`getCommercial${kind==='quotes'?'Quote':kind==='drafts'?'Draft':'Policy'}Exposure`;
  operation('get',path,name,'commercial-exposure-read',{output:ref('CommercialCaptureExposure'),query:[['effectiveAt',{type:'string',format:'date-time'}],['knownAt',{type:'string',format:'date-time'}]],summary:'Read scoped Commercial Combined exposure assessment'});
  const op=paths[path].get;
  op.description+=' Current parent scope is mandatory. Supply both effectiveAt and knownAt, or neither; other filters are rejected. Agency responses contain own exposure/outcome only; capable internal users may see book totals. Policy observations use the exact E/K winner; unbound quotes assess the whole proposed term against knowledge available at K. A draft without a supported projection explicitly returns unavailable. Observed-time preview grants no capacity reservation. Issue rechecks every affected interval under the common transaction lock.';
  for(const response of Object.values(op.responses))response.headers={...response.headers,'Cache-Control':{description:'Personal underwriting information is not cacheable.',schema:{type:'string',const:'no-store'}}};
 }
}
