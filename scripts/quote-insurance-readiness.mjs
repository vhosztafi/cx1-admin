import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';

// Source cover.ts NCB capture rules. No evidence verification or pricing is
// implied. Strict shape/question/reference validation must run first.
export function validateQuoteInsuranceReadiness(proposal,questions,references) {
 const mappings=quoteMappingsForProduct(questions,proposal.productCode);
 const insurance=proposal.risk?.previousInsurance??{};
 const answers=insurance.responses?.answers??[];
 const issues=[];
 const read=n=>{
  const questionId=`MTS-05-Q${n}`;
  const row=mappings.find(row=>row.owner===questionId);
  if(!row)throw new Error(`missing-insurance-mapping:${questionId}`);
  if(n===12)return {value:insurance.noClaimsBonusExpiresOn,path:'/risk/previousInsurance/noClaimsBonusExpiresOn',questionId};
  const index=answers.findIndex(answer=>answer.questionId===questionId);
  return {value:answers[index]?.value,path:`/risk/previousInsurance/responses/answers${index<0?'':`/${index}/value`}`,questionId};
 };
 const present=value=>value!==undefined&&value!==null&&(typeof value!=='string'||value.trim().length>0);
 const add=(code,field)=>issues.push({code,path:field.path,questionId:field.questionId});
 const trusted=(n,collection)=>{
  const reference=read(n).value;
  if(reference?.collection!==collection||reference.version!==references.version)return undefined;
  return references.collections[collection]?.find(row=>row.value===reference.value&&row.text===reference.label);
 };
 const condition=(n,active)=>{
  const field=read(n);
  if(active===true&&!present(field.value))add('insurance-answer-required',field);
  else if(active!==true&&field.value!==undefined)add(active===undefined?'insurance-context-required':'inactive-insurance-answer-retained',field);
 };
 condition(10,true);
 const ncb=trusted(10,'noClaimBonuses');
 const hasNcb=ncb===undefined?undefined:ncb.value!==1;
 for(const n of [11,12,13])condition(n,hasNcb);
 const earned=trusted(11,'noClaimBonusesEarned');
 const insurer=trusted(13,'noClaimBonusPreviousInsurers');
 const other=hasNcb===false?false:hasNcb===undefined||!insurer?undefined:insurer.value===21;
 condition(16,other);
 const protect=hasNcb===false?false:hasNcb===undefined||!earned?undefined:earned.value===3;
 condition(17,protect);
 const intro=hasNcb===false||ncb&&ncb.value!==2||earned&&earned.value!==3?false:
  !ncb||!earned||!insurer?undefined:insurer.requireNCBIntroRenewal===true;
 condition(14,intro);
 const declaredIntro=read(14).value;
 condition(15,intro===false?false:intro===undefined||declaredIntro===undefined?undefined:declaredIntro===true);
 if(insurance.noClaimsBonusExpiresOn&&insurance.noClaimsBonusExpiresOn<'1900-01-01')add('ncb-expiry-too-early',read(12));
 const otherDetails=read(16);
 if(typeof otherDetails.value==='string'&&otherDetails.value.length>50)add('insurer-details-too-long',otherDetails);
 return issues;
}
