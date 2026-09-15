// Preserve source selections while deriving common business facts. Prototype
// ordinal IDs are not interchangeable with the bundled funnel option IDs.
// This does not decide pricing/eligibility or invent missing funnel options.
const groups=[
  {fact:'coverLevel',source:'MTS-05-Q01',prototype:'prototype.quote.2beaf3b8d546',collection:'coverLevels',values:['comprehensive','third-party-fire-theft','third-party-only']},
  {fact:'ownVehicleLimit',source:'MTS-05-Q02',prototype:'prototype.quote.d9dd069a314c',collection:'indemnityOwnVehicles',values:['7500.00','10000.00','12500.00','15000.00','20000.00','25000.00','30000.00']},
  {fact:'customerVehicleLimit',source:'MTS-05-Q03',prototype:'prototype.quote.b4c7e25f7781',collection:'indemnityCustomerVehicles',values:['7500.00','10000.00','12500.00','15000.00','20000.00','25000.00','30000.00']},
  {fact:'excess',source:'MTS-05-Q04',prototype:'prototype.quote.00216de47ab5',collectionPrefix:'indemnityOwnVehicles/',values:['250.00','500.00','750.00','1000.00']},
];
export const coverReconciliationGroups=groups;

export function reconcileQuoteCover(proposal,catalogue) {
  const answers=proposal.cover?.responses?.answers??[];
  const facts={};const issues=[];
  const trusted=reference=>{
    if(reference?.version!==catalogue.version)return undefined;
    const rows=catalogue.collections[reference.collection];
    return Array.isArray(rows)?rows.find(row=>row.value===reference.value&&row.text===reference.label):undefined;
  };
  for(const group of groups) {
    const declarations=[];
    answers.forEach((answer,index)=>{
      if(![group.source,group.prototype].includes(answer.questionId))return;
      const path=`/cover/responses/answers/${index}/value`;
      const reference=answer.value;
      const row=answer.kind==='reference'?trusted(reference):undefined;
      const prototype=answer.questionId===group.prototype;
      const correctCollection=prototype?reference?.collection===group.prototype:
        group.collection?reference?.collection===group.collection:
          reference?.collection?.startsWith(group.collectionPrefix)&&reference.collection.endsWith('/excesses');
      if(!row||!correctCollection){issues.push({code:'unresolved-cover-selection',path,fact:group.fact});return;}
      let value;
      if(prototype||group.fact==='coverLevel')value=Number.isInteger(row.value)?group.values[row.value-1]:undefined;
      else if(Number.isFinite(row.numericValue)&&row.numericValue>=0)value=row.numericValue.toFixed(2);
      if(value===undefined){issues.push({code:'unresolved-cover-selection',path,fact:group.fact});return;}
      declarations.push({value,path});
    });
    if(new Set(declarations.map(row=>row.value)).size>1) {
      for(const declaration of declarations)issues.push({code:'conflicting-cover-declarations',path:declaration.path,fact:group.fact});
    } else if(declarations.length&&!issues.some(issue=>issue.fact===group.fact))facts[group.fact]=declarations[0].value;
  }
  return {facts,issues};
}
