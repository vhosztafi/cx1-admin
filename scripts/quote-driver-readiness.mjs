import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';
import {ageOn} from './quote-dynamic-options.mjs';

// Per-driver capture readiness after strict shape/question/reference checks.
// Source: driver-schema.ts and driver-histories.ts. Plan-level driver counts,
// dynamic age/experience options and relationship eligibility are separate.
export const requiredDriverQuestions=[8,9,10,11,12,13,14,15,18,19,20,21,23,24,25,27,28,31,32,33,40,46,48,53];
export const driverHistoryGroups=[
 {key:'occupations',parent:28,active:2,collection:'driverTradeEmploymentBasises',fields:[29,30]},
 {key:'convictions',parent:33,active:true,fields:[34,35,36,37,38]},
 {key:'losses',parent:40,active:true,fields:[41,42,43,44,45]},
 {key:'criminalConvictions',parent:48,active:true,fields:[49,50,51,52]},
 {key:'countyCourtJudgments',parent:53,active:true,fields:[54,55,56,57]},
];
const q=n=>`MTS-06-Q${String(n).padStart(2,'0')}`;
const present=v=>v!==undefined&&v!==null&&(typeof v!=='string'||v.trim().length>0);
const at=(v,path)=>path.split('.').reduce((item,key)=>item?.[key],v);

export function validateQuoteDriverReadiness(proposal,questions,references) {
 const mappings=quoteMappingsForProduct(questions,proposal.productCode);
 const issues=[];
 const trusted=(reference,collection)=>reference?.collection===collection&&reference.version===references.version?
  references.collections[collection]?.find(row=>row.value===reference.value&&row.text===reference.label)?.value:undefined;
 const mapping=n=>{const row=mappings.find(row=>row.owner===q(n));if(!row)throw new Error(`missing-driver-mapping:${q(n)}`);return row;};
 (proposal.risk?.drivers??[]).forEach((driver,index)=>{
  const root=`/risk/drivers/${index}`;
  const add=(code,path,n)=>issues.push({code,path,...(n?{questionId:q(n)}:{})});
  const read=n=>{
   const row=mapping(n);
   if(row.contractKind==='Answer') {
    const entries=driver.responses?.answers??[];const i=entries.findIndex(answer=>answer.questionId===row.questionId);
    return {value:entries[i]?.value,path:`${root}/responses/answers${i<0?'':`/${i}/value`}`};
   }
   const path=row.canonicalPath.replace('risk.drivers[].','');
   return {value:at(driver,path),path:`${root}/${path.replaceAll('.','/')}`};
  };
  const requireField=n=>{const field=read(n);if(!present(field.value))add('required-driver-field',field.path,n);};
  requiredDriverQuestions.forEach(requireField);
  const age=ageOn(driver.dateOfBirth,proposal.termIntent?.localStartDate);
  // The executable source accepts 85, despite copy saying "at most 84".
  if(age!==undefined&&(age<17||age>85))add('driver-age-out-of-range',`${root}/dateOfBirth`,11);
  if(read(21).value===false)requireField(22);
  const motorcycle=trusted(read(25).value,'driverMotorcycleCovers');
  if(motorcycle!==undefined&&motorcycle!==1)requireField(26);
  const motorcycleDate=read(26);
  if(motorcycleDate.value&&driver.licence?.issuedOn&&motorcycleDate.value<driver.licence.issuedOn)add('motorcycle-licence-before-driving-licence',motorcycleDate.path,26);
  // Correct the source's misspelled conditional parent to the actual declared
  // disability question; do not silently omit the requested explanation.
  if(read(46).value===true)requireField(47);
  if(trusted(driver.licence?.type,'driverLicenceTypes')===3)add('provisional-licence-not-covered',`${root}/licence/type`,23);
  const experience=ageOn(driver.licence?.issuedOn,proposal.termIntent?.localStartDate);
  if(age!==undefined&&age<25&&experience!==undefined&&experience<1)add('young-driver-licence-experience',`${root}/licence/issuedOn`,24);
  for(const n of [11,22,24,26]) {const field=read(n);if(field.value&&field.value<'1900-01-01')add('driver-date-too-early',field.path,n);}
  for(const group of driverHistoryGroups) {
   const parent=read(group.parent).value;
   const selected=group.collection?trusted(parent,group.collection):parent;
   const rows=driver[group.key]??[];const path=`${root}/${group.key}`;
   if(selected!==group.active) {
    if(rows.length)add(selected===undefined?'driver-history-context-required':'inactive-driver-history-retained',path,group.parent);
    continue;
   }
   if(!rows.length)add('driver-history-required',path,group.parent);
   const occupations=new Set();
   rows.forEach((item,child)=>{
    for(const n of group.fields) {
     const field=mapping(n).canonicalPath.split(`${group.key}[].`)[1];
     if(!present(at(item,field)))add('required-driver-history-field',`${path}/${child}/${field.replaceAll('.','/')}`,n);
    }
    if(item.occurredOn&&item.occurredOn<'1900-01-01')add('driver-date-too-early',`${path}/${child}/occurredOn`);
    if(group.key==='convictions'&&item.disqualified===true&&!present(item.banMonths))add('ban-length-required',`${path}/${child}/banMonths`,39);
    if(group.key==='occupations'&&item.occupation) {
     const identity=JSON.stringify([item.occupation.collection,item.occupation.value]);
     if(occupations.has(identity))add('duplicate-driver-occupation',`${path}/${child}/occupation`,29);
     occupations.add(identity);
    }
   });
  }
 });
 return issues;
}
