import {closed as o,uid as id,instant,choice as e} from './underwriting-contract-model.mjs';

export function addPolicyTemporalContracts({schemas:s,ref:r,operation,paths}) {
 const context={effectiveCutoff:instant,knownCutoff:instant};
 Object.assign(s.FirstPolicyView.properties,{...context,coverageState:e('scheduled','active','expired','cancelled')});
 s.FirstPolicyView.required.push(...Object.keys(context),'coverageState');
 // Retain the original first-issue schema; servicing reads use their own signed
 // financial shape and immutable snapshot format.
 s.ServicingPolicyFinancialView=structuredClone(s.FirstPolicyFinancialView);
 const signed={type:'string',pattern:'^-?(0|[1-9][0-9]{0,12})\\.[0-9]{2}$'};
 for(const key of ['amountDue','premium','tax','fee','brokerCommission','brokerFeeShare','insurerPayable','retainedFeeIncome','brokerRemunerationPayable'])
  s.ServicingPolicyFinancialView.properties[key]=signed;
 s.ServicingPolicyFinancialView.properties.lines.minItems=0;
 s.ServicingPolicyFinancialView.properties.lines.maxItems=3010;
 s.ServicingPolicyView=structuredClone(s.FirstPolicyView);
 s.ServicingPolicyView.properties.snapshot={$ref:'./schemas/issued-servicing.schema.json'};
 s.ServicingPolicyView.properties.financials=r('ServicingPolicyFinancialView');
 s.CancellationPolicyView=structuredClone(s.ServicingPolicyView);
 s.CancellationPolicyView.properties.snapshot={$ref:'./schemas/issued-cancellation.schema.json'};
 for(const key of ['sourceCycleId','ratingId','acceptanceId']) {
  delete s.CancellationPolicyView.properties[key];s.CancellationPolicyView.required=s.CancellationPolicyView.required.filter(x=>x!==key);
 }
 for(const key of ['cancellationDecisionId','cancellationApprovalId','cancellationPreviewId']) {
  s.CancellationPolicyView.properties[key]=id;s.CancellationPolicyView.required.push(key);
 }
 s.CommercialFirstPolicyView=structuredClone(s.FirstPolicyView);
 s.CommercialFirstPolicyView.properties.snapshot=r('CommercialIssuedPolicy');
 s.CommercialFirstPolicyView.properties.commercialExposureDecisionId=id;
 s.CommercialFirstPolicyView.required.push('commercialExposureDecisionId');
 s.CommercialServicingPolicyView=structuredClone(s.ServicingPolicyView);
 s.CommercialServicingPolicyView.properties.snapshot=r('CommercialServicingIssuedPolicy');
 s.CommercialServicingPolicyView.properties.commercialExposureDecisionId=id;
 s.CommercialServicingPolicyView.required.push('commercialExposureDecisionId');
 s.IssuedPolicyView={oneOf:[r('FirstPolicyView'),r('CommercialFirstPolicyView'),r('CommercialServicingPolicyView'),r('ServicingPolicyView'),r('CancellationPolicyView')]};
 s.PolicyNotCovered=o({id,...context,coverageState:{const:'not-covered'}});
 s.PolicyTemporalView={oneOf:[r('FirstPolicyView'),r('CommercialFirstPolicyView'),r('CommercialServicingPolicyView'),r('ServicingPolicyView'),r('CancellationPolicyView'),r('PolicyNotCovered')]};
 for(const suffix of ['versions/{versionId}','transactions/{transactionId}','obligations/{obligationId}'])
  paths[`/policies/{policyId}/terms/{termId}/${suffix}`].get.responses['200'].content['application/json'].schema=r('IssuedPolicyView');
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
