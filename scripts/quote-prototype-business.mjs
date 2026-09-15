import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';

// Prototype pNewQuote: experience (3613–3628), appetite (3650–3658),
// vehicle characteristics (4062–4066). Rating referrals belong to Phase 6.
export const prototypeBusinessGroups=[
 {parents:['ba9d4158ae2c','35350a32e79e','2ad460339240','f5e77ab8ec93','7fe8e3151553','27322dcfabf5','f856f5891026'],details:'909e1c6eff8c'},
 {parents:['d7a75768e505','5f9e8331ac6f','488ecf4bdc09','87fad6b4a9fe'],details:'2d662a3ec81d'},
];
export function validateQuotePrototypeBusiness(proposal,questions,references) {
 quoteMappingsForProduct(questions,proposal.productCode);
 const answers=proposal.risk?.business?.responses?.answers??[],issues=[];
 const entry=id=>{const index=answers.findIndex(a=>a.questionId===id);return {value:answers[index]?.value,path:`/risk/business/responses/answers${index<0?'':`/${index}/value`}`};};
 const present=value=>value!==undefined&&value!==null&&(typeof value!=='string'||value.trim().length>0);
 const add=(code,id)=>issues.push({code,path:entry(id).path,questionId:id});
 const require=id=>{if(!present(entry(id).value))add('required-prototype-business-answer',id);};
 const pinned=id=>{const value=entry(id).value;return value?.collection===id&&value.version===references.version?references.collections[id]?.find(row=>row.value===value.value&&row.text===value.label)?.value:undefined;};
 const trader='prototype.quote.c6181a11c34c',experience='prototype.quote.0552d5a68ba2',employment='prototype.quote.34613c23e95d',occupation='prototype.quote-value.3fd9edd7e66e';
 for(const id of [trader,experience,employment])require(id);
 if(pinned(trader)===2) {
  require(occupation);
  if(pinned(employment)===1)add('part-time-employment-required',employment);
 } else if(pinned(trader)===1) {
  if(present(entry(occupation).value))add('inactive-main-occupation-retained',occupation);
  if([2,3].includes(pinned(employment)))add('inactive-main-employment-retained',employment);
 } else if(present(entry(occupation).value))add('main-occupation-context-required',occupation);
 for(const group of prototypeBusinessGroups) {
  const parents=group.parents.map(id=>`prototype.quote.${id}`),details=`prototype.quote-value.${group.details}`;
  parents.forEach(require);
  if(parents.some(id=>entry(id).value===true))require(details);
  else if(present(entry(details).value))add(parents.every(id=>entry(id).value===false)?'inactive-prototype-details-retained':'prototype-details-context-required',details);
 }
 return issues;
}
