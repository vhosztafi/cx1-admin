import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';
import {reconcileQuoteCover} from './quote-cover-reconciliation.mjs';

// Source additional-information.ts. Applicability is derived from pinned
// activity/driver/cover metadata; retained inactive answers are never deleted.
export function validateQuoteAdditionalReadiness(proposal,questions,references) {
 quoteMappingsForProduct(questions,proposal.productCode);
 const issues=[];
 const add=(code,path)=>issues.push({code,path});
 const trusted=(reference,collection)=>reference?.collection===collection&&reference.version===references.version?
  references.collections[collection]?.find(row=>row.value===reference.value&&row.text===reference.label):undefined;
 const risk=proposal.risk??{},answers=risk.responses?.answers??[];
 const get=id=>{const index=answers.findIndex(a=>a.questionId===id);return {value:answers[index]?.value,path:`/risk/responses/answers${index<0?'':`/${index}/value`}`};};
 const rows=risk.business?.activities?.map(activity=>trusted(activity.code,'mtOccupations'))??[];
 const complete=rows.length>0&&rows.every(Boolean);
 const jockey=complete?rows.some(row=>row.requireCarJockeyRadius):undefined;
 const radius=get('MTS-13-Q02');
 if(jockey===true&&radius.value===undefined)add('car-jockey-radius-required',radius.path);
 if(jockey!==true&&radius.value!==undefined)add(jockey===undefined?'additional-activity-context-required':'inactive-car-jockey-radius-retained',radius.path);
 const shunterAnswer=get('MTS-13-Q03');
 if(shunterAnswer.value!==undefined) {
  if(!complete)add('additional-activity-context-required',shunterAnswer.path);
  else if(rows.length!==1||!rows[0].requireShunterRadius)add('inactive-shunter-radius-retained',shunterAnswer.path);
  else {
   const plan=trusted(get('MTS-06-Q01').value,'driverPlans');
   const usages=(risk.drivers??[]).map(driver=>trusted(driver.usage,'driverUsages'));
   const knownUsages=usages.length>0&&usages.every(Boolean);
   const driverEligible=plan?.value===3?true:!plan||!knownUsages?undefined:usages.every(row=>row.isSocialDomesticPleasure===false);
   const {facts}=reconcileQuoteCover(proposal,references);
   const coverEligible=facts.coverLevel==='third-party-only'||facts.ownVehicleLimit==='0.00'?true:
    facts.coverLevel===undefined||facts.ownVehicleLimit===undefined?undefined:false;
   if(driverEligible===false||coverEligible===false)add('inactive-shunter-radius-retained',shunterAnswer.path);
   else if(driverEligible===undefined||coverEligible===undefined)add('shunter-eligibility-context-required',shunterAnswer.path);
  }
 }
 if(typeof risk.materialFacts==='string'&&risk.materialFacts.length>1000)add('material-facts-too-long','/risk/materialFacts');
 return issues;
}
