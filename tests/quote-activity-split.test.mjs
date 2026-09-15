import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuoteActivitySplit,activitySplitKeys} from '../scripts/quote-activity-split.mjs';
import {createQuoteValidationPipeline} from '../scripts/quote-validation-pipeline.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ref=value=>({collection:'mtOccupations',value,label:references.collections.mtOccupations.find(row=>row.value===value).text,version:references.version});
const check=p=>validateQuoteActivitySplit(p,questions,references);
const zero=()=>Object.fromEntries(activitySplitKeys.map(key=>[key,0]));

test('all seven prototype shares retain distinct paths, explicit zeroes and an exact total',async()=>{
 const manifest=await read('quote-prototype-bindings.json');const splitBindings=manifest.controls.flatMap(c=>c.bindings).filter(b=>b.paths[0].startsWith('risk.business.declaredActivitySplit.'));
 assert.equal(splitBindings.length,7);assert.equal(new Set(splitBindings.map(b=>b.paths[0])).size,7);assert.ok(splitBindings.every(b=>b.unit==='basis-points'));
 for(const product of questions.products) {
  const {proposal:p}=await read(`examples/quote-capture-${product}.json`);assert.deepEqual(check(p),[]);
  p.risk.business.declaredActivitySplit.sales=9999;assert.ok(check(p).some(i=>i.code==='activity-split-total-invalid'));
  p.risk.business.declaredActivitySplit.sales=10000;delete p.risk.business.declaredActivitySplit.other;assert.ok(check(p).some(i=>i.path.endsWith('/other')));
 }
});

test('other activity needs a nonblank explanation and cannot retain one without its positive share',()=>{
 const p={productCode:'motor-trade-road-risks',risk:{business:{declaredActivitySplit:{...zero(),other:10000},responses:{answers:[]}}}};
 assert.equal(check(p)[0].code,'other-activity-description-required');p.risk.business.responses.answers=[{questionId:'prototype.quote-value.3e4fdf2b682e',value:' '}];assert.equal(check(p)[0].code,'other-activity-description-required');
 p.risk.business.responses.answers[0].value='Fictional specialist activity.';assert.deepEqual(check(p),[]);
 p.risk.business.declaredActivitySplit={...zero(),sales:10000};assert.equal(check(p)[0].code,'inactive-other-activity-description');
});

test('known occupations constrain matching totals without inventing servicing versus mechanical allocations',()=>{
 for(const [code,key] of [[8,'sales'],[2,'breakdownRecovery'],[10,'bodyRepairs'],[26,'valeting']]) {
  const p={productCode:'motor-trade-road-risks',risk:{business:{declaredActivitySplit:{...zero(),[key]:10000},activities:[{code:ref(code),turnoverBasisPoints:10000}]}}};
  assert.deepEqual(check(p),[]);p.risk.business.declaredActivitySplit[key]=0;assert.ok(check(p).some(i=>i.code==='activity-split-below-declared-occupations'));
 }
 const p={productCode:'motor-trade-road-risks',risk:{business:{declaredActivitySplit:{...zero(),servicing:2500,mechanicalRepair:7500},activities:[{code:ref(18),turnoverBasisPoints:10000}]}}};
 const before=structuredClone(p);assert.deepEqual(check(p),[]);assert.deepEqual(p,before);p.risk.business.declaredActivitySplit.mechanicalRepair=5000;assert.ok(check(p).some(i=>i.code==='activity-split-below-declared-occupations'));
 p.risk.business.activities[0].code.label='Forged';assert.ok(!check(p).some(i=>i.code==='activity-split-below-declared-occupations'));
});

test('composed schema permits incomplete shaped split but rejects extra shares, fractions and out-of-range values',async()=>{
 const {proposal:p,context}=await read('examples/quote-capture-motor-trade-combined.json'),validate=await createQuoteValidationPipeline();
 p.risk.business.declaredActivitySplit={sales:10000};assert.equal(validate(JSON.stringify(p),context).status,'incomplete');
 for(const split of [{...zero(),sales:10001},{...zero(),sales:1.5},{...zero(),arbitrary:0}]){p.risk.business.declaredActivitySplit=split;assert.equal(validate(JSON.stringify(p),context).status,'invalid-draft');}
});
