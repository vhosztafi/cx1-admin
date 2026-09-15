// One stable vehicle collection supports both prototype registers. Register
// purpose is independent of legal owner and specified/high-value membership.
export function validateQuoteVehicleRegisters(proposal) {
 return (proposal.risk?.vehicles??[]).flatMap((vehicle,index)=>vehicle.register===undefined?[{code:'vehicle-register-required',path:`/risk/vehicles/${index}/register`}]:[]);
}
export function summarizeQuoteVehicleRegisters(proposal) {
 const vehicles=proposal.risk?.vehicles??[];
 const mid=vehicles.map(vehicle=>vehicle.responses?.answers?.find(a=>a.questionId==='prototype.addveh.report-mid')?.value);
 return {
  owned:vehicles.filter(vehicle=>vehicle.register==='owned-not-for-sale').map(vehicle=>vehicle.id),
  forSale:vehicles.filter(vehicle=>vehicle.register==='held-for-sale').map(vehicle=>vehicle.id),
  unassigned:vehicles.filter(vehicle=>!['owned-not-for-sale','held-for-sale'].includes(vehicle.register)).map(vehicle=>vehicle.id),
  total:vehicles.length,
  midReportCount:mid.every(value=>typeof value==='boolean')?mid.filter(Boolean).length:null,
 };
}
