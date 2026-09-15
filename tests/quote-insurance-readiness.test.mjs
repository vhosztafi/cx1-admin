import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import {validateQuoteInsuranceReadiness} from '../scripts/quote-insurance-readiness.mjs';
import {validateQuoteQuestions,validateQuoteReferences,quoteMappingsForProduct} from '../scripts/quote-semantic-contract.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);const draft=ajv.compile(await read('schemas/quote-draft.schema.json'));
const ref=(collection,value=references.collections[collection][0].value)=>({collection,value,label:references.collections[collection].find(row=>row.value===value).text,version:references.version});
const set=(p,n,value)=>{
 const insurance=p.risk.previousInsurance;
 if(n===12){insurance.noClaimsBonusExpiresOn=value;return;}
 const questionId=`MTS-05-Q${n}`,row=questions.mappings.find(row=>row.owner===questionId);
 const existing=insurance.responses.answers.find(answer=>answer.questionId===questionId);
 if(existing)existing.value=value;else insurance.responses.answers.push({questionId,kind:row.answerKind,value});
};
const fixture=()=>{const p={schemaVersion:'1.0',productCode:'motor-trade-road-risks',risk:{previousInsurance:{responses:{questionSetVersion:questions.version,answers:[]}}}};set(p,10,ref('noClaimBonuses',1));return p;};
const check=p=>validateQuoteInsuranceReadiness(p,questions,references);
const withNcb=()=>{const p=fixture();set(p,10,ref('noClaimBonuses',2));set(p,11,ref('noClaimBonusesEarned',3));set(p,12,'2026-01-01');set(p,13,ref('noClaimBonusPreviousInsurers',18));set(p,14,false);set(p,17,false);return p;};

test('no-NCB and existing NCB sections validate for both products with explicit false declarations',()=>{
 for(const productCode of questions.products)for(const p of [fixture(),withNcb()]) {
  p.productCode=productCode;assert.equal(draft(p),true,JSON.stringify(draft.errors));
  assert.deepEqual(validateQuoteQuestions(p,quoteMappingsForProduct(questions,productCode),questions.version),[]);
  assert.deepEqual(validateQuoteReferences(p,references),[]);assert.deepEqual(check(p),[]);
 }
 assert.equal(check({schemaVersion:'1.0',productCode:'motor-trade-road-risks'})[0].questionId,'MTS-05-Q10');
});

test('held NCB requires source, insurer and separate NCB expiry rather than policy expiry',()=>{
 for(const n of [11,12,13]) {
  const p=withNcb(),insurance=p.risk.previousInsurance;
  if(n===12){delete insurance.noClaimsBonusExpiresOn;insurance.expiresOn='2026-01-01';}
  else insurance.responses.answers=insurance.responses.answers.filter(answer=>answer.questionId!==`MTS-05-Q${n}`);
  assert.ok(check(p).some(issue=>issue.code==='insurance-answer-required'&&issue.questionId===`MTS-05-Q${n}`));
 }
 const p=withNcb();set(p,10,ref('noClaimBonuses',1));const before=structuredClone(p);
 assert.ok(check(p).some(issue=>issue.code==='inactive-insurance-answer-retained'));assert.deepEqual(p,before);
});

test('introductory renewal uses pinned insurer metadata and requires years only after yes',()=>{
 for(const insurer of references.collections.noClaimBonusPreviousInsurers.filter(row=>row.requireNCBIntroRenewal)) {
  const p=withNcb();set(p,13,ref('noClaimBonusPreviousInsurers',insurer.value));set(p,14,true);
  assert.equal(check(p)[0].questionId,'MTS-05-Q15');set(p,15,ref('noClaimBonusIntroRenewalYears'));assert.deepEqual(check(p),[]);
  set(p,14,false);assert.ok(check(p).some(issue=>issue.code==='inactive-insurance-answer-retained'&&issue.questionId==='MTS-05-Q15'));
 }
 const p=withNcb();p.risk.previousInsurance.responses.answers.find(answer=>answer.questionId==='MTS-05-Q13').value.label='Forged';
 assert.ok(check(p).some(issue=>issue.code==='insurance-context-required'&&issue.questionId==='MTS-05-Q14'));
});

test('other insurer details, NCB protection and date/text bounds follow their own conditions',()=>{
 const p=withNcb();set(p,13,ref('noClaimBonusPreviousInsurers',21));
 p.risk.previousInsurance.responses.answers=p.risk.previousInsurance.responses.answers.filter(answer=>answer.questionId!=='MTS-05-Q14');
 assert.equal(check(p)[0].questionId,'MTS-05-Q16');set(p,16,'Example insurer');assert.deepEqual(check(p),[]);
 set(p,16,' '.repeat(2));assert.equal(check(p)[0].code,'insurance-answer-required');
 set(p,16,'a'.repeat(51));set(p,12,'1899-12-31');assert.deepEqual(check(p).map(issue=>issue.code),['ncb-expiry-too-early','insurer-details-too-long']);
 set(p,11,ref('noClaimBonusesEarned',1));assert.ok(check(p).some(issue=>issue.code==='inactive-insurance-answer-retained'&&issue.questionId==='MTS-05-Q17'));
});
