import test from 'node:test';
import assert from 'node:assert/strict';
import {createQuoteValidationPipeline} from '../scripts/quote-validation-pipeline.mjs';
const validate=await createQuoteValidationPipeline();
const context={asOfDate:'2026-09-15'};
const base=()=>({schemaVersion:'1.0',productCode:'motor-trade-road-risks'});

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
