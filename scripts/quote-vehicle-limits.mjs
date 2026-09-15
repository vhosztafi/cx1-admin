import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';
import {reconcileQuoteCover} from './quote-cover-reconciliation.mjs';

// Source validatorProjection, vehicleParameters and vehicleDetailSchema.
// Missing metadata is not an unlimited limit. Pinned null motorcycle CC is.
export function validateQuoteVehicleLimits(proposal,questions,references) {
 quoteMappingsForProduct(questions,proposal.productCode);
 const issues=[],risk=proposal.risk??{},drivers=risk.drivers??[];
 const answer=(response,id)=>response?.answers?.find(a=>a.questionId===id)?.value;
 const trusted=(reference,collection)=>reference?.collection===collection&&reference.version===references.version?
  references.collections[collection]?.find(row=>row.value===reference.value&&row.text===reference.label):undefined;
 const plan=trusted(answer(risk.responses,'MTS-06-Q01'),'driverPlans')?.value;
 const named=[1,2].includes(plan),any=[2,3].includes(plan);
 const aggregate=(driverQuestion,driverCollection,driverValue,planQuestion,planCollection,planValue)=>{
  if(plan===undefined)return undefined;
  const values=[];
  if(named) {
   if(!drivers.length)return undefined;
   for(const driver of drivers){const row=trusted(answer(driver.responses,driverQuestion),driverCollection);if(!row)return undefined;values.push(driverValue(row));}
  }
  if(any){const row=trusted(answer(risk.responses,planQuestion),planCollection);if(!row)return undefined;values.push(planValue(row));}
  if(values.some(v=>v===undefined||typeof v==='number'&&!Number.isFinite(v)))return undefined;
  return values.includes(null)?null:Math.max(...values);
 };
 const abi=aggregate('MTS-06-Q27','driverMaxABIGroups',row=>/^Group \d+$/.test(row.text)?Number(row.text.slice(6)):undefined,'MTS-06-Q05','aadMaxVehicleGrouping',row=>row.abiGroup);
 const gvw=aggregate('MTS-06-Q20','driverGVWLimits',row=>row.gvwLimitDecimal,'MTS-06-Q06','aadMaxVehicleGvw',row=>row.maxGVW);
 // CC limits are checked separately for each active population, matching the
 // source conjunction; explicit unlimited in either population bypasses both.
 const cc=aggregate('MTS-06-Q25','driverMotorcycleCovers',row=>row.maxCC,'MTS-06-Q07','aadMaxMotorcycleCc',row=>row.maxCC);
 const namedCc=named&&drivers.length?drivers.map(d=>trusted(answer(d.responses,'MTS-06-Q25'),'driverMotorcycleCovers')?.maxCC):[];
 const anyCc=any?trusted(answer(risk.responses,'MTS-06-Q07'),'aadMaxMotorcycleCc')?.maxCC:undefined;
 const specified=new Set((risk.specifiedVehicleIds??[]).map(id=>id.toLowerCase()));
 const facts=reconcileQuoteCover(proposal,references).facts;
 (risk.vehicles??[]).forEach((vehicle,index)=>{
  const root=`/risk/vehicles/${index}`,add=(code,field)=>issues.push({code,path:`${root}/${field}`});
  const type=trusted(vehicle.vehicleType,'vehicleType');
  if(!type){add('vehicle-limit-type-context-required','vehicleType');return;}
  if(vehicle.abiGroup!==undefined&&!(type.category==='Commercial'&&type.requiresABICheckForCommercialVehicle===false)) {
   if(abi===undefined)add('vehicle-abi-context-required','abiGroup');
   else if(abi!==null&&vehicle.abiGroup>abi)add('vehicle-abi-limit-exceeded','abiGroup');
  }
  if(type.category!=='Car'&&vehicle.grossWeightKg!==undefined) {
   if(gvw===undefined)add('vehicle-gvw-context-required','grossWeightKg');
   else if(gvw!==null&&vehicle.grossWeightKg>gvw)add('vehicle-gvw-limit-exceeded','grossWeightKg');
  }
  if(type.category==='Motorcycle') {
   if(cc===undefined)add('vehicle-motorcycle-context-required','declaredEngineSize');
   else if(cc===0)add('motorcycle-cover-not-selected','vehicleType');
   else if(cc!==null&&vehicle.declaredEngineSize!==undefined) {
    const engine=Number(vehicle.declaredEngineSize);
    if(!/^\d+(\.\d+)?$/.test(vehicle.declaredEngineSize)||!Number.isFinite(engine))add('motorcycle-engine-size-invalid','declaredEngineSize');
    else if(namedCc.length&&engine>Math.max(...namedCc)||any&&engine>anyCc)add('motorcycle-cc-limit-exceeded','declaredEngineSize');
   }
  }
  if(!specified.has(vehicle.id.toLowerCase())&&vehicle.value!==undefined&&facts.coverLevel!=='third-party-only') {
   if(facts.ownVehicleLimit===undefined)add('vehicle-value-context-required','value');
   else if(BigInt(vehicle.value.replace('.',''))>BigInt(facts.ownVehicleLimit.replace('.','')))add('vehicle-value-limit-exceeded','value');
  }
 });
 return issues;
}
