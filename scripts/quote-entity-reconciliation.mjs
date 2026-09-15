import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';

// Legal entity and source company category have different granularity. The
// bundled source has no LLP category; MVP applies partnership relationship
// eligibility while retaining LLP as the actual legal entity (never company).
export function reconcileQuoteEntity(proposal,questions,references) {
 quoteMappingsForProduct(questions,proposal.productCode);
 const insured=proposal.insured??{},issues=[];
 if(!insured.entityType)return [{code:'legal-entity-required',path:'/insured/entityType'}];
 const ref=insured.declaredCompanyType;
 const company=ref?.collection==='companyTypes'&&ref.version===references.version?references.collections.companyTypes.find(row=>row.value===ref.value&&row.text===ref.label):undefined;
 const allowed={'sole-trader':[1],partnership:[4],'limited-company':[2,3],llp:[4]};
 if(!company)issues.push({code:'legal-entity-company-context-required',path:'/insured/declaredCompanyType'});
 else if(!allowed[insured.entityType]?.includes(company.value)) {
  for(const path of ['/insured/entityType','/insured/declaredCompanyType'])issues.push({code:'conflicting-legal-entity',path});
 }
 if(['limited-company','llp'].includes(insured.entityType)&&(typeof insured.companyNumber!=='string'||!insured.companyNumber.trim()))issues.push({code:'incorporated-company-number-required',path:'/insured/companyNumber'});
 return issues;
}
