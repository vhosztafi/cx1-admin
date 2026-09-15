import {readFile,writeFile} from 'node:fs/promises';
import {createQuoteValidationPipeline} from './quote-validation-pipeline.mjs';

const read=async name=>JSON.parse(await readFile(new URL(`../contracts/${name}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const validate=await createQuoteValidationPipeline();
const id=n=>`bbbbbbbb-0000-4000-8000-${String(n).padStart(12,'0')}`;
const ref=(collection,value=references.collections[collection][0].value)=>{
 const row=references.collections[collection].find(row=>row.value===value);
 if(!row)throw new Error(`Missing option ${collection}/${value}`);
 return {collection,value,label:row.text,version:references.version};
};
const set=(container,questionId,value)=>{
 container.responses??={questionSetVersion:questions.version,answers:[]};
 const rows=container.responses.answers,index=rows.findIndex(row=>row.questionId===questionId);
 const row={questionId,kind:questions.mappings.find(row=>row.questionId===questionId).answerKind,value};
 if(index<0)rows.push(row);else rows[index]=row;
};
const q=n=>`MTS-06-Q${String(n).padStart(2,'0')}`;
for(const product of questions.products)for(const scenario of ['history-and-extras','any-driver-trip']) {
 const {proposal:p,context}=await read(`examples/quote-capture-${product}.json`);
 p.cover.temporaryEuropeanCover=[{id:id(1),registration:'DEMO 01',startsOn:'2026-10-01',endsOn:'2026-10-10',area:ref('europeanArea'),cover:ref('europeanTripCover'),usage:ref('europeanTripUsage',1)}];
 set(p.cover,'MTS-11-Q13',true);
 if(scenario==='history-and-extras') {
  const driver=p.risk.drivers[0];p.cover.temporaryEuropeanCover[0].driverIds=[driver.id];
  p.cover.annualEuropeanCover=[{id:id(2),registration:'DEMO 01',usage:ref('europeanTripUsage',1)}];
  set(p.cover,'MTS-11-Q10',true);
  set(p.cover,'prototype.quote.becc2653d0de',true);set(p.cover,'MTS-11-Q01',true);set(p.cover,'MTS-11-Q02',ref('demonstrationCovers',2));
  set(p.cover,'MTS-11-Q04',true);set(p.cover,'MTS-11-Q05',ref('windScreenCovers'));set(p.cover,'MTS-11-Q06',false);
  set(p.risk.business,'MTS-03-Q04',true);set(p.risk.business,'MTS-03-Q05','Fictional Motor Traders Association');
  set(p.risk.business,'prototype.quote-value.315960b57ab1',true);
  p.risk.heldTradePlates=[{id:id(3),number:'123 AB'}];p.risk.tradePlates=[{id:id(4),number:'123 AB'}];
  set(p.risk,'MTS-07-Q01',true);set(p.risk,'MTS-07-Q02',1);
  const offence=references.collections.driverMTConvictionCodes.find(row=>row.text.startsWith('SP30 -'));
  driver.convictions=[{id:id(5),occurredOn:'2024-01-01',code:ref('driverMTConvictionCodes',offence.value),fine:'100.00',points:3,disqualified:false,declaredBanPeriod:'none'}];
  set(driver.convictions[0],'prototype.addconv.prosecution-status',ref('prototype.addconv.prosecution-status',1));
  driver.losses=[{id:id(6),occurredOn:'2025-01-01',type:ref('driverClaimTypes',1),declaredType:ref('prototype.incident-type',1),description:'Fictional parking collision; vehicle repaired and claim closed.',amount:'1500.00',declaredStatus:ref('driverClaimStatuses',3),fault:'fault',riskItemId:p.risk.vehicles[0].id}];
  set(driver.losses[0],'prototype.addinc.claim-made',true);
  set(driver,q(33),true);set(driver,q(40),true);
  set(p.risk.business,'prototype.quote.ef70e80708bb',true);set(p.risk.business,'prototype.quote.36da21d3c935',true);
  p.risk.materialFacts='Fictional demonstration only. Jamie Example: SP30 speeding conviction in January 2024, three points and a £100 fine, no ban. January 2025 parking collision: £1,500 claim, closed and at fault.';
 } else {
  p.risk.drivers=[];
  set(p.risk,q(1),ref('driverPlans',3));set(p.risk,q(2),2);
  for(const [n,collection,value] of [[3,'aadDriverMinAge',3],[4,'aadDriverMaxAge',1],[5,'aadMaxVehicleGrouping',1],[6,'aadMaxVehicleGvw',1],[7,'aadMaxMotorcycleCc',1]])set(p.risk,q(n),ref(collection,value));
 }
 const result=validate(JSON.stringify(p),context);
 if(result.status!=='section-checks-pass')throw new Error(JSON.stringify({product,scenario,...result}));
 await writeFile(new URL(`../contracts/examples/quote-capture-${product}-${scenario}.json`,import.meta.url),JSON.stringify({description:`Fictional ${scenario} composed capture; section validation only, not evidence/authority or runtime acceptance.`,context,proposal:p},null,2)+'\n');
}
