import {closed as o,uid as id,instant,choice as e} from './underwriting-contract-model.mjs';

export function addPolicyTemporalContracts({schemas:s,ref:r,operation,paths}) {
 const context={effectiveCutoff:instant,knownCutoff:instant};
 Object.assign(s.FirstPolicyView.properties,{...context,coverageState:e('scheduled','active','expired','cancelled')});
 s.FirstPolicyView.required.push(...Object.keys(context),'coverageState');
 s.PolicyNotCovered=o({id,...context,coverageState:{const:'not-covered'}});
 s.PolicyTemporalView={oneOf:[r('FirstPolicyView'),r('PolicyNotCovered')]};
 for(const path of ['/policies/{policyId}','/policies/{policyId}/terms/{termId}'])
  paths[path].get.responses['200'].content['application/json'].schema=r('PolicyTemporalView');
 for(const [path,name] of [['/policies/{policyId}/as-at','getPolicyTemporalView'],['/terms/{termId}/as-at','getPolicyAsAt']]) {
  delete paths[path]?.get;
  operation('get',path,name,'policy-read',{output:r('PolicyTemporalView'),query:[['effectiveAt',instant],['knownAt',instant]]});
  const op=paths[path].get;
  op['x-runtime-status']='phase-7-02-implemented';
  op.parameters.filter(x=>x.in==='query').forEach(x=>x.required=true);
  op.description+=' Both cutoffs require ISO timestamps with explicit offsets. Only issued records visible at knownAt participate; current identity and same-policy ownership are required. No applicable known record returns not-covered without a snapshot. Responses are no-store.';
  for(const response of Object.values(op.responses))response.headers={...response.headers,'Cache-Control':{description:'Policy data is never cacheable.',schema:{type:'string',const:'no-store'}}};
 }
}
