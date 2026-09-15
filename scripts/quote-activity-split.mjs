import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';
export const activitySplitKeys=['sales','servicing','mechanicalRepair','breakdownRecovery','bodyRepairs','valeting','other'];

export function validateQuoteActivitySplit(proposal,questions,references) {
 quoteMappingsForProduct(questions,proposal.productCode);
 const business=proposal.risk?.business??{},split=business.declaredActivitySplit??{},issues=[],path='/risk/business/declaredActivitySplit';
 for(const key of activitySplitKeys)if(split[key]===undefined)issues.push({code:'activity-split-share-required',path:`${path}/${key}`});
 if(activitySplitKeys.every(key=>Number.isInteger(split[key]))&&activitySplitKeys.reduce((sum,key)=>sum+split[key],0)!==10000)issues.push({code:'activity-split-total-invalid',path});
 const id='prototype.quote-value.3e4fdf2b682e',answers=business.responses?.answers??[],index=answers.findIndex(a=>a.questionId===id),detail=answers[index]?.value;
 const detailPath=`/risk/business/responses/answers${index<0?'':`/${index}/value`}`;
 if(split.other>0&&(typeof detail!=='string'||!detail.trim()))issues.push({code:'other-activity-description-required',path:detailPath,questionId:id});
 if(detail!==undefined&&!(split.other>0))issues.push({code:split.other===undefined?'other-activity-share-context-required':'inactive-other-activity-description',path:detailPath,questionId:id});
 // Only unambiguous occupations impose lower bounds on a declared bucket.
 // Source servicing/mechanical rows cannot identify the individual split.
 for(const [keys,values] of [[['sales'],[5,6,7,8,23]],[['servicing','mechanicalRepair'],[16,17,18]],[['breakdownRecovery'],[2,3]],[['bodyRepairs'],[10]],[['valeting'],[26,27]]]) {
  let minimum=0;
  for(const activity of business.activities??[]) {
   const ref=activity.code;
   const trusted=ref?.collection==='mtOccupations'&&ref.version===references.version?references.collections.mtOccupations.find(row=>row.value===ref.value&&row.text===ref.label):undefined;
   if(trusted&&values.includes(trusted.value)&&Number.isInteger(activity.turnoverBasisPoints))minimum+=activity.turnoverBasisPoints;
  }
  if(keys.every(key=>Number.isInteger(split[key]))&&minimum>keys.reduce((sum,key)=>sum+split[key],0))issues.push({code:'activity-split-below-declared-occupations',path,fields:keys});
 }
 return issues;
}
