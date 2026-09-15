import {ageOn} from './quote-dynamic-options.mjs';

// Source text-control bounds supplement the broader reusable policy shapes.
export function validateQuoteTextBounds(proposal) {
 const issues=[];
 const limit=(value,max,path)=>{if(typeof value==='string'&&value.length>max)issues.push({code:'source-text-too-long',path,maxLength:max});};
 const address=(value,path)=>{
  limit(value?.postcode,10,`${path}/postcode`);
  for(const field of ['houseNumber','street','town','city','county'])limit(value?.[field],50,`${path}/${field}`);
 };
 address(proposal.insured?.address,'/insured/address');
 (proposal.risk?.premises??[]).forEach((p,i)=>address(p.address,`/risk/premises/${i}/address`));
 (proposal.risk?.drivers??[]).forEach((driver,index)=>{
  address(driver.address,`/risk/drivers/${index}/address`);
  (driver.responses?.answers??[]).forEach((answer,i)=>{if(answer.questionId==='MTS-06-Q47')limit(answer.value,50,`/risk/drivers/${index}/responses/answers/${i}/value`);});
 });
 (proposal.risk?.business?.responses?.answers??[]).forEach((answer,i)=>{if(answer.questionId==='MTS-03-Q07')limit(answer.value,20,`/risk/business/responses/answers/${i}/value`);});
 return issues;
}
// Cross-field design checks after schema/identity/question validation. The
// caller supplies the trusted London as-of date; never use a browser clock.
export function validateQuoteSourceRules(proposal,asOfDate) {
  if(ageOn('1900-01-01',asOfDate)===undefined)throw new Error('trusted-as-of-date-required');
  const issues=[];const add=(code,path)=>issues.push({code,path});
  const risk=proposal.risk??{};
  const answer=(response,id)=>response?.answers?.find(item=>item.questionId===id)?.value;
  const normalized=value=>value.replaceAll(' ','').toUpperCase();
  const unique=(items,field,path)=>{
    const seen=new Set();
    (items??[]).forEach((item,index)=>{
      if(typeof item[field]!=='string')return;
      const key=normalized(item[field]);
      if(seen.has(key))add('duplicate-registration',`${path}/${index}/${field}`);
      seen.add(key);
    });
  };
  unique(risk.vehicles,'registration','/risk/vehicles');
  unique(risk.tradePlates,'number','/risk/tradePlates');
  unique(proposal.cover?.annualEuropeanCover,'registration','/cover/annualEuropeanCover');
  const plates=risk.tradePlates??[];
  if(plates.length>150)add('trade-plate-row-limit','/risk/tradePlates');
  const plateCover=answer(risk.responses,'MTS-07-Q01');
  const plateCount=answer(risk.responses,'MTS-07-Q02');
  if(plateCover===false&&(plates.length||plateCount!==undefined))add('inactive-trade-plate-data','/risk/tradePlates');
  if(plateCover===true&&(!Number.isInteger(plateCount)||plateCount<1||plateCount>999))add('trade-plate-count-required','/risk/responses/answers');
  if(Number.isInteger(plateCount)&&plateCount<plates.length)add('trade-plate-count-below-rows','/risk/tradePlates');
  const start=proposal.termIntent?.localStartDate;
  if(risk.business?.startedOn&&start&&risk.business.startedOn>start)add('business-start-after-policy','/risk/business/startedOn');
  const activities=risk.business?.activities;
  if(activities) {
    const codes=new Set();let total=0,complete=activities.length>0;
    activities.forEach((activity,index)=>{
      if(activity.code) {
        const code=JSON.stringify([activity.code.collection,activity.code.value]);
        if(codes.has(code))add('duplicate-business-activity',`/risk/business/activities/${index}/code`);
        codes.add(code);
      }
      if(!Number.isInteger(activity.turnoverBasisPoints))complete=false;
      else total+=activity.turnoverBasisPoints;
    });
    if(!complete)add('activity-share-required','/risk/business/activities');
    else if(total!==10000)add('activity-total-must-equal-100-percent','/risk/business/activities');
  }
  (risk.drivers??[]).forEach((driver,index)=>{
    const path=`/risk/drivers/${index}`;
    if(driver.licence?.issuedOn&&driver.dateOfBirth&&(driver.licence.issuedOn<driver.dateOfBirth||ageOn(driver.dateOfBirth,driver.licence.issuedOn)<17))add('licence-before-seventeenth-birthday',`${path}/licence/issuedOn`);
    const residency=answer(driver.responses,'MTS-06-Q22');
    if(residency&&driver.dateOfBirth&&residency<driver.dateOfBirth)add('residency-before-birth',`${path}/responses/answers`);
    if(residency&&residency>asOfDate)add('residency-in-future',`${path}/responses/answers`);
  });
  const specified=new Set((risk.specifiedVehicleIds??[]).map(id=>id.toLowerCase()));
  (risk.vehicles??[]).forEach((vehicle,index)=>{
    if(vehicle.purchasedOn&&vehicle.purchasedOn>asOfDate&&!specified.has(vehicle.id.toLowerCase()))add('purchase-in-future',`/risk/vehicles/${index}/purchasedOn`);
    const modifications=new Set();
    (vehicle.modifications??[]).forEach((modification,child)=>{
      if(!modification.code)return;
      const key=JSON.stringify([modification.code.collection,modification.code.value]);
      if(modifications.has(key))add('duplicate-vehicle-modification',`/risk/vehicles/${index}/modifications/${child}/code`);
      modifications.add(key);
    });
  });
  return issues;
}
