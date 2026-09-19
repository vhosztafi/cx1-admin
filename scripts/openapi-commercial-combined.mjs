import {readFileSync} from 'node:fs';
// Planned CC contracts do not broaden the currently operational MT proposal
// routes. Their product union is activated with the implemented Phase08-02 API.
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
 for(const [kind,id,permission] of [['quotes','quoteId','quote-read'],['drafts','draftId','policy-read'],['policies','policyId','policy-read']]){
  const path=`/${kind}/{${id}}/commercial-exposure`,name=`getCommercial${kind==='quotes'?'Quote':kind==='drafts'?'Draft':'Policy'}Exposure`;
  operation('get',path,name,permission,{output:ref('CommercialCaptureExposure'),query:[['effectiveAt',{type:'string',format:'date-time'}],['knownAt',{type:'string',format:'date-time'}]],summary:'Read scoped Commercial Combined exposure assessment'});
  const op=paths[path].get;op['x-runtime-status']='phase-8-plan-10-pending';
  op.description+=' Contract only until08-10 implements the route. Current parent scope is mandatory. Agency responses contain own exposure/outcome only; capable internal users may see book totals. Observed-time preview grants no capacity reservation. Issue rechecks every affected interval under the common transaction lock.';
  for(const response of Object.values(op.responses))response.headers={...response.headers,'Cache-Control':{description:'Personal underwriting information is not cacheable.',schema:{type:'string',const:'no-store'}}};
 }
}
