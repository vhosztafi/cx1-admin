import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import {validateQuoteVehicleReadiness,requiredVehicleFields,manualVehicleFields} from '../scripts/quote-vehicle-readiness.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);const draft=ajv.compile(await read('schemas/quote-draft.schema.json'));
const id='aaaaaaaa-0000-4000-8000-000000000001';
const ref=(collection,value=references.collections[collection][0].value)=>({collection,value,label:references.collections[collection].find(row=>row.value===value).text,version:references.version});
const type=category=>ref('vehicleType',references.collections.vehicleType.find(row=>row.category===category).value);
const fixture=()=>({schemaVersion:'1.0',productCode:'motor-trade-road-risks',risk:{specifiedVehiclesRequested:false,vehicles:[{id,registration:'DEMO 01',abiCode:'DEMO',abiGroup:1,make:'Example',model:'Demo',vehicleType:type('Car'),bodyDescription:'Saloon',registrationYear:2020,registeredOn:'2020-01-01',imported:false,value:'10000.00',purchasedOn:'2020-01-01',declaredOwnerType:ref('vehicleOwnerTypes'),keptOvernightType:ref('vehicleKeptOvernightType'),keptOvernightAddress:'Example address',partOfLeaseAgreement:false,customerLoan:false,modified:false}]}});
const check=(proposal,modes={[id]:'manual'})=>validateQuoteVehicleReadiness(proposal,questions,references,modes);

test('manual vehicle sections accept both products and require each source field without dropping false',()=>{
 for(const product of questions.products){const p=fixture();p.productCode=product;assert.equal(draft(p),true,JSON.stringify(draft.errors));assert.deepEqual(check(p),[]);}
 for(const field of [...requiredVehicleFields,...manualVehicleFields,'customerLoan']) {
  const p=fixture();delete p.risk.vehicles[0][field];
  assert.ok(check(p).some(issue=>issue.code==='required-vehicle-field'&&issue.path===`/risk/vehicles/0/${field}`),field);
 }
});

test('lookup versus manual requirements use trusted per-vehicle context and pinned category',()=>{
 const p=fixture(),v=p.risk.vehicles[0];delete v.make;delete v.model;
 assert.deepEqual(check(p,{[id]:'lookup'}),[]);
 assert.ok(check(p,{}).some(issue=>issue.code==='vehicle-capture-context-required'));
 v.make='Example';v.model='Demo';v.vehicleType=type('Motorcycle');delete v.abiGroup;
 assert.deepEqual(check(p).map(issue=>issue.path),['/risk/vehicles/0/declaredEngineSize','/risk/vehicles/0/grossWeightKg']);
 v.declaredEngineSize='125';v.grossWeightKg=150;assert.deepEqual(check(p),[]);
 v.vehicleType.label='Forged';assert.ok(check(p).some(issue=>issue.code==='vehicle-category-context-required'));
});

test('specified vehicle declaration, membership and exact minimum value cannot be confused with ordinary capture',()=>{
 const p=fixture(),v=p.risk.vehicles[0];p.risk.specifiedVehiclesRequested=true;
 assert.equal(check(p)[0].code,'specified-vehicle-required');p.risk.specifiedVehicleIds=[id.toUpperCase()];delete v.customerLoan;
 v.value='49999.99';assert.equal(check(p)[0].code,'specified-vehicle-value-minimum');
 v.value='50000.00';assert.deepEqual(check(p),[]);
 p.risk.specifiedVehiclesRequested=false;assert.equal(check(p)[0].code,'inactive-specified-vehicles-retained');
 delete p.risk.specifiedVehiclesRequested;assert.equal(check(p)[0].code,'specified-vehicle-declaration-required');
});

test('lease, modification and source date requirements preserve captured data for correction',()=>{
 const p=fixture(),v=p.risk.vehicles[0];v.partOfLeaseAgreement=true;v.modified=true;
 assert.deepEqual(check(p).map(issue=>issue.code),['required-vehicle-field','vehicle-modification-required']);
 v.leaseLengthYears=0;v.modifications=[{id:'aaaaaaaa-0000-4000-8000-000000000002'}];
 assert.equal(check(p)[0].code,'vehicle-modification-code-required');
 v.modified=false;const before=structuredClone(p);assert.ok(check(p).some(issue=>issue.code==='inactive-vehicle-modifications-retained'));assert.deepEqual(p,before);
 delete v.modifications;v.registeredOn='1899-12-31';v.purchasedOn='1899-12-31';v.registrationYear=1899;v.registration='DEMO!';
 assert.deepEqual(check(p).map(issue=>issue.code),['vehicle-registration-invalid','vehicle-date-too-early','vehicle-date-too-early','vehicle-year-too-early']);
});
