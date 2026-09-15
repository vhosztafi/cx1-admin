import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';
import {ageOn} from './quote-dynamic-options.mjs';

// Source driver-rules.ts. Run schema, identity, question/reference and required
// field checks first. No relationship, usage or motorcycle ID is trusted alone.
export function validateQuoteDriverEligibility(proposal,questions,references) {
 quoteMappingsForProduct(questions,proposal.productCode);
 const issues=[];
 const trusted=(reference,collection)=>reference?.collection===collection&&reference.version===references.version?
  references.collections[collection]?.find(row=>row.value===reference.value&&row.text===reference.label):undefined;
 const company=trusted(proposal.insured?.declaredCompanyType,'companyTypes');
 const drivers=proposal.risk?.drivers??[];
 const policyholders=drivers.filter(driver=>trusted(driver.relationship,'driverRelationshipsPolicyHolder')?.value===3);
 drivers.forEach((driver,index)=>{
  const root=`/risk/drivers/${index}`;
  const add=(code,field)=>issues.push({code,path:`${root}/${field}`});
  const answer=n=>driver.responses?.answers?.find(a=>a.questionId===`MTS-06-Q${n}`)?.value;
  const relationship=trusted(driver.relationship,'driverRelationshipsPolicyHolder')?.value;
  const usage=trusted(driver.usage,'driverUsages')?.value;
  const motorcycle=trusted(answer(25),'driverMotorcycleCovers')?.value;
  const age=ageOn(driver.dateOfBirth,proposal.termIntent?.localStartDate);
  if(!company||relationship===undefined)add('driver-relationship-context-required','relationship');
  else if(!company.relationshipOptions?.includes(relationship))add('driver-relationship-ineligible','relationship');
  if(relationship===5&&age!==undefined&&age<25)add('spouse-under-25','relationship');
  if(relationship===3&&policyholders.length>1)add('duplicate-policyholder-driver','relationship');
  if(motorcycle===6) {
   const experience=ageOn(answer(26),proposal.termIntent?.localStartDate);
   if(age===undefined||experience===undefined)add('motorcycle-eligibility-context-required','responses/answers');
   else if(age<30||experience<2)add('motorcycle-over-1000-ineligible','responses/answers');
  }
  const personal=answer(31),other=answer(32);
  if((personal!==undefined||other!==undefined)&&usage===undefined)add('driver-usage-context-required','usage');
  if(personal===true&&usage===1)add('personal-cover-motor-trade-only','responses/answers');
  if(personal===false&&[1,4,3,5].includes(relationship)&&usage!==undefined&&usage!==1)add('personal-cover-required-for-relationship','responses/answers');
  if(other===true&&usage===1)add('other-cover-motor-trade-only','responses/answers');
  if(other===true&&age!==undefined&&age<21)add('other-cover-under-21','responses/answers');
  if(other===false&&age!==undefined&&age>=21&&[1,4,3].includes(relationship)&&usage!==undefined&&usage!==1)add('other-cover-required-for-relationship','responses/answers');
  (driver.convictions??[]).forEach((conviction,child)=>{
   if(conviction.disqualified!==true||!conviction.occurredOn||!Number.isInteger(conviction.banMonths))return;
   const [year,month,day]=conviction.occurredOn.split('-').map(Number);
   const monthIndex=year*12+month-1+conviction.banMonths;
   const endYear=Math.floor(monthIndex/12),endMonth=monthIndex%12;
   const lastDay=new Date(Date.UTC(endYear,endMonth+1,0)).getUTCDate();
   const end=Date.UTC(endYear,endMonth,Math.min(day,lastDay));
   if(!proposal.termIntent?.localStartDate)add('ban-policy-start-required',`convictions/${child}/banMonths`);
   else if(end>Date.parse(`${proposal.termIntent.localStartDate}T00:00:00Z`))add('driver-ban-active-at-policy-start',`convictions/${child}/banMonths`);
  });
 });
 return issues;
}
