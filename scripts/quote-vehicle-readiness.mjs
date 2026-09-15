import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';

// Source: vehicle-schema.ts. modes is trusted persisted lookup/manual-selection
// context keyed by lowercase vehicle UUID; it must never come from proposal JSON.
// Vehicle/driver limit eligibility and lookup fingerprint freshness are separate.
export const requiredVehicleFields=['registration','abiCode','value','purchasedOn','declaredOwnerType','keptOvernightType','keptOvernightAddress','partOfLeaseAgreement','modified'];
export const manualVehicleFields=['make','model','vehicleType','bodyDescription','registrationYear','registeredOn','imported'];
const present=value=>value!==undefined&&value!==null&&(typeof value!=='string'||value.trim().length>0);
export function validateQuoteVehicleReadiness(proposal,questions,references,modes={}) {
 quoteMappingsForProduct(questions,proposal.productCode);
 const issues=[];
 const add=(code,path)=>issues.push({code,path});
 const risk=proposal.risk??{};
 const specified=new Set((risk.specifiedVehicleIds??[]).map(id=>id.toLowerCase()));
 if(risk.specifiedVehiclesRequested===undefined)add('specified-vehicle-declaration-required','/risk/specifiedVehiclesRequested');
 if(risk.specifiedVehiclesRequested===true&&!specified.size)add('specified-vehicle-required','/risk/specifiedVehicleIds');
 if(risk.specifiedVehiclesRequested===false&&specified.size)add('inactive-specified-vehicles-retained','/risk/specifiedVehicleIds');
 const trusted=(reference,collection)=>reference?.collection===collection&&reference.version===references.version?
  references.collections[collection]?.find(row=>row.value===reference.value&&row.text===reference.label):undefined;
 (risk.vehicles??[]).forEach((vehicle,index)=>{
  const root=`/risk/vehicles/${index}`,isSpecified=specified.has(vehicle.id.toLowerCase());
  const requireField=field=>{if(!present(vehicle[field]))add('required-vehicle-field',`${root}/${field}`);};
  requiredVehicleFields.forEach(requireField);
  if(!isSpecified)requireField('customerLoan');
  const mode=Object.hasOwn(modes,vehicle.id.toLowerCase())?modes[vehicle.id.toLowerCase()]:undefined;
  if(!['manual','lookup'].includes(mode))add('vehicle-capture-context-required',root);
  if(mode==='manual')manualVehicleFields.forEach(requireField);
  const type=trusted(vehicle.vehicleType,'vehicleType');
  if(!type)add('vehicle-category-context-required',`${root}/vehicleType`);
  else {
   if(!['Motorcycle','Commercial','Other'].includes(type.category))requireField('abiGroup');
   if(mode==='manual'&&type.category!=='Car') {
    requireField('declaredEngineSize');
    if(type.category!=='Other')requireField('grossWeightKg');
   }
  }
  if(vehicle.registration&&!/^[A-Za-z0-9 ]+$/.test(vehicle.registration))add('vehicle-registration-invalid',`${root}/registration`);
  if(vehicle.partOfLeaseAgreement===true)requireField('leaseLengthYears');
  if(vehicle.registeredOn&&vehicle.registeredOn<'1900-01-01')add('vehicle-date-too-early',`${root}/registeredOn`);
  if(vehicle.purchasedOn&&vehicle.purchasedOn<'1900-01-01')add('vehicle-date-too-early',`${root}/purchasedOn`);
  if(mode==='manual'&&vehicle.registrationYear!==undefined&&vehicle.registrationYear<1900)add('vehicle-year-too-early',`${root}/registrationYear`);
  // Decimal strings are schema-validated; integer minor units avoid float drift.
  if(isSpecified&&vehicle.value!==undefined&&BigInt(vehicle.value.replace('.',''))<5000000n)add('specified-vehicle-value-minimum',`${root}/value`);
  const modifications=vehicle.modifications??[];
  if(vehicle.modified===true&&!modifications.length)add('vehicle-modification-required',`${root}/modifications`);
  if(vehicle.modified===false&&modifications.length)add('inactive-vehicle-modifications-retained',`${root}/modifications`);
  modifications.forEach((item,child)=>{if(!item.code)add('vehicle-modification-code-required',`${root}/modifications/${child}/code`);});
 });
 return issues;
}
