import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';
import {reconcileQuoteCover,coverReconciliationGroups} from './quote-cover-reconciliation.mjs';
import {selectQuoteDynamicOptions} from './quote-dynamic-options.mjs';

// Source cover.ts requiredness and customer-loan eligibility. Strict proposal,
// question/reference validation is still required; returned dynamic context is
// server-derived and must be used by the reference validator.
export function validateQuoteCoverReadiness(proposal,questions,references) {
 quoteMappingsForProduct(questions,proposal.productCode);
 const answers=proposal.cover?.responses?.answers??[];
 const {facts}=reconcileQuoteCover(proposal,references);
 const dynamic=selectQuoteDynamicOptions(proposal,references);
 const issues=[...dynamic.issues];
 const add=(code,path,fact)=>issues.push({code,path,...(fact?{fact}:{})});
 const entry=id=>{const index=answers.findIndex(answer=>answer.questionId===id);return {value:answers[index]?.value,path:`/cover/responses/answers${index<0?'':`/${index}/value`}`};};
 const present=value=>value!==undefined&&value!==null&&(typeof value!=='string'||value.trim().length>0);
 const condition=(id,active)=>{
  const field=entry(id);
  if(active===true&&!present(field.value))add('cover-answer-required',field.path,id);
  else if(active!==true&&field.value!==undefined)add(active===undefined?'cover-context-required':'inactive-cover-answer-retained',field.path,id);
 };
 const trusted=(reference,collection)=>reference?.collection===collection&&reference.version===references.version?
  references.collections[collection]?.find(row=>row.value===reference.value&&row.text===reference.label):undefined;
 const hasLimits=facts.coverLevel===undefined?undefined:facts.coverLevel!=='third-party-only';
 if(facts.coverLevel===undefined)add('cover-level-required','/cover/responses/answers','coverLevel');
 const activities=proposal.risk?.business?.activities;
 const rows=activities?.map(activity=>trusted(activity.code,'mtOccupations'));
 const complete=rows?.length>0&&rows.every(Boolean);
 const customerRequired=hasLimits===false?false:hasLimits===undefined||!complete?undefined:rows.some(row=>row.customerLOI);
 const factCondition=(fact,active)=>{
  const group=coverReconciliationGroups.find(group=>group.fact===fact);
  if(active===true&&facts[fact]===undefined)add('cover-fact-required','/cover/responses/answers',fact);
  if(active!==true)for(const id of [group.source,group.prototype]) {
   const field=entry(id);if(field.value!==undefined)add(active===undefined?'cover-context-required':'inactive-cover-answer-retained',field.path,fact);
  }
 };
 factCondition('ownVehicleLimit',hasLimits);factCondition('excess',hasLimits);factCondition('customerVehicleLimit',customerRequired);
 if(hasLimits===true&&!complete)add('cover-activity-context-required','/risk/business/activities');
 for(const id of ['MTS-05-Q05','MTS-05-Q06','MTS-05-Q07'])condition(id,hasLimits);
 // Disabled source controls still have fixed, explicit persisted values.
 if(hasLimits===true) {
  const allSections=entry('MTS-05-Q05');if(allSections.value!==undefined&&allSections.value!==false)add('unsupported-all-sections-excess',allSections.path);
  const late=entry('MTS-05-Q06'),selected=trusted(late.value,'excessLates');
  if(selected&&selected.value!==1)add('unsupported-late-notification-excess',late.path);
 }
 if(facts.coverLevel==='third-party-fire-theft')for(const fact of ['ownVehicleLimit','customerVehicleLimit']) {
  if(facts[fact]!==undefined&&BigInt(facts[fact].replace('.',''))>1500000n)add('fire-theft-limit-exceeded','/cover/responses/answers',fact);
 }
 condition('MTS-05-Q08',true);
 const loan=entry('MTS-05-Q08').value;
 condition('MTS-05-Q09',loan===undefined?undefined:loan===true);
 const loanLevel=trusted(entry('MTS-05-Q09').value,'customerLoanCoverLevels');
 if(loan===true&&loanLevel?.isComprehensive&&facts.coverLevel!=='comprehensive')add('customer-loan-cover-ineligible',entry('MTS-05-Q09').path);
 (proposal.risk?.vehicles??[]).forEach((vehicle,index)=>{
  if(vehicle.customerLoan===true&&loan!==true)add(loan===undefined?'customer-loan-context-required':'customer-loan-cover-required',`/risk/vehicles/${index}/customerLoan`);
 });
 return {issues,selectedCollections:dynamic.selectedCollections};
}
