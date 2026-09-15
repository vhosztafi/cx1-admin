import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';

// Source vehicleOwnerOptions/vehicleOwnerSelection and vehicle-schema.ts.
// All owner choices come from the current proposal and pinned reference data.
export function validateQuoteVehicleOwnership(proposal,questions,references) {
 quoteMappingsForProduct(questions,proposal.productCode);
 const issues=[],risk=proposal.risk??{},drivers=risk.drivers??[];
 const trusted=(reference,collection)=>reference?.collection===collection&&reference.version===references.version?
  references.collections[collection]?.find(row=>row.value===reference.value&&row.text===reference.label)?.value:undefined;
 const company=trusted(proposal.insured?.declaredCompanyType,'companyTypes');
 const specified=new Set((risk.specifiedVehicleIds??[]).map(id=>id.toLowerCase()));
 (risk.vehicles??[]).forEach((vehicle,index)=>{
  const path=`/risk/vehicles/${index}`;
  const add=(code,field)=>issues.push({code,path:`${path}/${field}`});
  const owner=trusted(vehicle.declaredOwnerType,'vehicleOwnerTypes');
  if(company===undefined||owner===undefined){add('vehicle-owner-context-required','declaredOwnerType');return;}
  if(owner!==4) {
   const allowed=company===1?[1]:company===4?[2]:[2,3];
   if(!allowed.includes(owner))add('vehicle-owner-type-ineligible','declaredOwnerType');
   if(vehicle.ownerDriverId!==undefined)add('inactive-owner-driver-retained','ownerDriverId');
   return;
  }
  if(!vehicle.ownerDriverId){add('vehicle-owner-driver-required','ownerDriverId');return;}
  const driver=drivers.find(row=>row.id.toLowerCase()===vehicle.ownerDriverId.toLowerCase());
  if(!driver){add('vehicle-owner-driver-not-in-proposal','ownerDriverId');return;}
  const relationship=trusted(driver.relationship,'driverRelationshipsPolicyHolder');
  if(relationship===undefined){add('vehicle-owner-relationship-context-required','ownerDriverId');return;}
  if(!specified.has(vehicle.id.toLowerCase())&&company===1&&relationship===3)add('policyholder-driver-owner-not-selectable','ownerDriverId');
  const personal=driver.responses?.answers?.find(a=>a.questionId==='MTS-06-Q31')?.value;
  // Fix the source specified-vehicle callback's missing return: a named owner
  // must have personal-vehicle cover for either vehicle branch.
  if(personal!==true)add(personal===undefined?'vehicle-owner-cover-context-required':'vehicle-owner-personal-cover-required','ownerDriverId');
 });
 return issues;
}
