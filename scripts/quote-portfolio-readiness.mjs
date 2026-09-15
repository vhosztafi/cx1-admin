import {quoteMappingsForProduct,validateQuotePortfolio,validateQuoteAnswerConditions} from './quote-semantic-contract.mjs';

// Source vehicle-types.ts: percentages are stored as basis points, and source
// detail/eligibility rules are enforced without silently toggling declarations.
export function validateQuotePortfolioReadiness(proposal,questions,references) {
 const mappings=quoteMappingsForProduct(questions,proposal.productCode);
 const rules=mappings.filter(row=>row.owner.startsWith('MTS-10-'));
 const issues=[...validateQuotePortfolio(proposal,mappings),...validateQuoteAnswerConditions(proposal,rules)];
 const risk=proposal.risk??{},answers=risk.responses?.answers??[];
 const get=id=>{const index=answers.findIndex(a=>a.questionId===id);return {value:answers[index]?.value,path:`/risk/responses/answers${index<0?'':`/${index}/value`}`};};
 const add=(code,id)=>issues.push({code,path:get(id).path,questionId:id});
 const trusted=(reference,collection)=>reference?.collection===collection&&reference.version===references.version?references.collections[collection]?.find(row=>row.value===reference.value&&row.text===reference.label):undefined;
 for(const rule of rules.filter(row=>row.answerKind==='percentage')) {
  const value=get(rule.questionId).value;
  if(get(rule.requiredWhen.questionId).value===true&&Number.isInteger(value)&&value>0&&value<100&&!['MTS-10-Q04','MTS-10-Q08'].includes(rule.questionId))add('portfolio-minimum-one-percent',rule.questionId);
 }
 if(get('MTS-10-Q19').value===true&&get('MTS-10-Q21').value!==undefined&&get('MTS-10-Q21').value<3)add('transporter-minimum-three-vehicles','MTS-10-Q21');
 const drivers=risk.drivers??[],plan=trusted(get('MTS-06-Q01').value,'driverPlans')?.value;
 const driverAnswer=(driver,id)=>driver.responses?.answers?.find(a=>a.questionId===id)?.value;
 if(get('MTS-10-Q03').value===true) {
  const groups=[];let complete=plan!==undefined;
  if([1,2].includes(plan)) {
   if(!drivers.length)complete=false;
   for(const driver of drivers){const row=trusted(driverAnswer(driver,'MTS-06-Q27'),'driverMaxABIGroups');if(!row||!/^Group \d+$/.test(row.text))complete=false;else groups.push(Number(row.text.slice(6)));}
  }
  if([2,3].includes(plan)){const row=trusted(get('MTS-06-Q05').value,'aadMaxVehicleGrouping');if(!row)complete=false;else groups.push(row.abiGroup);}
  if(!complete)add('sports-portfolio-context-required','MTS-10-Q03');
  else if(!groups.some(value=>value>28))add('sports-portfolio-ineligible','MTS-10-Q03');
 }
 if(get('MTS-10-Q07').value===true) {
  const named=drivers.some(driver=>driverAnswer(driver,'MTS-06-Q26'));
  const motorcycle=trusted(get('MTS-06-Q07').value,'aadMaxMotorcycleCc');
  if(!named&&plan===undefined||!named&&[2,3].includes(plan)&&!motorcycle)add('motorcycle-portfolio-context-required','MTS-10-Q07');
  else if(!named&&!(plan!==1&&motorcycle&&motorcycle.value!==1))add('motorcycle-portfolio-ineligible','MTS-10-Q07');
 }
 const specified=new Set((risk.specifiedVehicleIds??[]).map(id=>id.toLowerCase()));
 for(const vehicle of risk.vehicles??[]) {
  const type=trusted(vehicle.vehicleType,'vehicleType');
  const required=[
   ['MTS-10-Q05',type?.category==='Commercial'&&vehicle.grossWeightKg>3500],
   ['MTS-10-Q11',vehicle.imported===true],
   ['MTS-10-Q13',!specified.has(vehicle.id.toLowerCase())&&vehicle.modifications?.length>0],
   ['MTS-10-Q17',type?.requiresRallyTrackKitCarsTrikes===true],
   ['MTS-10-Q22',type?.requiresQuadBikes===true],
   ['MTS-10-Q26',vehicle.seats>7],
  ];
  for(const [id,active] of required)if(active&&get(id).value!==true&&!issues.some(i=>i.code==='portfolio-declaration-conflicts-with-vehicle'&&i.questionId===id))add('portfolio-declaration-conflicts-with-vehicle',id);
 }
 return issues;
}
