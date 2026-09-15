import {quoteMappingsForProduct} from './quote-semantic-contract.mjs';

// Prototype cover/use/courtesy selections retain their own meaning. This is
// capture consistency, not pricing or an inferred grant of insurance cover.
export function reconcileQuoteCoverDeclarations(proposal,questions,references) {
 quoteMappingsForProduct(questions,proposal.productCode);
 const issues=[];
 const entry=(answers,id,root)=>{const index=(answers??[]).findIndex(a=>a.questionId===id);return {value:answers?.[index]?.value,path:`${root}/answers${index<0?'':`/${index}/value`}`,questionId:id};};
 const cover=id=>entry(proposal.cover?.responses?.answers,id,'/cover/responses');
 const add=(code,field)=>issues.push({code,path:field.path,questionId:field.questionId});
 const trusted=(reference,collection)=>reference?.collection===collection&&reference.version===references.version?references.collections[collection]?.find(row=>row.value===reference.value&&row.text===reference.label):undefined;
 const demo=cover('prototype.quote.becc2653d0de'),sourceDemo=cover('MTS-11-Q01'),privateUse=cover('prototype.quote.4c5df77ffba1');
 const courtesy=entry(proposal.risk?.business?.responses?.answers,'prototype.quote.af67cb40b4de','/risk/business/responses');
 for(const field of [demo,privateUse,courtesy])if(field.value===undefined)add('required-prototype-cover-answer',field);
 if(demo.value!==undefined&&sourceDemo.value!==undefined&&demo.value!==sourceDemo.value){add('conflicting-demonstration-cover',demo);add('conflicting-demonstration-cover',sourceDemo);}
 if(demo.value===true&&sourceDemo.value===undefined)add('demonstration-cover-context-required',sourceDemo);
 const courtesyOption=trusted(courtesy.value,courtesy.questionId),loan=cover('MTS-05-Q08');
 if(courtesyOption&&loan.value!==undefined&&(courtesyOption.value===2)!==loan.value){add('conflicting-courtesy-cover',courtesy);add('conflicting-courtesy-cover',loan);}
 // A customer's own insurer and "none" both request no customer-loan cover
 // here; their different vehicle arrangements remain in the captured answer.
 const plan=trusted(proposal.risk?.responses?.answers?.find(a=>a.questionId==='MTS-06-Q01')?.value,'driverPlans');
 const drivers=proposal.risk?.drivers??[],usages=drivers.map(driver=>trusted(driver.usage,'driverUsages'));
 if(privateUse.value===false)drivers.forEach((driver,index)=>{
  if(usages[index]?.isSocialDomesticPleasure===true){add('conflicting-private-use',privateUse);issues.push({code:'conflicting-private-use',path:`/risk/drivers/${index}/usage`});}
 });
 // Only the named-only plan has a complete population to compare. Any-driver
 // settings contain no equivalent individual usage fact; do not invent one.
 if(privateUse.value===true&&plan?.value===1&&drivers.length&&usages.every(Boolean)&&!usages.some(usage=>usage.isSocialDomesticPleasure))add('private-use-driver-required',privateUse);
 return issues;
}
