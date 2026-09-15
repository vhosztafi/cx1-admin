import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuoteDriverPlan} from '../scripts/quote-driver-plan.mjs';
import {quoteMappingsForProduct,validateQuoteQuestions,validateQuoteReferences} from '../scripts/quote-semantic-contract.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ref=(collection,value=references.collections[collection][0].value)=>({collection,value,label:references.collections[collection].find(row=>row.value===value).text,version:references.version});
const fixture=(plan=1)=>{
 const answers=[{questionId:'MTS-06-Q01',kind:'reference',value:ref('driverPlans',plan)}];
 if(plan!==1)for(let n=2;n<=7;n++) {
  const questionId=`MTS-06-Q0${n}`,row=questions.mappings.find(row=>row.owner===questionId);
  answers.push({questionId,kind:row.answerKind,value:n===2?1:ref(row.optionCollection)});
 }
 return {schemaVersion:'1.0',productCode:'motor-trade-road-risks',risk:{responses:{questionSetVersion:questions.version,answers},drivers:plan===3?[]:[{id:'aaaaaaaa-0000-4000-8000-000000000001'}]}};
};
const check=p=>validateQuoteDriverPlan(p,questions,references);

test('all driver plan branches accept correctly separated named and unnamed populations for both products',()=>{
 for(const productCode of questions.products)for(const plan of [1,2,3]) {
  const p=fixture(plan);p.productCode=productCode;
  assert.deepEqual(check(p),[]);
  assert.deepEqual(validateQuoteQuestions(p,quoteMappingsForProduct(questions,productCode),questions.version),[]);
  assert.deepEqual(validateQuoteReferences(p,references),[]);
 }
});

test('any driver plans require each configured field and a positive integral count',()=>{
 for(const plan of [2,3])for(let n=2;n<=7;n++) {
  const p=fixture(plan);p.risk.responses.answers=p.risk.responses.answers.filter(a=>a.questionId!==`MTS-06-Q0${n}`);
  assert.ok(check(p).some(issue=>issue.code==='any-driver-answer-required'&&issue.questionId===`MTS-06-Q0${n}`));
 }
 for(const value of [0,1.5]){const p=fixture(3);p.risk.responses.answers[1].value=value;assert.equal(check(p)[0].code,'positive-any-driver-count-required');}
});

test('plan transitions retain incompatible data as issues instead of silently deleting it',()=>{
 const p=fixture(2);p.risk.responses.answers[0].value=ref('driverPlans',1);const before=structuredClone(p);
 assert.equal(check(p).filter(i=>i.code==='inactive-any-driver-answer-retained').length,6);assert.deepEqual(p,before);
 p.risk.responses.answers[0].value=ref('driverPlans',3);assert.equal(check(p)[0].code,'inactive-named-drivers-retained');
 p.risk.responses.answers[0].value=ref('driverPlans',2);p.risk.drivers=[];assert.equal(check(p)[0].code,'named-driver-required');
 p.risk.responses.answers[0].value.label='Forged';assert.equal(check(p)[0].code,'driver-plan-context-required');
 assert.equal(check({productCode:'motor-trade-road-risks'})[0].code,'driver-plan-required');
});
