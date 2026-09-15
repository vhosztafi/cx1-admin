import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuotePrototypeDetails} from '../scripts/quote-prototype-details.mjs';
import {createQuoteValidationPipeline} from '../scripts/quote-validation-pipeline.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json');
const check=p=>validateQuotePrototypeDetails(p,questions);
const responses=answers=>({answers:answers.map(([questionId,value])=>({questionId,value}))});

test('modal capture preserves optional answers and explicit defaults for both Motor Trade products',async()=>{
 for(const product of questions.products) {
  const {proposal:p}=await read(`examples/quote-capture-${product}.json`);
  assert.deepEqual(check(p),[]);const driver=p.risk.drivers[0];
  assert.ok(!driver.responses.answers.some(a=>a.questionId==='prototype.adddriver.occupation'));
  delete driver.responses;assert.ok(check(p).some(i=>i.questionId==='prototype.adddriver.trade-employment'));
  driver.responses=responses([['prototype.adddriver.trade-employment',{}]]); // Shape/reference gates separately enforce the selected reference.
  driver.fullName=' A ';assert.ok(check(p).some(i=>i.code==='driver-name-too-short'));
  driver.fullName=' Al ';assert.ok(check(p).some(i=>i.code==='driver-name-too-short'));
  delete driver.fullName;assert.deepEqual(check(p),[]);
 }
});

test('each conviction and incident owns its declaration and incident descriptions meet the prototype minimum',()=>{
 const p={productCode:'motor-trade-road-risks',risk:{drivers:[{fullName:'Example Driver',responses:responses([['prototype.adddriver.trade-employment',{}]]),convictions:[{},{}],losses:[{description:'123456789'},{description:' 1234567890 '}]}]}};
 let issues=check(p);assert.equal(issues.filter(i=>i.questionId==='prototype.addconv.prosecution-status').length,2);assert.equal(issues.filter(i=>i.questionId==='prototype.addinc.claim-made').length,2);assert.equal(issues.filter(i=>i.code==='incident-description-too-short').length,1);
 p.risk.drivers[0].convictions.forEach(row=>row.responses=responses([['prototype.addconv.prosecution-status',{}]]));
 p.risk.drivers[0].losses.forEach(row=>{row.responses=responses([['prototype.addinc.claim-made',false]]);row.description='Fictional incident.';});
 const before=structuredClone(p);assert.deepEqual(check(p),[]);assert.deepEqual(p,before); // Damage amounts remain optional, not invented zeroes.
 delete p.risk.drivers[0].losses[1].responses;issues=check(p);assert.equal(issues.length,1);assert.equal(issues[0].path,'/risk/drivers/0/losses/1/responses/answers');
});

test('premises defaults are required only for the applicable product and composed pipeline checks omissions',async()=>{
 const {proposal:p,context}=await read('examples/quote-capture-motor-trade-combined.json');
 p.risk.premises[0].responses.answers.forEach(answer=>answer.value=false);assert.deepEqual(check(p),[]);
 delete p.risk.premises[0].responses;
 assert.equal(check(p).length,2);const validate=await createQuoteValidationPipeline();
 assert.equal(validate(JSON.stringify(p),context).issues.filter(i=>i.stage==='prototype-details').length,2);
 p.productCode='motor-trade-road-risks';assert.equal(check(p).filter(i=>i.code==='inapplicable-prototype-reference').length,2);
 delete p.risk.premises[0].declaredUse;delete p.risk.premises[0].security;assert.deepEqual(check(p),[]); // Source physical-premise type remains separate.
});
