import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuoteVehicleOwnership} from '../scripts/quote-vehicle-ownership.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ref=(collection,value)=>({collection,value,label:references.collections[collection].find(row=>row.value===value).text,version:references.version});
const driverId='aaaaaaaa-0000-4000-8000-000000000001',vehicleId='aaaaaaaa-0000-4000-8000-000000000002';
const fixture=()=>({productCode:'motor-trade-road-risks',insured:{declaredCompanyType:ref('companyTypes',1)},risk:{drivers:[{id:driverId,relationship:ref('driverRelationshipsPolicyHolder',2),responses:{answers:[{questionId:'MTS-06-Q31',kind:'boolean',value:true}]}}],vehicles:[{id:vehicleId,declaredOwnerType:ref('vehicleOwnerTypes',1)}]}});
const check=p=>validateQuoteVehicleOwnership(p,questions,references);

test('vehicle non-driver owner types follow each company branch for both products',()=>{
 for(const productCode of questions.products)for(const company of [1,2,3,4])for(const owner of [1,2,3]) {
  const p=fixture();p.productCode=productCode;p.insured.declaredCompanyType=ref('companyTypes',company);p.risk.vehicles[0].declaredOwnerType=ref('vehicleOwnerTypes',owner);
  assert.equal(check(p).some(i=>i.code==='vehicle-owner-type-ineligible'),!(company===1?[1]:company===4?[2]:[2,3]).includes(owner));
 }
});

test('driver ownership requires current proposal membership, trusted relationship and affirmative personal cover',()=>{
 const p=fixture(),v=p.risk.vehicles[0],d=p.risk.drivers[0];v.declaredOwnerType=ref('vehicleOwnerTypes',4);
 assert.equal(check(p)[0].code,'vehicle-owner-driver-required');v.ownerDriverId=driverId.toUpperCase();assert.deepEqual(check(p),[]);
 d.responses.answers[0].value=false;assert.equal(check(p)[0].code,'vehicle-owner-personal-cover-required');
 p.risk.specifiedVehicleIds=[vehicleId.toUpperCase()];assert.equal(check(p)[0].code,'vehicle-owner-personal-cover-required');
 d.responses.answers=[];assert.equal(check(p)[0].code,'vehicle-owner-cover-context-required');
 d.relationship.label='Forged';assert.equal(check(p)[0].code,'vehicle-owner-relationship-context-required');
 p.risk.drivers=[];assert.equal(check(p)[0].code,'vehicle-owner-driver-not-in-proposal');
});

test('sole-trader policyholder option differs for specified vehicles without deleting retained links',()=>{
 const p=fixture(),v=p.risk.vehicles[0];v.declaredOwnerType=ref('vehicleOwnerTypes',4);v.ownerDriverId=driverId;p.risk.drivers[0].relationship=ref('driverRelationshipsPolicyHolder',3);
 assert.equal(check(p)[0].code,'policyholder-driver-owner-not-selectable');p.risk.specifiedVehicleIds=[vehicleId];assert.deepEqual(check(p),[]);
 v.declaredOwnerType=ref('vehicleOwnerTypes',1);const before=structuredClone(p);assert.equal(check(p)[0].code,'inactive-owner-driver-retained');assert.deepEqual(p,before);
 p.insured.declaredCompanyType.label='Forged';assert.equal(check(p)[0].code,'vehicle-owner-context-required');
});
