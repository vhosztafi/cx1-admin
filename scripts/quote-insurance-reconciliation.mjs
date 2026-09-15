import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';

// Reconcile facts with matching meanings only. Discount claimed is not the same
// as NCB held, and introductory discount claimed is not prior introductory renewal.
export function reconcileQuoteInsuranceDeclarations(proposal,questions,references) {
 quoteMappingsForProduct(questions,proposal.productCode);
 const insurance=proposal.risk?.previousInsurance??{},answers=insurance.responses?.answers??[],issues=[];
 const entry=id=>{const index=answers.findIndex(a=>a.questionId===id);return {value:answers[index]?.value,path:`/risk/previousInsurance/responses/answers${index<0?'':`/${index}/value`}`};};
 const add=(code,path)=>issues.push({code,path});
 const trusted=(reference,collection)=>reference?.collection===collection&&reference.version===references.version?
  references.collections[collection]?.find(row=>row.value===reference.value&&row.text===reference.label):undefined;
 const origin=entry('prototype.quote.c0650c760167'),source=entry('MTS-05-Q11');
 if(origin.value!==undefined&&source.value!==undefined) {
  const prototype=trusted(origin.value,'prototype.quote.c0650c760167'),funnel=trusted(source.value,'noClaimBonusesEarned');
  if(!prototype||!funnel)add('insurance-origin-context-required',origin.path);
  else {
   const values={1:'motor-trade',2:'private-car',3:'commercial-vehicle'},sourceValues={1:'private-car',2:'commercial-vehicle',3:'motor-trade',4:'taxi',5:'other'};
   if(values[prototype.value]!==sourceValues[funnel.value]){add('conflicting-ncb-origin',origin.path);add('conflicting-ncb-origin',source.path);}
  }
 }
 const protection=entry('prototype.quote.61a13bb828b2'),protectedSource=entry('MTS-05-Q17');
 if(protection.value!==undefined&&protectedSource.value!==undefined&&protection.value!==protectedSource.value){add('conflicting-ncb-protection',protection.path);add('conflicting-ncb-protection',protectedSource.path);}
 if(insurance.noClaimsYears!==undefined) {
  const path='/risk/previousInsurance/noClaimsYears';
  const ncb=trusted(entry('MTS-05-Q10').value,'noClaimBonuses');
  if(!ncb||insurance.noClaimsYearsBasis===undefined)add('ncb-years-context-required',path);
  else {
   // Pinned option text expresses exact years or the capped 15+ lower bound.
   const match=/^(\d+)(\+)? Years?$/.exec(ncb.text);
   const sourceYears=ncb.value===1?0:match?Number(match[1]):undefined;
   const sourceAtLeast=!!match?.[2];
   if(sourceYears===undefined)add('ncb-years-context-required',path);
   else {
    const prototypeAtLeast=insurance.noClaimsYearsBasis==='at-least';
    const conflicts=sourceAtLeast?(!prototypeAtLeast&&insurance.noClaimsYears<sourceYears):
     prototypeAtLeast?insurance.noClaimsYears>sourceYears:insurance.noClaimsYears!==sourceYears;
    if(conflicts)add('conflicting-ncb-years',path);
   }
  }
 }
 return issues;
}
