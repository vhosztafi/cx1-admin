import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';

// Explicit semantic matches between the pinned occupation catalogue and the
// prototype's activity declarations. Do not infer "specialises in" from a
// small turnover share, or infer salvage from ordinary repair/restoration.
const declarations=[
 {values:[5,6,16,17],questionId:'prototype.quote.35350a32e79e'},
 {values:[13],questionId:'prototype.quote.2ad460339240'},
 {values:[23,29],questionId:'prototype.quote.ba9d4158ae2c'},
];
export function reconcileQuoteActivityDeclarations(proposal,questions,references) {
 quoteMappingsForProduct(questions,proposal.productCode);
 const business=proposal.risk?.business??{},answers=business.responses?.answers??[],issues=[];
 const emitted=new Set();
 (business.activities??[]).forEach((activity,index)=>{
  const ref=activity.code;
  const trusted=ref?.collection==='mtOccupations'&&ref.version===references.version?references.collections.mtOccupations?.find(row=>row.value===ref.value&&row.text===ref.label):undefined;
  if(!trusted||!(activity.turnoverBasisPoints>0))return;
  for(const rule of declarations.filter(rule=>rule.values.includes(trusted.value))) {
   const answerIndex=answers.findIndex(a=>a.questionId===rule.questionId);
   if(answers[answerIndex]?.value!==true&&!emitted.has(rule.questionId)) {
    issues.push({code:'activity-declaration-required',questionId:rule.questionId,path:`/risk/business/responses/answers${answerIndex<0?'':`/${answerIndex}/value`}`,relatedPath:`/risk/business/activities/${index}/code`});emitted.add(rule.questionId);
   }
  }
 });
 return issues;
}
