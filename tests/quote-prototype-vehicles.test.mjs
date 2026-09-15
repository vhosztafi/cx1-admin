import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuotePrototypeVehicles} from '../scripts/quote-prototype-vehicles.mjs';
import {createQuoteValidationPipeline} from '../scripts/quote-validation-pipeline.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const characteristic='prototype.addveh.special-characteristics';
const set=(p,value)=>{p.risk.vehicles[0].responses.answers[0].value={collection:characteristic,value,label:references.collections[characteristic].find(r=>r.value===value).text,version:references.version};};
const declare=(p,id)=>{p.risk.business.responses.answers.find(a=>a.questionId===`prototype.quote.${id}`).value=true;};
const fixture=async()=> (await read('examples/quote-capture-motor-trade-road-risks.json')).proposal;
const check=p=>validateQuotePrototypeVehicles(p,questions,references);

test('all six vehicle characteristic choices retain distinct meanings and require consistent declarations',async()=>{
 for(const productCode of questions.products) {
  let p=await fixture();p.productCode=productCode;assert.deepEqual(check(p),[]);
  p.risk.vehicles[0].imported=true;assert.deepEqual(check(p),[]); // Manufacturer import is not a grey import.
  for(const [option,declaration] of [[2,'5f9e8331ac6f'],[3,'488ecf4bdc09'],[4,'87fad6b4a9fe'],[5,'d7a75768e505']]) {
   p=await fixture();p.productCode=productCode;set(p,option);assert.ok(check(p).some(i=>i.code==='vehicle-characteristic-declaration-required'));
   declare(p,declaration);if(option===2)p.risk.vehicles[0].modified=true;if(option===4)p.risk.vehicles[0].imported=true;
   assert.deepEqual(check(p),[]);
  }
  p=await fixture();set(p,6);declare(p,'488ecf4bdc09');assert.ok(check(p).some(i=>i.code==='multiple-vehicle-characteristics-context-required'));
  declare(p,'d7a75768e505');assert.deepEqual(check(p),[]);const before=structuredClone(p);check(p);assert.deepEqual(p,before);
 }
});

test('modification and import contradictions cannot hide behind selected options or specified vehicle membership',async()=>{
 const p=await fixture(),vehicle=p.risk.vehicles[0];p.risk.specifiedVehicleIds=[vehicle.id];vehicle.modified=true;
 assert.ok(check(p).some(i=>i.code==='conflicting-vehicle-modification'));assert.ok(check(p).some(i=>i.code==='vehicle-characteristic-declaration-required'));
 set(p,2);declare(p,'5f9e8331ac6f');assert.deepEqual(check(p),[]);
 vehicle.modified=false;assert.ok(check(p).some(i=>i.code==='conflicting-vehicle-modification'));
 set(p,4);declare(p,'87fad6b4a9fe');assert.ok(check(p).some(i=>i.code==='conflicting-vehicle-import'));
 delete vehicle.imported;assert.deepEqual(check(p),[]); // Lookup-mode generic import field may be absent.
});

test('MID false is an explicit capture answer and composed validation rejects missing or forged vehicle declarations',async()=>{
 const {proposal:p,context}=await read('examples/quote-capture-motor-trade-combined.json'),validate=await createQuoteValidationPipeline();
 p.risk.vehicles[0].responses.answers[1].value=false;assert.deepEqual(check(p),[]);assert.equal(validate(JSON.stringify(p),context).status,'section-checks-pass');
 p.risk.vehicles[0].responses.answers[0].value.label='Forged';assert.ok(validate(JSON.stringify(p),context).issues.some(i=>i.stage==='references'));
 delete p.risk.vehicles[0].responses;const result=validate(JSON.stringify(p),context);
 assert.equal(result.status,'incomplete');assert.equal(result.issues.filter(i=>i.stage==='prototype-vehicles').length,2);
});
