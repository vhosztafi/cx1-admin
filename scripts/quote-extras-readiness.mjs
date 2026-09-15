import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';
import {reconcileQuoteCover} from './quote-cover-reconciliation.mjs';
import {validateQuoteTerm} from './quote-term-contract.mjs';
import {annualEndDate} from './design-rules.mjs';

// Source extras.ts; capture never trusts its isDraft bypass or six-month
// assumed short term. Trip bounds use the actual validated capture term.
export function validateQuoteExtrasReadiness(proposal,questions,references) {
 quoteMappingsForProduct(questions,proposal.productCode);
 const issues=[],cover=proposal.cover??{},drivers=proposal.risk?.drivers??[];
 const add=(code,path)=>issues.push({code,path});
 const answers=cover.responses?.answers??[];
 const entry=n=>{const id=`MTS-11-Q${String(n).padStart(2,'0')}`,index=answers.findIndex(a=>a.questionId===id);return {value:answers[index]?.value,path:`/cover/responses/answers${index<0?'':`/${index}/value`}`};};
 const present=v=>v!==undefined&&v!==null&&(typeof v!=='string'||v.trim().length>0);
 const condition=(n,active)=>{const field=entry(n);if(active===true&&!present(field.value))add('extras-answer-required',field.path);else if(active!==true&&field.value!==undefined)add(active===undefined?'extras-context-required':'inactive-extras-answer-retained',field.path);};
 const trusted=(reference,collection)=>reference?.collection===collection&&reference.version===references.version?references.collections[collection]?.find(row=>row.value===reference.value&&row.text===reference.label):undefined;
 const level=reconcileQuoteCover(proposal,references).facts.coverLevel;
 const comprehensive=(reference,collection,path)=>{if(trusted(reference,collection)?.isComprehensive&&level!=='comprehensive')add('extras-comprehensive-cover-required',path);};
 for(const n of [7,8,9])condition(n,true);
 const demonstration=entry(1).value;
 condition(2,demonstration===undefined?undefined:demonstration===true);
 const motorcycle=drivers.some(driver=>driver.responses?.answers?.some(a=>a.questionId==='MTS-06-Q26'&&present(a.value)));
 condition(3,demonstration===false||!motorcycle?false:demonstration===undefined?undefined:true);
 comprehensive(entry(2).value,'demonstrationCovers',entry(2).path);
 const windscreen=entry(4).value;
 for(const n of [5,6])condition(n,windscreen===undefined?undefined:windscreen===true);
 if(windscreen===true&&level!=='comprehensive')add('extras-comprehensive-cover-required',entry(4).path);
 const planAnswer=proposal.risk?.responses?.answers?.find(a=>a.questionId==='MTS-06-Q01');
 const plan=trusted(planAnswer?.value,'driverPlans');
 const term=validateQuoteTerm(proposal.termIntent);
 const endDate=term.term?(proposal.termIntent.kind==='annual'?annualEndDate(proposal.termIntent.localStartDate):proposal.termIntent.localEndDate):undefined;
 for(const [key,n,fields] of [['annualEuropeanCover',10,['registration','usage']],['temporaryEuropeanCover',13,['registration','startsOn','endsOn','area','cover','usage']]]) {
  const rows=cover[key]??[],selected=entry(n).value,path=`/cover/${key}`;
  if(selected===true&&!rows.length)add('european-cover-row-required',path);
  if(selected!==true&&rows.length)add(selected===undefined?'european-cover-context-required':'inactive-european-cover-retained',path);
  rows.forEach((trip,index)=>{
   const root=`${path}/${index}`;
   for(const field of fields)if(!present(trip[field]))add('european-trip-field-required',`${root}/${field}`);
   const social=trusted(trip.usage,'europeanTripUsage')?.isSocialDomesticPleasure;
   if(!plan)add('european-driver-plan-required',root);
   if(key==='temporaryEuropeanCover') {
    if(plan&&plan.value!==3&&!trip.driverIds?.length)add('european-trip-drivers-required',`${root}/driverIds`);
    if(!term.term)add('european-trip-term-required',root);
    else {
     if(trip.startsOn&&trip.startsOn<proposal.termIntent.localStartDate)add('european-trip-before-policy',`${root}/startsOn`);
     if(trip.endsOn&&trip.endsOn>endDate)add('european-trip-after-policy',`${root}/endsOn`);
    }
    if(trip.startsOn&&trip.endsOn&&trip.endsOn<=trip.startsOn)add('european-trip-end-must-follow-start',`${root}/endsOn`);
    comprehensive(trip.cover,'europeanTripCover',`${root}/cover`);
    if(social&&plan?.value!==3)for(const id of trip.driverIds??[]) {
     const driver=drivers.find(driver=>driver.id.toLowerCase()===id.toLowerCase());
     const usage=trusted(driver?.usage,'driverUsages');
     if(!usage)add('european-driver-usage-context-required',`${root}/driverIds`);
     else if(usage.isSocialDomesticPleasure===false)add('european-trip-usage-ineligible',`${root}/usage`);
    }
   }
   if(social&&plan?.value===3)add('european-trip-usage-ineligible',`${root}/usage`);
  });
 }
 return issues;
}
