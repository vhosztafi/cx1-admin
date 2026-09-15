import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuoteTradePlateInventory} from '../scripts/quote-trade-plate-inventory.mjs';
import {createQuoteValidationPipeline} from '../scripts/quote-validation-pipeline.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json');
const heldId='prototype.quote-value.315960b57ab1',id=n=>`aaaaaaaa-0000-4000-8000-${String(n).padStart(12,'0')}`;
const set=(responses,questionId,value)=>{const entry=responses.answers.find(a=>a.questionId===questionId);if(entry)entry.value=value;else responses.answers.push({questionId,kind:typeof value==='boolean'?'boolean':'count',value});};
const check=p=>validateQuoteTradePlateInventory(p,questions);

test('both products can hold plates without requesting cover and cover a separately identified subset',async()=>{
 const validate=await createQuoteValidationPipeline();
 for(const product of questions.products) {
  const {proposal:p,context}=await read(`examples/quote-capture-${product}.json`);set(p.risk.business.responses,heldId,true);
  p.risk.heldTradePlates=[{id:id(91),number:'TP 123'},{id:id(92),number:'TP 456'}];
  assert.equal(validate(JSON.stringify(p),context).status,'section-checks-pass');
  set(p.risk.responses,'MTS-07-Q01',true);set(p.risk.responses,'MTS-07-Q02',2);p.risk.tradePlates=[{id:id(93),number:'TP123'}];
  const before=structuredClone(p);assert.equal(validate(JSON.stringify(p),context).status,'section-checks-pass');assert.deepEqual(p,before);
  p.risk.tradePlates[0].number='TP789';assert.equal(check(p)[0].code,'covered-trade-plate-not-held');
 }
});

test('held inventory requires complete unique numbers and an explicit declaration',async()=>{
 const {proposal:p}=await read('examples/quote-capture-motor-trade-road-risks.json');set(p.risk.business.responses,heldId,true);
 assert.equal(check(p)[0].code,'held-trade-plate-inventory-required');p.risk.heldTradePlates=[{id:id(91),number:'TP123'},{id:id(92),number:'tp 123'},{id:id(93)}];
 assert.ok(check(p).some(i=>i.code==='duplicate-held-trade-plate'));assert.ok(check(p).some(i=>i.code==='held-trade-plate-number-required'));
 set(p.risk.business.responses,heldId,false);assert.ok(check(p).some(i=>i.code==='inactive-held-trade-plates'));
 set(p.risk.responses,'MTS-07-Q01',true);assert.ok(check(p).some(i=>i.code==='covered-trade-plates-require-held-declaration'));
 p.risk.business.responses.answers=p.risk.business.responses.answers.filter(a=>a.questionId!==heldId);assert.ok(check(p).some(i=>i.code==='trade-plates-held-answer-required'));
});

test('held plate IDs share the strict child identity gate and the prototype binding targets inventory',async()=>{
 const {proposal:p,context}=await read('examples/quote-capture-motor-trade-combined.json'),validate=await createQuoteValidationPipeline();
 p.risk.heldTradePlates=[{number:'TP123'}];assert.equal(validate(JSON.stringify(p),context).status,'invalid-draft');
 p.risk.heldTradePlates[0].id=p.risk.drivers[0].id;assert.ok(validate(JSON.stringify(p),context).issues.some(i=>i.code==='duplicate-item-id'));
 const bindings=await read('quote-prototype-bindings.json');assert.deepEqual(bindings.controls.find(c=>c.controlId==='CTL-58b5bc3f356b').bindings[0].paths,['risk.heldTradePlates']);
});
