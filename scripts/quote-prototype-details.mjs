import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';

// modalVals adddriver/addconv/addinc/addprem. Persist explicit default choices;
// optional years, occupation and damage amounts remain optional. Source dates,
// names and mandatory fields are also checked by their section validators.
export function validateQuotePrototypeDetails(proposal,questions) {
 const mappings=quoteMappingsForProduct(questions,proposal.productCode),issues=[];
 for(const field of questions.directReferenceFields??[]) {
  const applicable=field.products.includes(proposal.productCode);
  const [collection,property]=field.canonicalPath.replace('risk.','').split('[].');
  (proposal.risk?.[collection]??[]).forEach((row,index)=>{
   if(applicable&&row[property]===undefined)issues.push({code:'required-prototype-reference',path:`/risk/${collection}/${index}/${property}`});
   if(!applicable&&row[property]!==undefined)issues.push({code:'inapplicable-prototype-reference',path:`/risk/${collection}/${index}/${property}`});
  });
 }
 const require=(row,path,id)=>{
  if(!mappings.some(mapping=>mapping.questionId===id))return;
  const answers=row.responses?.answers??[],index=answers.findIndex(answer=>answer.questionId===id);
  if(index<0)issues.push({code:'required-prototype-detail-answer',path:`${path}/responses/answers`,questionId:id});
 };
 (proposal.risk?.drivers??[]).forEach((driver,index)=>{
  const path=`/risk/drivers/${index}`;
  const name=driver.fullName??[driver.firstName,driver.surname].filter(Boolean).join(' ');
  if(name.trim().length<3)issues.push({code:'driver-name-too-short',path:`${path}/fullName`});
  require(driver,path,'prototype.adddriver.trade-employment');
  (driver.convictions??[]).forEach((conviction,index)=>require(conviction,`${path}/convictions/${index}`,'prototype.addconv.prosecution-status'));
  (driver.losses??[]).forEach((loss,index)=>{
   const lossPath=`${path}/losses/${index}`;
   require(loss,lossPath,'prototype.addinc.claim-made');
   if(typeof loss.description!=='string'||loss.description.trim().length<10)issues.push({code:'incident-description-too-short',path:`${lossPath}/description`});
  });
 });
 (proposal.risk?.premises??[]).forEach((premise,index)=>{
  for(const id of ['prototype.addprem.overnight-vehicles','prototype.addprem.public-access'])require(premise,`/risk/premises/${index}`,id);
 });
 return issues;
}
