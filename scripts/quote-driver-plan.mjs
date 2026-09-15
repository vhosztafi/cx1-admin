import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';

// Source driver-workflow.ts. The named list and any-driver count describe
// different populations; do not silently add them or discard named drivers.
export function validateQuoteDriverPlan(proposal,questions,references) {
 const mappings=quoteMappingsForProduct(questions,proposal.productCode);
 const answers=proposal.risk?.responses?.answers??[],drivers=proposal.risk?.drivers??[];
 const issues=[];
 const field=n=>{
  const questionId=`MTS-06-Q0${n}`;
  if(!mappings.some(row=>row.owner===questionId))throw new Error(`missing-driver-plan-mapping:${questionId}`);
  const index=answers.findIndex(answer=>answer.questionId===questionId);
  return {value:answers[index]?.value,path:`/risk/responses/answers${index<0?'':`/${index}/value`}`,questionId};
 };
 const add=(code,entry)=>issues.push({code,path:entry.path,...(entry.questionId?{questionId:entry.questionId}:{})});
 const selection=field(1),reference=selection.value;
 const plan=reference?.collection==='driverPlans'&&reference.version===references.version?
  references.collections.driverPlans.find(row=>row.value===reference.value&&row.text===reference.label)?.value:undefined;
 if(plan===undefined)add(reference===undefined?'driver-plan-required':'driver-plan-context-required',selection);
 for(let n=2;n<=7;n++) {
  const entry=field(n);
  if([2,3].includes(plan)&&entry.value===undefined)add('any-driver-answer-required',entry);
  else if(![2,3].includes(plan)&&entry.value!==undefined)add(plan===undefined?'any-driver-context-required':'inactive-any-driver-answer-retained',entry);
 }
 if([2,3].includes(plan)&&field(2).value!==undefined&&(!Number.isInteger(field(2).value)||field(2).value<1))add('positive-any-driver-count-required',field(2));
 if([1,2].includes(plan)&&!drivers.length)add('named-driver-required',{path:'/risk/drivers'});
 if(plan===3&&drivers.length)add('inactive-named-drivers-retained',{path:'/risk/drivers'});
 if(plan===undefined&&drivers.length)add('named-driver-plan-context-required',{path:'/risk/drivers'});
 return issues;
}
