import {quoteMappingsForProduct,validateQuoteAnswerConditions} from './quote-semantic-contract.mjs';

// The prototype Previous insurance & NCD stage is Road Risks only. Capture
// requirements do not establish entitlement or evidence approval (Phase 6/5-08).
export function validateQuotePrototypeInsurance(proposal,questions) {
 const mappings=quoteMappingsForProduct(questions,proposal.productCode);
 if(!mappings.some(row=>row.questionId==='prototype.no-claims.reason'))return [];
 const insurance=proposal.risk?.previousInsurance??{},answers=insurance.responses?.answers??[],issues=[];
 const field=id=>{const index=answers.findIndex(a=>a.questionId===id);return {value:answers[index]?.value,path:`/risk/previousInsurance/responses/answers${index<0?'':`/${index}/value`}`,questionId:id};};
 const present=value=>value!==undefined&&value!==null&&(typeof value!=='string'||value.trim().length>0);
 const add=(code,entry)=>issues.push({code,path:entry.path,...(entry.questionId?{questionId:entry.questionId}:{})});
 const claim=field('prototype.quote.1bdc05ff8b3d'),protect=field('prototype.quote.61a13bb828b2'),intro=field('prototype.quote.3fad63dd9abf'),origin=field('prototype.quote.c0650c760167');
 for(const entry of [claim,protect,intro,{value:insurance.insurer,path:'/risk/previousInsurance/insurer'}])if(!present(entry.value))add('prototype-insurance-answer-required',entry);
 if(claim.value===true) {
  for(const key of ['noClaimsYears','noClaimsYearsBasis','expiresOn'])if(!present(insurance[key]))add('prototype-insurance-answer-required',{path:`/risk/previousInsurance/${key}`});
  if(!present(origin.value))add('prototype-insurance-answer-required',origin);
  if(insurance.noClaimsYears===0)add('claimed-discount-years-must-be-positive',{path:'/risk/previousInsurance/noClaimsYears'});
 } else if(claim.value===false) {
  if(protect.value===true)add('discount-protection-without-claim',protect);
  if(origin.value!==undefined)add('inactive-discount-origin-retained',origin);
  for(const key of ['noClaimsYears','noClaimsYearsBasis'])if(insurance[key]!==undefined)add('inactive-claimed-discount-years',{path:`/risk/previousInsurance/${key}`});
 }
 issues.push(...validateQuoteAnswerConditions(proposal,mappings.filter(row=>row.questionId==='prototype.no-claims.reason')));
 return issues;
}
