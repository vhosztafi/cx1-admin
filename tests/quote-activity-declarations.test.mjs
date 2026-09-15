import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {reconcileQuoteActivityDeclarations} from '../scripts/quote-activity-declarations.mjs';
import {createQuoteValidationPipeline} from '../scripts/quote-validation-pipeline.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ref=value=>({collection:'mtOccupations',value,label:references.collections.mtOccupations.find(r=>r.value===value).text,version:references.version});
const check=p=>reconcileQuoteActivityDeclarations(p,questions,references);

test('motorcycle, import/export and salvage activities require their matching declarations for both products',()=>{
 for(const productCode of questions.products)for(const [values,questionId] of [[[5,6,16,17],'prototype.quote.35350a32e79e'],[[13],'prototype.quote.2ad460339240'],[[23,29],'prototype.quote.ba9d4158ae2c']]) {
  for(const value of values) {
   const p={productCode,risk:{business:{activities:[{code:ref(value),turnoverBasisPoints:100}],responses:{answers:[{questionId,value:false}]}}}};
   assert.equal(check(p)[0].questionId,questionId);assert.equal(check(p)[0].relatedPath,'/risk/business/activities/0/code');
   p.risk.business.responses.answers[0].value=true;const before=structuredClone(p);assert.deepEqual(check(p),[]);assert.deepEqual(p,before);
  }
 }
});

test('activity reconciliation avoids label inference, duplicate declaration errors and unrelated appetite assumptions',()=>{
 const p={productCode:'motor-trade-road-risks',risk:{business:{activities:[{code:ref(5),turnoverBasisPoints:5000},{code:ref(16),turnoverBasisPoints:5000}]}}};
 assert.equal(check(p).length,1);
 p.risk.business.activities.forEach(row=>row.code.label='Forged');assert.deepEqual(check(p),[]); // Pinned reference gate diagnoses forged entries.
 for(const value of [7,8,18,34,37]){p.risk.business.activities=[{code:ref(value),turnoverBasisPoints:100}];assert.deepEqual(check(p),[]);}
 p.risk.business.activities=[{code:ref(5),turnoverBasisPoints:0}];assert.deepEqual(check(p),[]); // Activity readiness rejects a zero row independently.
});

test('fictional standard-car demos use the correct occupation and composed checks catch motorcycle drift',async()=>{
 const validate=await createQuoteValidationPipeline();
 for(const product of questions.products) {
  const {proposal:p,context}=await read(`examples/quote-capture-${product}.json`);
  assert.equal(p.risk.business.activities[0].code.value,8);assert.equal(p.risk.business.activities[0].code.label,'Buying & Selling of Standard Cars & Vans');
  p.risk.business.activities[0].code=ref(5);assert.ok(validate(JSON.stringify(p),context).issues.some(i=>i.stage==='activity-declarations'&&i.questionId==='prototype.quote.35350a32e79e'));
 }
});
