import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import {funnelToProposal,proposalToFunnel} from '../lib/funnel-adapter.ts';
const read=async path=>JSON.parse(await readFile(new URL(`../../../contracts/${path}`,import.meta.url),'utf8'));
const references=await read('reference-data/motor-trade-capture.json');
const questions=await read('quote-question-catalogue.json');
const catalogue={...references,mappings:questions.mappings};
const ajv=new Ajv({strict:true,allErrors:true});addFormats(ajv);const validate=ajv.compile(await read('schemas/quote-draft.schema.json'));
const base={schemaVersion:'1.0',productCode:'motor-trade-road-risks'};
test('partial capture retains false and exact amounts, date offsets and agency selected identity',()=>{
 const state={version:1,step:3,formData:{proposerCompanyPartnershipName:'Selected client',proposerCompanyTypeId:3,proposerPostcode:'PE1 1AA',proposerStreet:'Example Street',proposerTown:'Example Town',proposerPolicyStartDate:'2026-10-05',proposerPolicyStartTime:'10:00',isShortTerm:false,businessAnnualTurnover:'123.45',businessIsVATRegistered:false,businessVATNumber:null}};
 const result=funnelToProposal(state,base,catalogue);
 assert.ok(validate(result),JSON.stringify(validate.errors));assert.equal(result.insured.legalName,'Selected client');assert.equal(result.risk.business.turnover,'123.45');assert.equal(result.termIntent.utcOffsetMinutes,60);
 assert.equal(result.risk.business.responses.answers.find(row=>row.questionId==='MTS-03-Q06').value,false);
 const raw=proposalToFunnel(result,catalogue);assert.equal(raw.businessIsVATRegistered,false);assert.equal(raw.proposerCompanyPartnershipName,'Selected client');
});
test('driver, premises, activities, vehicle and trade plate rows retain stable identities across saves',()=>{
 const state={version:1,step:8,formData:{occupations:[{occupationId:references.collections.mtOccupations[0].value,percentage:'100'}],drivers:[{firstName:'Example',surname:'Driver',dateOfBirth:'1980-01-01',licenceTypeID:references.collections.driverLicenceTypes[0].value,driverOccupations:[{motortradeRoadRisksOccupationID:references.collections.occupations[0].value,isBusinessUseRequired:false}]}],vehicles:[{regNumber:'AB12 CDE',make:'Example',model:'Car',ownerTypeID:2,vehicleValue:'1000.01'}],tradingPremises:[{tradingPremisePostcode:'PE1 1AA',tradingPremiseStreet:'Example Road',tradingPremiseTown:'Town'}],tradePlateNumbers:[{plateNumber:'12345'}]}};
 const first=funnelToProposal(state,base,catalogue);assert.ok(validate(first),JSON.stringify(validate.errors));const next=funnelToProposal(state,first,catalogue);assert.deepEqual(next,first);
 assert.equal(next.risk.business.activities[0].turnoverBasisPoints,10000);assert.equal(next.risk.vehicles[0].ownership,'business-owned');
 const raw=proposalToFunnel(first,catalogue);assert.equal(raw.tradePlateNumbers[0].plateNumber,'12345');assert.equal(raw.drivers[0].guid,first.risk.drivers[0].id);
});
test('unsupported and string-coerced IDs are rejected; raw null remains absent typed answer',()=>{
 assert.throws(()=>funnelToProposal({version:1,step:1,formData:{proposerCompanyTypeId:'3'}},base,catalogue),/supported option/);
 const result=funnelToProposal({version:1,step:1,formData:{proposerCompanyTypeId:null}},base,catalogue);assert.ok(validate(result),JSON.stringify(validate.errors));
});
test('full any-driver capture maps dynamic excess families, percentages, declarations and civil six-month terms',()=>{
 const pick=(name,label)=>{const option=references.collections[name].find(row=>row.text===label);assert.ok(option,`${name}: ${label}`);return option;};
 const own=pick('indemnityOwnVehicles','£15,000');const family=`indemnityOwnVehicles/number:${own.value}/excesses`;
 const raw={proposerCompanyTypeId:1,proposerTitleId:pick('proposerTitles','Mr').value,proposerName:'Fictional',proposerSurname:'Trader',proposerPolicyStartDate:'2026-10-05',proposerPolicyStartTime:'10:00',isShortTerm:true,proposerPostcode:'PE1 1AA',proposerStreet:'Example Lane',proposerTown:'Town',proposerEmail:'fictional@example.invalid',proposerTelephoneNumber:'01632960000',tradingFromId:pick('tradingFroms','Home').value,businessStartedDate:'2020-01-01',businessAnnualTurnover:50000,businessAnnualWageRoll:0,businessIsTradeAssociationMember:false,businessIsVATRegistered:false,businessNumberOfVehiclesHandlePerYear:20,businessNumberOfVehicleCapacity:5,occupations:[{occupationId:pick('mtOccupations','Buying & Selling of Standard Cars & Vans').value,percentage:100}],coverLevelID:pick('coverLevels','Comprehensive').value,coverIndemnityLimitOwnVehiclesID:own.value,coverExcessID:references.collections[family][0].value,coverNCBID:pick('noClaimBonuses','No NCB').value,driverPlanId:pick('driverPlans','Any Driver').value,aadDriverNumber:1,aadDriverMinAgeId:pick('aadDriverMinAge','25').value,aadDriverMaxAgeId:pick('aadDriverMaxAge','65').value,aadMaxVehicleGroupingID:pick('aadMaxVehicleGrouping','Group 28').value,aadMaxVehicleGvwID:pick('aadMaxVehicleGvw','3.5T').value,aadMaxMotorcycleCcID:pick('aadMaxMotorcycleCc','None').value,drivers:[],vehicles:[],specifiedVehicles:[],tradingPremises:[],tradePlateNumbers:[],standardCars:true,percentageOfStandardCars:100,lossOfUseCover:false,vehiclesAtSubcontractorsCover:false,essentialRiskInformation:'Fictional fixture'};
 for(const row of questions.mappings.filter(row=>row.owner.startsWith('MTS-12')&&row.answerKind==='boolean'))raw[row.rawPath]=false;
 const state={version:1,step:14,formData:raw};const result=funnelToProposal(state,base,catalogue);assert.ok(validate(result),JSON.stringify(validate.errors));
 assert.equal(result.termIntent.localEndDate,'2027-04-05');assert.equal(result.termIntent.localEndTime,'10:00');assert.equal(result.cover.responses.answers.find(row=>row.questionId==='MTS-05-Q04').value.collection,family);
 assert.deepEqual(funnelToProposal(state,result,catalogue),result);const roundtrip=proposalToFunnel(result,catalogue);assert.equal(roundtrip.coverExcessID,raw.coverExcessID);assert.equal(roundtrip.percentageOfStandardCars,100);
});
