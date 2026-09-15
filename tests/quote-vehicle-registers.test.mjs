import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuoteVehicleRegisters,summarizeQuoteVehicleRegisters} from '../scripts/quote-vehicle-registers.mjs';
import {createQuoteValidationPipeline} from '../scripts/quote-validation-pipeline.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const vehicle=(id,register,mid)=>({id,register,responses:{answers:mid===undefined?[]:[{questionId:'prototype.addveh.report-mid',value:mid}]}});

test('register membership and MID counts preserve one vehicle identity independently of specified membership',()=>{
 const p={risk:{vehicles:[vehicle('a','owned-not-for-sale',true),vehicle('b','held-for-sale',false),vehicle('c','held-for-sale',true)],specifiedVehicleIds:['b']}};
 const before=structuredClone(p);assert.deepEqual(summarizeQuoteVehicleRegisters(p),{owned:['a'],forSale:['b','c'],unassigned:[],total:3,midReportCount:2});assert.deepEqual(p,before);
 p.risk.vehicles[1].register='owned-not-for-sale';assert.deepEqual(summarizeQuoteVehicleRegisters(p).owned,['a','b']);assert.equal(summarizeQuoteVehicleRegisters(p).total,3);
});

test('incomplete vehicle rows remain visible and missing MID declarations do not become yes or no',()=>{
 const p={risk:{vehicles:[vehicle('a',undefined,undefined)]}};
 assert.equal(validateQuoteVehicleRegisters(p)[0].path,'/risk/vehicles/0/register');assert.deepEqual(summarizeQuoteVehicleRegisters(p),{owned:[],forSale:[],unassigned:['a'],total:1,midReportCount:null});
 assert.deepEqual(summarizeQuoteVehicleRegisters({}),{owned:[],forSale:[],unassigned:[],total:0,midReportCount:0});
});

test('prototype add actions retain register modes and composed schemas reject unknown register or caller totals',async()=>{
 const bindings=await read('quote-prototype-bindings.json');assert.deepEqual(bindings.collectionViews.map(v=>[v.addControlId,v.modalMode,v.discriminatorValue]),[['CTL-9387056d9fac','own','owned-not-for-sale'],['CTL-49edfbf20fae','sale','held-for-sale']]);
 const {proposal:p,context}=await read('examples/quote-capture-motor-trade-combined.json'),validate=await createQuoteValidationPipeline();
 delete p.risk.vehicles[0].register;assert.ok(validate(JSON.stringify(p),context).issues.some(i=>i.code==='vehicle-register-required'));
 p.risk.vehicles[0].register='unknown';assert.equal(validate(JSON.stringify(p),context).status,'invalid-draft');
 p.risk.vehicles[0].register='held-for-sale';p.risk.vehicleCount=999;assert.equal(validate(JSON.stringify(p),context).status,'invalid-draft');
});
