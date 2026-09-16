import {readFile,writeFile} from 'node:fs/promises';
import {underwritingConfigSchema} from './underwriting-contract-model.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const references=await read('reference-data/motor-trade-capture.json');
// Some source collections have numeric display text but no numericValue.
// Translate once into a pinned, versioned server meaning table. Runtime never
// parses a client label or interprets an option identity as its numeric value.
const meanings={version:'uw-source-meanings-1',referenceVersion:references.version,collections:{}};
for(const name of ['aadDriverMinAge','aadDriverMaxAge','noClaimBonuses']){
  meanings.collections[name]=references.collections[name].map(row=>{
    const match=name==='noClaimBonuses'?/^(\d+)(\+)? Years?$/.exec(row.text):/^(\d+)$/.exec(row.text);
    if(!match&&!(name==='noClaimBonuses'&&row.text==='No NCB'))throw new Error(`Unresolved numeric source meaning ${name}/${row.value}`);
    return {value:row.value,numericValue:match?Number(match[1]):0,atLeast:Boolean(match?.[2])};
  });
}
// Match the known source catalog once during generation, then persist numeric
// identities. Runtime predicates use pinned IDs and never labels.
const trades=references.collections.mtOccupations;
const valeting=trades.filter(x=>/valet/i.test(x.text)).map(x=>x.value);
if(!valeting.length)throw new Error('Source catalog has no valeting identity');
const metadata={schemaVersion:'1',effectiveFrom:'2026-01-01T00:00:00Z',effectiveTo:'2030-01-01T00:00:00Z'};
const rating=(productCode,combined)=>({...metadata,kind:'rating',version:`demo-${combined?'combined':'road'}-1`,productCode,
  basePremium:combined?'800.00':'600.00',minimumPremium:combined?'800.00':'600.00',
  driverAdditionalPremium:'50.00',vehicleAdditionalPremium:'25.00',stockRateBps:combined?20:0,
  premisesPremium:combined?'100.00':'0.00',claimsLoadingBps:1000,taxRateBps:1200,fee:'35.00',quoteValidityDays:14,
  maximumAnnualPremium:'1000000.00',valetingLoadingBps:1200,youngDriverLoadingBps:1800,noClaimsDiscountBps:800,
  toolsPremium:'212.58',minimumTradingYears:5,noClaimsDiscountYears:5,youngDriverAge:25,
  referenceVersion:references.version,valetingTradeValues:valeting,
});
const limits={annualPremiumLimit:'5000.00',stockLimit:'125000.00',vehicleLimit:'50000.00',minimumDriverAge:25,
  maximumDriverAge:75,minimumLicenceYears:2,reviewConvictions:true,reviewClaims:true,reviewTradingHistory:true,
  allowSalvage:false,allowedTradeValues:trades.map(x=>x.value),
  coverLimits:{'road-risks':'50000.00','stock-custody':'125000.00',premises:'1000000.00','tools-equipment':'5000.00'},
};
const configurations=['motor-trade-road-risks','motor-trade-combined'].map((code,i)=>rating(code,i===1));
for(const productCode of ['motor-trade-road-risks','motor-trade-combined']){
  const productLimits=structuredClone(limits);
  if(productCode==='motor-trade-road-risks'){productLimits.stockLimit='0.00';productLimits.coverLimits['stock-custody']='0.00';productLimits.coverLimits.premises='0.00';}
  configurations.push({...metadata,kind:'binder',version:'demo-binder-1',productCode,providerId:'60000000-0000-4000-8000-000000000001',limits:productLimits});
  configurations.push({...metadata,kind:'authority',version:'demo-senior-1',productCode,limits:structuredClone(productLimits)});
  const narrow=structuredClone(productLimits);Object.assign(narrow,{annualPremiumLimit:'2500.00',stockLimit:productCode==='motor-trade-combined'?'100000.00':'0.00',vehicleLimit:'35000.00',reviewConvictions:false,reviewClaims:false});
  narrow.coverLimits['road-risks']='35000.00';if(productCode==='motor-trade-combined')narrow.coverLimits['stock-custody']='100000.00';
  configurations.push({...metadata,kind:'authority',version:'demo-underwriter-1',productCode,limits:narrow});
}
const example={purpose:'Design fixtures only: no runtime publication or real underwriting authority',configurations,
  pricingExamples:[
    {name:'road-baseline',annualPremium:'600.00',termPremium:'600.00',tax:'72.00',fee:'35.00',gross:'707.00'},
    {name:'combined-stock150000-premises',annualPremium:'1200.00',termPremium:'1200.00',tax:'144.00',fee:'35.00',gross:'1379.00'},
    {name:'combined-180days',annualPremium:'1200.00',termPremium:'591.78',tax:'71.01',fee:'35.00',gross:'697.79'},
    {name:'road-valeting',annualPremium:'672.00',termPremium:'672.00',tax:'80.64',fee:'35.00',gross:'787.64',factorInputs:{subtotal:'600.00',valeting:true,youngDriver:false,ncd:false,tools:false}},
    {name:'road-young-driver',annualPremium:'708.00',termPremium:'708.00',tax:'84.96',fee:'35.00',gross:'827.96',factorInputs:{subtotal:'600.00',valeting:false,youngDriver:true,ncd:false,tools:false}},
    {name:'road-ncd-above-minimum',annualPremium:'690.00',termPremium:'690.00',tax:'82.80',fee:'35.00',gross:'807.80',factorInputs:{subtotal:'750.00',valeting:false,youngDriver:false,ncd:true,tools:false}},
    {name:'road-tools',annualPremium:'812.58',termPremium:'812.58',tax:'97.51',fee:'35.00',gross:'945.09',factorInputs:{subtotal:'600.00',valeting:false,youngDriver:false,ncd:false,tools:true}},
  ],
  postingExamples:[
    {name:'agency-net',premium:'1200.00',tax:'144.00',fee:'35.00',commission:'120.00',feeShare:'0.00',debtorDue:'1259.00',insurerDue:'1224.00',feeIncome:'35.00',brokerPayable:'0.00'},
    {name:'agency-net-fee-share',premium:'1200.00',tax:'144.00',fee:'35.00',commission:'120.00',feeShare:'7.00',debtorDue:'1252.00',insurerDue:'1224.00',feeIncome:'28.00',brokerPayable:'0.00'},
    {name:'agency-separate',premium:'1200.00',tax:'144.00',fee:'35.00',commission:'120.00',feeShare:'7.00',debtorDue:'1379.00',insurerDue:'1224.00',feeIncome:'28.00',brokerPayable:'127.00'},
    {name:'direct-separate',premium:'1200.00',tax:'144.00',fee:'35.00',commission:'120.00',feeShare:'7.00',debtorDue:'1379.00',insurerDue:'1224.00',feeIncome:'28.00',brokerPayable:'127.00'},
  ],
};
example.proposals={};
for(const productCode of ['motor-trade-road-risks','motor-trade-combined']){
  const source=await read(`examples/quote-capture-${productCode}.json`);
  const proposal=structuredClone(source.proposal);
  proposal.cover.requestedSections=[{id:'60000000-0000-4000-8000-000000000011',code:'tools-equipment',selected:false}];
  if(productCode==='motor-trade-combined')proposal.cover.requestedSections.push(
    {id:'60000000-0000-4000-8000-000000000012',code:'stock-custody',selected:true,limit:'150000.00',excess:'750.00',anyOneVehicleLimit:'35000.00'},
    {id:'60000000-0000-4000-8000-000000000013',code:'premises',selected:true,limit:'250000.00',excess:'500.00',premisesIds:proposal.risk.premises.map(p=>p.id)},
  );
  example.proposals[productCode]=proposal;
}
for(const [path,value] of [['schemas/underwriting-config.schema.json',underwritingConfigSchema],['examples/underwriting-demo.json',example],['reference-data/underwriting-meanings.json',meanings]])
  await writeFile(new URL(`../contracts/${path}`,import.meta.url),JSON.stringify(value,null,2)+'\n');
console.log(`Generated closed underwriting config and ${configurations.length} fictional definitions.`);
