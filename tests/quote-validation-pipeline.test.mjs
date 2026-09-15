import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {createQuoteValidationPipeline} from '../scripts/quote-validation-pipeline.mjs';
const validate=await createQuoteValidationPipeline();
const context={asOfDate:'2026-09-15'};
const base=()=>({schemaVersion:'1.0',productCode:'motor-trade-road-risks'});

test('both fictional Motor Trade examples pass every composed section with persisted-context inputs separated',async()=>{
 for(const product of ['motor-trade-road-risks','motor-trade-combined']) {
  const example=JSON.parse(await readFile(new URL(`../contracts/examples/quote-capture-${product}.json`,import.meta.url),'utf8'));
  const text=JSON.stringify(example.proposal);const before=structuredClone(example);
  assert.deepEqual(validate(text,example.context),{status:'section-checks-pass',issues:[]});assert.deepEqual(example,before);
  assert.equal(example.proposal.productCode,product);assert.equal(example.proposal.risk.premises?.length??0,product==='motor-trade-combined'?1:0);
  assert.ok(!('context' in example.proposal));
 }
});

test('composed examples fail after cross-section drift, missing trusted lookup context or forged reference labels',async()=>{
 const example=JSON.parse(await readFile(new URL('../contracts/examples/quote-capture-motor-trade-combined.json',import.meta.url),'utf8'));
 const check=p=>validate(JSON.stringify(p),example.context);
 let p=structuredClone(example.proposal);p.risk.vehicles[0].value='10000.01';assert.ok(check(p).issues.some(i=>i.stage==='vehicle-limits'&&i.code==='vehicle-value-limit-exceeded'));
 p=structuredClone(example.proposal);p.insured.address.postcode='ZZ1 1ZZ';p.risk.premises[0].address.postcode='ZZ1 1ZZ';assert.ok(check(p).issues.some(i=>i.stage==='overnight'));
 p=structuredClone(example.proposal);p.risk.business.activities[0].code.label='Forged';assert.ok(check(p).issues.some(i=>i.stage==='references'&&i.code==='reference-label-mismatch'));
 assert.ok(validate(JSON.stringify(example.proposal),{asOfDate:example.context.asOfDate}).issues.some(i=>i.code==='vehicle-capture-context-required'));
});

test('pipeline gates duplicate JSON and forbidden proposal shapes before semantic traversal',()=>{
 for(const text of ['{"schemaVersion":"1.0","schemaVersion":"1.0"}','{bad json'])assert.equal(validate(text,context).status,'invalid-draft');
 for(const patch of [{premium:{}},{risk:{drivers:[null]}},{productCode:'commercial-combined'}]) {
  const result=validate(JSON.stringify({...base(),...patch}),context);assert.equal(result.status,'invalid-draft');assert.ok(result.issues.every(i=>i.stage==='schema'));
 }
});

test('pipeline composes every applicable section for omitted containers without claiming full readiness',()=>{
 for(const productCode of ['motor-trade-road-risks','motor-trade-combined']) {
  const result=validate(JSON.stringify({...base(),productCode}),context);
  assert.equal(result.status,'incomplete');
  for(const stage of ['term','business','driver-plan','insurance','activity','portfolio','cover','extras','vehicle'])assert.ok(result.issues.some(i=>i.stage===stage),stage);
  assert.ok(!('ready' in result));assert.ok(!JSON.stringify(result).includes('undefined'));
 }
});

test('pipeline blocks duplicate identities and unknown questions before domain rules, and uses a trusted as-of date',()=>{
 const id='aaaaaaaa-0000-4000-8000-000000000001';
 let p={...base(),risk:{drivers:[{id},{id:id.toUpperCase()}]}};
 let result=validate(JSON.stringify(p),context);assert.equal(result.status,'invalid-draft');assert.ok(result.issues.some(i=>i.stage==='identity'));
 p={...base(),risk:{responses:{questionSetVersion:'forged',answers:[{questionId:'forged',kind:'boolean',value:true}]}}};
 result=validate(JSON.stringify(p),context);assert.equal(result.status,'invalid-draft');assert.ok(result.issues.every(i=>i.stage==='questions'));
 assert.throws(()=>validate(JSON.stringify(base()),{asOfDate:'2026-02-30'}),/trusted-as-of-date-required/);
});
