// Run after strict schema and identity validation. Prototype band endpoints
// are inclusive as labelled; six months can validly appear in either band.
export function validateQuoteConvictionPeriods(proposal) {
 const ranges={'none':[0,0],'under-3-months':[1,2],'3-to-6-months':[3,6],'6-to-12-months':[6,12],'over-12-months':[13,1200]},issues=[];
 (proposal.risk?.drivers??[]).forEach((driver,d)=>{
  (driver.convictions??[]).forEach((row,c)=>{
   const path=`/risk/drivers/${d}/convictions/${c}`;
   const range=ranges[row.declaredBanPeriod];
   if(!range){issues.push({code:'declared-ban-period-required',path:`${path}/declaredBanPeriod`});return;}
   const banned=row.declaredBanPeriod!=='none';
   if(row.disqualified!==banned)issues.push({code:'conflicting-disqualification-declaration',path:`${path}/disqualified`});
   if(banned&&row.banMonths===undefined)issues.push({code:'exact-ban-duration-required',path:`${path}/banMonths`});
   if(row.banMonths!==undefined&&(row.banMonths<range[0]||row.banMonths>range[1]))issues.push({code:'ban-duration-outside-declared-band',path:`${path}/banMonths`});
  });
 });
 return issues;
}
