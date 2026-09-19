// Executable contract examples, not an HTTP authorization or underwriting engine.
// Call after AJV validates commercial-combined.schema.json. The .NET consumer
// must enforce the same semantic cases before saving/rating (Phase08-02).
export function normalizeCommercialPostcode(input){
 if(typeof input!=='string')return null;
 const p=input.trim().toUpperCase().replace(/\s+/g,'');
 const syntax=/^(?:GIR0AA|(?:[A-PR-UWYZ][0-9][0-9]?|[A-PR-UWYZ][A-HK-Y][0-9][0-9]?|[A-PR-UWYZ][0-9][A-HJKPSTUW]|[A-PR-UWYZ][A-HK-Y][0-9][ABEHMNPRVWXY])[0-9][ABD-HJLNP-UW-Z]{2})$/;
 if(!syntax.test(p))return null;
 return {postcode:`${p.slice(0,-3)} ${p.slice(-3)}`,district:p.slice(0,-3)};
}
const pennies=x=>x===undefined?0n:BigInt(x.replace('.',''));
export function commercialSemanticIssues(value){
 const invalid=[],readiness=[],risk=value.risk??{},locations=risk.locations??[],wages=risk.wages??[],losses=risk.losses??[];
 const ids=new Set();
 for(const row of [...locations,...wages,...losses,...(risk.business?.activities??[]),...(risk.businessInterruption?.dependencies??[])]){
  const key=row.id.toLowerCase();if(ids.has(key))invalid.push('duplicate-risk-id');ids.add(key);
 }
 const responseSets=[risk.declarations,risk.business?.responses,value.cover?.responses,...locations.map(x=>x.responses),...wages.map(x=>x.responses),...losses.map(x=>x.responses)];
 for(const responses of responseSets){const seen=new Set();for(const q of responses?.answers??[]){if(seen.has(q.questionId))invalid.push('duplicate-question-id');seen.add(q.questionId);}}
 const locationsById=new Set(locations.map(x=>x.id.toLowerCase()));
 for(const loss of losses)if(loss.riskItemId&&!locationsById.has(loss.riskItemId.toLowerCase()))invalid.push('loss-location-not-owned');
 for(const location of locations){
  if(location.address?.postcode&&!normalizeCommercialPostcode(location.address.postcode))invalid.push('location-postcode-invalid');
  if(['buildings','contents','stock'].every(k=>location[k]!==undefined)&&['buildings','contents','stock'].reduce((n,k)=>n+pennies(location[k]),0n)<=0n)readiness.push('location-positive-sum-insured-required');
 }
 const answer=(responses,id)=>responses?.answers?.find(x=>x.questionId===id)?.value;
 const el=answer(risk.declarations,'prototype.quote.36ef01068295');
 if(el===false&&(pennies(risk.liability?.employersLimit)>0n||risk.liability?.employersReferenceNumber))readiness.push('el-disabled-details');
 if(el===true&&(!risk.liability?.employersReferenceNumber||pennies(risk.liability?.employersLimit)<=0n||wages.reduce((n,x)=>n+pennies(x.employees)+pennies(x.labourOnlySubcontractors),0n)<=0n))readiness.push('el-details-required');
 const bi=answer(value.cover?.responses,'prototype.quote.7660fc5eb42e');
 if(bi===false&&Object.keys(risk.businessInterruption??{}).length)readiness.push('bi-disabled-details');
 if(bi===true&&(!risk.businessInterruption?.basis||!risk.businessInterruption?.indemnityMonths||pennies(risk.businessInterruption?.sumInsured)<=0n))readiness.push('bi-details-required');
 const cw=value.cover?.contractWorks;
 if(cw?.selected===false&&(cw.sumInsured!==undefined||cw.excess!==undefined))readiness.push('contract-works-disabled-details');
 if(cw?.selected===true&&(pennies(cw.sumInsured)<=0n||cw.excess===undefined))readiness.push('contract-works-details-required');
 const activities=risk.business?.activities??[];
 if(activities.length&&activities.every(x=>x.percentageBasisPoints!==undefined)&&activities.reduce((n,x)=>n+x.percentageBasisPoints,0)!==10000)readiness.push('activity-total-must-be-10000');
 const machinery=answer(value.cover?.responses,'prototype.quote-value.5743fa7db272'),computers=answer(value.cover?.responses,'prototype.quote-value.fa63248f9ae1');
 if(locations.every(x=>x.contents!==undefined)&&pennies(machinery)+pennies(computers)>locations.reduce((n,x)=>n+pennies(x.contents),0n))readiness.push('contents-breakdown-exceeds-total');
 return {invalid:[...new Set(invalid)],readiness:[...new Set(readiness)]};
}
