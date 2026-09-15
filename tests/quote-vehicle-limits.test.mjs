import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuoteVehicleLimits} from '../scripts/quote-vehicle-limits.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ref=(collection,value)=>({collection,value,label:references.collections[collection].find(row=>row.value===value).text,version:references.version});
const a=(questionId,collection,value)=>({questionId,kind:'reference',value:ref(collection,value)});
const type=category=>ref('vehicleType',references.collections.vehicleType.find(row=>row.category===category).value);
const id='aaaaaaaa-0000-4000-8000-000000000001';
const fixture=()=>({productCode:'motor-trade-road-risks',risk:{responses:{answers:[a('MTS-06-Q01','driverPlans',1)]},drivers:[{responses:{answers:[a('MTS-06-Q27','driverMaxABIGroups',1),a('MTS-06-Q20','driverGVWLimits',1),a('MTS-06-Q25','driverMotorcycleCovers',2)]}}],vehicles:[{id,vehicleType:type('Car'),abiGroup:28,value:'10000.00'}]},cover:{responses:{answers:[a('MTS-05-Q01','coverLevels',1),a('MTS-05-Q02','indemnityOwnVehicles',5)]}}});
const check=p=>validateQuoteVehicleLimits(p,questions,references);

test('ABI and ordinary value limits use pinned amounts with exact boundaries for both products',()=>{
 for(const productCode of questions.products){const p=fixture();p.productCode=productCode;assert.deepEqual(check(p),[]);p.risk.vehicles[0].abiGroup=29;p.risk.vehicles[0].value='10000.01';assert.deepEqual(check(p).map(i=>i.code),['vehicle-abi-limit-exceeded','vehicle-value-limit-exceeded']);p.risk.specifiedVehicleIds=[id.toUpperCase()];assert.deepEqual(check(p).map(i=>i.code),['vehicle-abi-limit-exceeded']);}
 const p=fixture();p.risk.drivers[0].responses.answers[0].value.label='Group 99';assert.equal(check(p)[0].code,'vehicle-abi-context-required');
});

test('mixed named/any driver ABI and GVW aggregate from their metadata while absent selections fail closed',()=>{
 const p=fixture();p.risk.responses.answers[0]=a('MTS-06-Q01','driverPlans',2);p.risk.responses.answers.push(a('MTS-06-Q05','aadMaxVehicleGrouping',2),a('MTS-06-Q06','aadMaxVehicleGvw',2),a('MTS-06-Q07','aadMaxMotorcycleCc',2));
 p.risk.vehicles[0].abiGroup=35;assert.deepEqual(check(p),[]);
 p.risk.vehicles[0].vehicleType=type('Commercial');p.risk.vehicles[0].grossWeightKg=7500;assert.deepEqual(check(p),[]);
 p.risk.vehicles[0].grossWeightKg=7501;assert.ok(check(p).some(i=>i.code==='vehicle-gvw-limit-exceeded'));
 p.risk.responses.answers=p.risk.responses.answers.filter(a=>a.questionId!=='MTS-06-Q06');assert.ok(check(p).some(i=>i.code==='vehicle-gvw-context-required'));
});

test('motorcycle limits distinguish explicit unlimited, missing metadata and finite limits',()=>{
 const p=fixture(),v=p.risk.vehicles[0];v.vehicleType=type('Motorcycle');delete v.abiGroup;v.grossWeightKg=150;v.declaredEngineSize='250';assert.deepEqual(check(p),[]);
 v.declaredEngineSize='251';assert.equal(check(p)[0].code,'motorcycle-cc-limit-exceeded');v.declaredEngineSize='unknown';assert.equal(check(p)[0].code,'motorcycle-engine-size-invalid');
 p.risk.drivers[0].responses.answers[2]=a('MTS-06-Q25','driverMotorcycleCovers',6);v.declaredEngineSize='2000';assert.deepEqual(check(p),[]);
 p.risk.drivers[0].responses.answers[2].value.label='Forged';assert.equal(check(p)[0].code,'vehicle-motorcycle-context-required');
 p.risk.drivers[0].responses.answers[2]=a('MTS-06-Q25','driverMotorcycleCovers',1);assert.equal(check(p)[0].code,'motorcycle-cover-not-selected');
});
