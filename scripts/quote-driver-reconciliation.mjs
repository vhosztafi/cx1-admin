import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';
import {ageOn} from './quote-dynamic-options.mjs';

// Prototype full-name/employment/year declarations overlap source capture.
// Year declarations mean complete years at the local policy start. Never
// invent an exact date from a rounded year count or split a full name.
export function reconcileQuoteDriverDeclarations(proposal,questions,references) {
 quoteMappingsForProduct(questions,proposal.productCode);
 const issues=[];
 const trusted=(reference,collection)=>reference?.collection===collection&&reference.version===references.version?
  references.collections[collection]?.find(row=>row.value===reference.value&&row.text===reference.label)?.value:undefined;
 const name=value=>value.trim().replace(/\s+/g,' ').toLocaleLowerCase('en-GB');
 (proposal.risk?.drivers??[]).forEach((driver,index)=>{
  const root=`/risk/drivers/${index}`,answers=driver.responses?.answers??[];
  const entry=id=>{const index=answers.findIndex(a=>a.questionId===id);return {value:answers[index]?.value,path:`${root}/responses/answers${index<0?'':`/${index}/value`}`};};
  const add=(code,path)=>issues.push({code,path});
  if(driver.fullName&&driver.firstName&&driver.surname&&name(driver.fullName)!==name(`${driver.firstName} ${driver.surname}`))add('conflicting-driver-name',`${root}/fullName`);
  const prototype=entry('prototype.adddriver.trade-employment'),source=entry('MTS-06-Q28');
  const declared=trusted(prototype.value,'prototype.adddriver.trade-employment'),employment=trusted(source.value,'driverTradeEmploymentBasises');
  if(prototype.value!==undefined&&source.value!==undefined) {
   if(declared===undefined||employment===undefined)add('driver-employment-context-required',prototype.path);
   else if(declared!==employment){add('conflicting-driver-employment',prototype.path);add('conflicting-driver-employment',source.path);}
  }
  const other=entry('prototype.adddriver.other-occupation');
  const partTime=declared===2||employment===2;
  if(partTime&&prototype.value!==undefined&&(typeof other.value!=='string'||!other.value.trim()))add('prototype-other-occupation-required',other.path);
  if(other.value!==undefined&&!partTime)add(declared===undefined&&employment===undefined?'driver-employment-context-required':'inactive-prototype-other-occupation',other.path);
  for(const [id,date] of [
   ['prototype.adddriver.residency-years',entry('MTS-06-Q21').value===true?driver.dateOfBirth:entry('MTS-06-Q21').value===false?entry('MTS-06-Q22').value:undefined],
   ['prototype.adddriver.licence-years',[1,2].includes(trusted(driver.licence?.type,'driverLicenceTypes'))?driver.licence?.issuedOn:undefined],
  ]) {
   const field=entry(id);if(field.value===undefined)continue;
   const years=ageOn(date,proposal.termIntent?.localStartDate);
   if(years===undefined)add('driver-years-context-required',field.path);
   else if(years!==field.value)add('conflicting-driver-years',field.path);
  }
 });
 return issues;
}
