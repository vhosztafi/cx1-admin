import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';
import {ageOn} from './quote-dynamic-options.mjs';

// B2 is answered for the proposer AND every named driver, so an affirmative
// global declaration need not have a named-driver row. Only contradict a
// negative/absent global answer using positively captured applicable history.
// MVP lookback anchor: trusted assessment date, not browser time/future inception.
export function reconcileQuoteHistory(proposal,questions,references,asOfDate) {
 quoteMappingsForProduct(questions,proposal.productCode);
 if(ageOn('1900-01-01',asOfDate)===undefined)throw new Error('trusted-as-of-date-required');
 const issues=[],answers=proposal.risk?.business?.responses?.answers??[];
 const lower=years=>{let date=`${String(Number(asOfDate.slice(0,4))-years).padStart(4,'0')}${asOfDate.slice(4)}`;if(ageOn(date,asOfDate)===undefined)date=`${date.slice(0,4)}-02-28`;return date;};
 const groups=[['convictions','ef70e80708bb',5],['losses','36da21d3c935',3],['countyCourtJudgments','922ca15dc9ed',5],['criminalConvictions','46414cc10100',null]];
 const pending=conviction=>{
  const id='prototype.addconv.prosecution-status',ref=conviction.responses?.answers?.find(a=>a.questionId===id)?.value;
  return ref?.collection===id&&ref.version===references.version&&references.collections[id]?.some(row=>row.value===2&&row.value===ref.value&&row.text===ref.label);
 };
 (proposal.risk?.drivers??[]).forEach((driver,driverIndex)=>{
  for(const [key,suffix,years] of groups)for(const [index,row] of (driver[key]??[]).entries()) {
   const path=`/risk/drivers/${driverIndex}/${key}/${index}`,date=row.occurredOn;
   if(date&&date>asOfDate){issues.push({code:'history-date-after-assessment',path:`${path}/occurredOn`});continue;}
   if(!date||ageOn(date,asOfDate)===undefined)continue; // Missing/invalid shape is diagnosed by the preceding gates.
   if(years!==null&&date<lower(years)&&!(key==='convictions'&&pending(row)))continue;
   const questionId=`prototype.quote.${suffix}`,answerIndex=answers.findIndex(a=>a.questionId===questionId);
   if(answers[answerIndex]?.value!==true)issues.push({code:'history-declaration-required',path:`/risk/business/responses/answers${answerIndex<0?'':`/${answerIndex}/value`}`,questionId,relatedPath:path});
  }
 });
 return issues;
}
