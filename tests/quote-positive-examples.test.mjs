import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {createQuoteValidationPipeline} from '../scripts/quote-validation-pipeline.mjs';
const validate=await createQuoteValidationPipeline();
const products=['motor-trade-road-risks','motor-trade-combined'];
const read=async (product,scenario)=>JSON.parse(await readFile(new URL(`../contracts/examples/quote-capture-${product}-${scenario}.json`,import.meta.url),'utf8'));
const set=(container,questionId,value)=>{container.responses.answers.find(row=>row.questionId===questionId).value=value;};

test('positive histories, covered plates, optional covers and both European trip kinds compose for both products',async()=>{
 for(const product of products) {
  const example=await read(product,'history-and-extras'),p=example.proposal,before=structuredClone(example);
  assert.deepEqual(validate(JSON.stringify(p),example.context),{status:'section-checks-pass',issues:[]});
  assert.deepEqual(example,before);
  assert.equal(p.risk.drivers[0].convictions.length,1);assert.equal(p.risk.drivers[0].losses.length,1);
  assert.equal(p.risk.heldTradePlates.length,1);assert.equal(p.risk.tradePlates.length,1);
  assert.equal(p.cover.annualEuropeanCover.length,1);assert.equal(p.cover.temporaryEuropeanCover.length,1);
  assert.ok(p.risk.materialFacts.includes('Fictional'));
 }
});

test('positive composed examples reject absent history disclosures, association details and named trip membership',async()=>{
 for(const product of products) {
  const example=await read(product,'history-and-extras');
  const cases=[
   [p=>set(p.risk.business,'prototype.quote.ef70e80708bb',false),'history-declaration-required'],
   [p=>set(p.risk.business,'prototype.quote.36da21d3c935',false),'history-declaration-required'],
   [p=>{delete p.cover.temporaryEuropeanCover[0].driverIds;},'european-trip-drivers-required'],
   [p=>{p.cover.temporaryEuropeanCover[0].endsOn='2027-10-01';},'european-trip-after-policy'],
  ];
  for(const [mutate,code] of cases){const p=structuredClone(example.proposal);mutate(p);assert.ok(validate(JSON.stringify(p),example.context).issues.some(i=>i.code===code),code);}
  const p=structuredClone(example.proposal);set(p.risk.business,'MTS-03-Q05','');
  assert.equal(validate(JSON.stringify(p),example.context).status,'incomplete');
 }
});

test('Any Driver trips pass without fabricated named IDs but stale named links and social use fail',async()=>{
 for(const product of products) {
  const example=await read(product,'any-driver-trip'),p=example.proposal;
  assert.equal(p.risk.drivers.length,0);assert.ok(!('driverIds' in p.cover.temporaryEuropeanCover[0]));
  assert.deepEqual(validate(JSON.stringify(p),example.context),{status:'section-checks-pass',issues:[]});
  p.cover.temporaryEuropeanCover[0].driverIds=['aaaaaaaa-0000-4000-8000-000000000002'];
  assert.ok(validate(JSON.stringify(p),example.context).issues.some(i=>i.stage==='identity'));
  delete p.cover.temporaryEuropeanCover[0].driverIds;
  p.cover.temporaryEuropeanCover[0].usage={...p.cover.temporaryEuropeanCover[0].usage,value:2,label:'Motor Trade with Social, Domestic and Pleasure'};
  assert.ok(validate(JSON.stringify(p),example.context).issues.some(i=>i.code==='european-trip-usage-ineligible'));
 }
});
