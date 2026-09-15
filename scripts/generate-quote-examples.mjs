import {readFile,writeFile} from 'node:fs/promises';
import {createQuoteValidationPipeline} from './quote-validation-pipeline.mjs';
import {prototypeBusinessGroups,prototypeHistoryDeclarations} from './quote-prototype-business.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ref=(collection,value=references.collections[collection][0].value)=>{
 const row=references.collections[collection]?.find(row=>row.value===value);if(!row)throw new Error(`missing-example-option:${collection}:${value}`);
 return {collection,value,label:row.text,version:references.version};
};
const id=n=>`aaaaaaaa-0000-4000-8000-${String(n).padStart(12,'0')}`;
const response=items=>({questionSetVersion:questions.version,answers:items.map(([questionId,value])=>({questionId,kind:questions.mappings.find(row=>row.questionId===questionId).answerKind,value,...(questions.mappings.find(row=>row.questionId===questionId).answerKind==='percentage'?{unit:'basis-points'}:{})}))});
const address={postcode:'AB1 2CD',houseNumber:'1',street:'Example Street',county:'Example County'};
const context={asOfDate:'2026-09-15',vehicleModes:{[id(3)]:'manual'}};
const validate=await createQuoteValidationPipeline();
for(const productCode of questions.products) {
 const combined=productCode==='motor-trade-combined';
 const proposal={
  schemaVersion:'1.0',productCode,
  insured:{declaredCompanyType:ref('companyTypes',1),title:ref(questions.mappings.find(row=>row.owner==='MTS-01-Q07').optionCollection),firstName:'Alex',surname:'Example',contact:{email:'alex@example.test',mobile:'07123456789'},address:structuredClone(address)},
  termIntent:{kind:'annual',localStartDate:'2026-09-15',localStartTime:'09:00',timeZone:'Europe/London'},
  risk:{
   business:{description:'Fictional business selling standard used cars to private customers.',startedOn:'2020-01-01',turnover:'100000.00',wageRoll:'25000.00',activities:[{id:id(1),code:ref('mtOccupations',5),turnoverBasisPoints:10000}],responses:response([
    ['MTS-02-Q01',ref('tradingFroms',combined?3:1)],['MTS-03-Q04',false],['MTS-03-Q06',true],['MTS-03-Q08',25],['MTS-03-Q09',5],
    ['prototype.quote.c6181a11c34c',ref('prototype.quote.c6181a11c34c',1)],['prototype.quote.0552d5a68ba2',ref('prototype.quote.0552d5a68ba2',6)],['prototype.quote.34613c23e95d',ref('prototype.quote.34613c23e95d',1)],
    ...prototypeBusinessGroups.flatMap(group=>group.parents.map(id=>[`prototype.quote.${id}`,false])),
    ...['ea4580cbac7a',...prototypeHistoryDeclarations].map(id=>[`prototype.quote.${id}`,false]),
    ['prototype.quote.af67cb40b4de',ref('prototype.quote.af67cb40b4de',1)],
   ])},
   responses:response([['MTS-06-Q01',ref('driverPlans',1)],['MTS-07-Q01',false],['MTS-10-Q01',true],['MTS-10-Q02',10000]]),
   declarations:response([1,3,5,7,9,11,13,15,17,19].map(n=>[`MTS-12-Q${String(n).padStart(2,'0')}`,false])),
   drivers:[{id:id(2),firstName:'Jamie',surname:'Example',fullName:'Jamie Example',dateOfBirth:'1980-01-01',relationship:ref('driverRelationshipsPolicyHolder',2),usage:ref('driverUsages',1),licence:{type:ref('driverLicenceTypes',2),issuedOn:'2000-01-01'},address:structuredClone(address),responses:response([
    ['MTS-06-Q08',ref('driverTitles')],['MTS-06-Q20',ref('driverGVWLimits',1)],['MTS-06-Q21',true],['MTS-06-Q25',ref('driverMotorcycleCovers',1)],['MTS-06-Q27',ref('driverMaxABIGroups',1)],['MTS-06-Q28',ref('driverTradeEmploymentBasises',1)],
    ...[31,32,33,40,46,48,53].map(n=>[`MTS-06-Q${n}`,false]),
    ['prototype.adddriver.trade-employment',ref('prototype.adddriver.trade-employment',1)],
   ])}],
   vehicles:[{id:id(3),registration:'DEMO 01',make:'Example',model:'Demonstrator',abiCode:'DEMO',abiGroup:28,vehicleType:ref('vehicleType',references.collections.vehicleType.find(row=>row.category==='Car').value),bodyDescription:'Saloon',registrationYear:2020,registeredOn:'2020-01-01',imported:false,value:'10000.00',purchasedOn:'2020-01-01',declaredOwnerType:ref('vehicleOwnerTypes',1),keptOvernightType:ref('vehicleKeptOvernightType'),keptOvernightAddress:'AB1 2CD',partOfLeaseAgreement:false,customerLoan:false,modified:false,responses:response([['prototype.addveh.special-characteristics',ref('prototype.addveh.special-characteristics',1)],['prototype.addveh.report-mid',true]])}],
   specifiedVehiclesRequested:false,
   previousInsurance:{responses:response([['MTS-05-Q10',ref('noClaimBonuses',1)]])},
   ...(combined?{premises:[{id:id(4),address:structuredClone(address),use:ref('tradingPremiseTypes'),yearsTrading:6,sharedWorksite:false,responses:response([['prototype.addprem.overnight-vehicles',true],['prototype.addprem.public-access',true]])}]}:{}),
  },
  cover:{responses:response([
   ['MTS-05-Q01',ref('coverLevels',1)],['MTS-05-Q02',ref('indemnityOwnVehicles',5)],['MTS-05-Q04',ref('indemnityOwnVehicles/number:5/excesses',4)],['MTS-05-Q05',false],['MTS-05-Q06',ref('excessLates',1)],['MTS-05-Q07',false],['MTS-05-Q08',false],
   ['MTS-11-Q07',false],['MTS-11-Q08',false],['MTS-11-Q09',ref('thirdPartyDamageLimits')],
   ['prototype.quote.becc2653d0de',false],['prototype.quote.4c5df77ffba1',false],
  ])},
 };
 const result=validate(JSON.stringify(proposal),context);
 if(result.status!=='section-checks-pass')throw new Error(JSON.stringify({productCode,...result}));
 await writeFile(new URL(`../contracts/examples/quote-capture-${productCode}.json`,import.meta.url),JSON.stringify({description:'Fictional composed section-validation example; not full prototype/readiness acceptance.',context,proposal},null,2)+'\n');
}
