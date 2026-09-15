import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {reconcileQuoteHistory} from '../scripts/quote-history-reconciliation.mjs';
import {createQuoteValidationPipeline} from '../scripts/quote-validation-pipeline.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const check=(p,date='2026-09-15')=>reconcileQuoteHistory(p,questions,references,date);
const fixture=()=>({productCode:'motor-trade-road-risks',termIntent:{localStartDate:'2027-09-15'},risk:{business:{responses:{answers:[]}},drivers:[{}]}});
const answer=(p,suffix,value)=>{p.risk.business.responses.answers=[{questionId:`prototype.quote.${suffix}`,value}];};

test('three and five year history windows include the anniversary and use trusted assessment date',()=>{
 for(const productCode of questions.products)for(const [key,suffix,boundary,older] of [['convictions','ef70e80708bb','2021-09-15','2021-09-14'],['losses','36da21d3c935','2023-09-15','2023-09-14'],['countyCourtJudgments','922ca15dc9ed','2021-09-15','2021-09-14']]) {
  const p=fixture();p.productCode=productCode;p.risk.drivers[0][key]=[{occurredOn:older}];answer(p,suffix,false);assert.deepEqual(check(p),[]);
  p.risk.drivers[0][key][0].occurredOn=boundary;assert.equal(check(p)[0].code,'history-declaration-required');assert.equal(check(p)[0].relatedPath,`/risk/drivers/0/${key}/0`);
  answer(p,suffix,true);const before=structuredClone(p);assert.deepEqual(check(p),[]);assert.deepEqual(p,before);
 }
 const p=fixture();p.risk.drivers[0].losses=[{occurredOn:'2021-02-28'}];assert.equal(check(p,'2024-02-29').length,1);p.risk.drivers[0].losses[0].occurredOn='2021-02-27';assert.deepEqual(check(p,'2024-02-29'),[]);
});

test('current pending prosecutions and criminal convictions are not hidden by a dated history row',()=>{
 const p=fixture(),id='prototype.addconv.prosecution-status',row=references.collections[id].find(r=>r.value===2);
 p.risk.drivers[0].convictions=[{occurredOn:'2010-01-01',responses:{answers:[{questionId:id,value:{collection:id,value:row.value,label:row.text,version:references.version}}]}}];
 assert.equal(check(p).length,1);p.risk.drivers[0].convictions[0].responses.answers[0].value.label='Forged';assert.deepEqual(check(p),[]); // Reference gate rejects it; no untrusted pending inference.
 p.risk.drivers[0].criminalConvictions=[{occurredOn:'2010-01-01'}];assert.equal(check(p)[0].questionId,'prototype.quote.46414cc10100');
});

test('proposer declarations do not invent named-driver rows and future events cannot validate against future inception',()=>{
 const p=fixture();answer(p,'36da21d3c935',true);assert.deepEqual(check(p),[]);
 p.risk.drivers[0].losses=[{occurredOn:'2026-09-16'}];assert.equal(check(p)[0].code,'history-date-after-assessment');
 assert.throws(()=>check(p,'2026-02-30'),/trusted-as-of-date-required/);
});

test('composed pipeline exposes the conflicting global declaration with the owning incident path',async()=>{
 const {proposal:p,context}=await read('examples/quote-capture-motor-trade-combined.json');
 p.risk.drivers[0].losses=[{id:'aaaaaaaa-0000-4000-8000-000000000099',occurredOn:'2026-08-01'}];
 const validate=await createQuoteValidationPipeline(),result=validate(JSON.stringify(p),context);
 assert.ok(result.issues.some(i=>i.stage==='history-reconciliation'&&i.questionId==='prototype.quote.36da21d3c935'&&i.relatedPath==='/risk/drivers/0/losses/0'));
});
