// Design contract for trusted, per-instance option selection. Run strict schema,
// question and identity checks first; do not accept this context from a caller.
import {reconcileQuoteCover} from './quote-cover-reconciliation.mjs';
const validDate=value=>typeof value==='string'&&/^\d{4}-\d{2}-\d{2}$/.test(value)&&Number.isFinite(Date.parse(`${value}T00:00:00Z`))&&new Date(`${value}T00:00:00Z`).toISOString().slice(0,10)===value;
export function ageOn(dateOfBirth,onDate) {
  if(!validDate(dateOfBirth)||!validDate(onDate)||dateOfBirth>onDate)return undefined;
  const years=Number(onDate.slice(0,4))-Number(dateOfBirth.slice(0,4));
  let birthday=`${onDate.slice(0,4)}${dateOfBirth.slice(4)}`;
  if(!validDate(birthday))birthday=`${onDate.slice(0,4)}-02-28`;
  return years-(onDate<birthday?1:0);
}

export function selectQuoteDynamicOptions(proposal,catalogue) {
  const selectedCollections={};const issues=[];
  const add=(code,path)=>issues.push({code,path});
  const trusted=(reference,collection)=>{
    if(!reference||reference.collection!==collection||reference.version!==catalogue.version)return undefined;
    return catalogue.collections[collection]?.find(row=>row.value===reference.value&&row.text===reference.label);
  };
  const find=(answers,id)=>{
    const entries=(answers??[]).map((answer,index)=>({answer,index})).filter(entry=>entry.answer.questionId===id);
    return entries.length===1?entries[0]:undefined;
  };
  const answers=proposal.cover?.responses?.answers;
  const reconciled=reconcileQuoteCover(proposal,catalogue);
  const levelValue=['comprehensive','third-party-fire-theft','third-party-only'].indexOf(reconciled.facts.coverLevel)+1;
  const level=catalogue.collections.coverLevels.find(row=>row.value===levelValue);
  // Resolve an existing pinned source option by its business meaning. Never
  // manufacture an ID for a prototype option absent from this product version.
  const equivalent=(fact,collection)=>reconciled.facts[fact]===undefined?undefined:
    catalogue.collections[collection].find(row=>Number.isFinite(row.numericValue)&&row.numericValue.toFixed(2)===reconciled.facts[fact]);
  const own=equivalent('ownVehicleLimit','indemnityOwnVehicles');
  const customer=equivalent('customerVehicleLimit','indemnityCustomerVehicles');
  const validOwn=own&&level&&(level.value!==2||own.numericValue<=15000);
  const excess=find(answers,'MTS-05-Q04');
  if(excess) {
    const path=`/cover/responses/answers/${excess.index}/value`;
    if(level?.value===3)add('inactive-dynamic-answer',path);
    else if(!validOwn)add('missing-dynamic-dependency',path);
    else selectedCollections[path]=[`indemnityOwnVehicles/${typeof own.value==='number'?'number':'string'}:${own.value}/excesses`];
  }
  const prototypeExcess=find(answers,'prototype.quote.00216de47ab5');
  if(prototypeExcess) {
    const path=`/cover/responses/answers/${prototypeExcess.index}/value`;
    if(level?.value===3)add('inactive-dynamic-answer',path);
    else if(!validOwn)add('missing-dynamic-dependency',path);
    else {
      const collection=`indemnityOwnVehicles/${typeof own.value==='number'?'number':'string'}:${own.value}/excesses`;
      if(!equivalent('excess',collection))add('unsupported-cover-configuration',path);
    }
  }
  for(const [fact,questionId,row] of [
    ['ownVehicleLimit','prototype.quote.d9dd069a314c',own],
    ['customerVehicleLimit','prototype.quote.b4c7e25f7781',customer],
  ]) {
    const entry=find(answers,questionId);
    if(entry&&reconciled.facts[fact]!==undefined&&!row)add('unsupported-cover-configuration',`/cover/responses/answers/${entry.index}/value`);
  }
  const activities=proposal.risk?.business?.activities;
  const activityRows=activities?.map(activity=>trusted(activity.code,'mtOccupations'));
  const completeActivities=activityRows?.length&&activityRows.every(Boolean);
  const customerRequired=completeActivities&&activityRows.some(row=>row.customerLOI);
  const validCustomer=customer&&level&&(level.value!==2||customer.numericValue>0&&customer.numericValue<=15000);
  const limit=validOwn&&completeActivities&&(!customerRequired||level.value===3||validCustomer)?
    customerRequired&&level.value!==3?Math.max(own.numericValue,customer.numericValue):own.numericValue:undefined;
  (proposal.risk?.drivers??[]).forEach((driver,index)=>{
    const age=ageOn(driver.dateOfBirth,proposal.termIntent?.localStartDate);
    const band=age===undefined?-1:catalogue.youngDriverConfiguration.findIndex(row=>(row.ageFrom??0)<=age&&age<=(row.ageTo??1000));
    for(const [questionId,child] of [['MTS-06-Q59','indemnities'],['MTS-06-Q60','cCs']]) {
      const entry=find(driver.responses?.answers,questionId);
      if(!entry)continue;
      const path=`/risk/drivers/${index}/responses/answers/${entry.index}/value`;
      if(age!==undefined&&age>=25||child==='indemnities'&&level?.value===3){add('inactive-dynamic-answer',path);continue;}
      if(band<0||child==='indemnities'&&(limit===undefined||!level)){add('missing-dynamic-dependency',path);continue;}
      const collection=`youngDriverConfiguration/${band}/${child}`;
      selectedCollections[path]=[collection];
      if(child==='indemnities') {
        const selected=trusted(entry.answer.value,collection);
        if(selected&&(!selected.numericValue||selected.numericValue>limit))add('indemnity-exceeds-policy-limit',path);
      }
    }
  });
  return {selectedCollections,issues:[...issues,...reconciled.issues]};
}
