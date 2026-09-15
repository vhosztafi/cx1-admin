import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';

// pNewQuote C3 applies to every declared vehicle. The modal's single-choice
// characteristic is distinct from generic source "imported": manufacturer
// imports must not be silently classified as grey imports. MID is capture only.
export function validateQuotePrototypeVehicles(proposal,questions,references) {
 quoteMappingsForProduct(questions,proposal.productCode);
 const issues=[],declarations=proposal.risk?.business?.responses?.answers??[];
 const characteristic='prototype.addveh.special-characteristics',mid='prototype.addveh.report-mid';
 const declarationIds={modified:'5f9e8331ac6f',left:'488ecf4bdc09',grey:'87fad6b4a9fe',adapted:'d7a75768e505'};
 const declaration=key=>declarations.find(a=>a.questionId===`prototype.quote.${declarationIds[key]}`)?.value;
 (proposal.risk?.vehicles??[]).forEach((vehicle,index)=>{
  const path=`/risk/vehicles/${index}`,answers=vehicle.responses?.answers??[];
  const entry=id=>{const offset=answers.findIndex(a=>a.questionId===id);return {value:answers[offset]?.value,path:`${path}/responses/answers${offset<0?'':`/${offset}/value`}`};};
  const add=(code,field)=>issues.push({code,path:field});
  const selected=entry(characteristic),report=entry(mid);
  if(selected.value===undefined)add('vehicle-characteristics-required',selected.path);
  if(report.value===undefined)add('vehicle-mid-declaration-required',report.path);
  const ref=selected.value;
  const option=ref?.collection===characteristic&&ref.version===references.version?references.collections[characteristic]?.find(row=>row.value===ref.value&&row.text===ref.label)?.value:undefined;
  const requireDeclaration=key=>{if(declaration(key)!==true)add('vehicle-characteristic-declaration-required',selected.path);};
  if(vehicle.modified===true)requireDeclaration('modified');
  if(option===undefined)return; // The preceding reference gate diagnoses forged selections.
  if(option===2) {
   if(vehicle.modified!==true)add('conflicting-vehicle-modification',`${path}/modified`);
   if(vehicle.modified!==true)requireDeclaration('modified');
  } else if(option!==6&&vehicle.modified===true)add('conflicting-vehicle-modification',selected.path);
  if(option===3)requireDeclaration('left');
  if(option===4) {
   requireDeclaration('grey');
   if(vehicle.imported===false)add('conflicting-vehicle-import',`${path}/imported`);
  }
  if(option===5)requireDeclaration('adapted');
  if(option===6&&Object.keys(declarationIds).filter(key=>declaration(key)===true).length<2)add('multiple-vehicle-characteristics-context-required',selected.path);
 });
 return issues;
}
