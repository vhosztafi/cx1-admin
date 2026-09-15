import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuoteCoverReadiness} from '../scripts/quote-cover-readiness.mjs';
import {validateQuoteReferences} from '../scripts/quote-semantic-contract.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ref=(collection,value)=>({collection,value,label:references.collections[collection].find(row=>row.value===value).text,version:references.version});
const answer=(id,value)=>({questionId:id,kind:typeof value==='boolean'?'boolean':'reference',value});
const fixture=()=>({schemaVersion:'1.0',productCode:'motor-trade-road-risks',risk:{business:{activities:[{code:ref('mtOccupations',5)}]}},cover:{responses:{questionSetVersion:questions.version,answers:[answer('MTS-05-Q01',ref('coverLevels',1)),answer('MTS-05-Q02',ref('indemnityOwnVehicles',5)),answer('MTS-05-Q04',ref('indemnityOwnVehicles/number:5/excesses',4)),answer('MTS-05-Q05',false),answer('MTS-05-Q06',ref('excessLates',1)),answer('MTS-05-Q07',false),answer('MTS-05-Q08',false)]}}});
const check=p=>validateQuoteCoverReadiness(p,questions,references);
const set=(p,id,value)=>{const entry=p.cover.responses.answers.find(a=>a.questionId===id);if(entry)entry.value=value;else p.cover.responses.answers.push(answer(id,value));};

test('cover source and prototype facts compose with explicit defaults for both products',()=>{
 for(const productCode of questions.products) {
  const p=fixture();p.productCode=productCode;let result=check(p);assert.deepEqual(result.issues,[]);assert.deepEqual(validateQuoteReferences(p,references,result.selectedCollections),[]);
  p.cover.responses.answers=p.cover.responses.answers.filter(a=>!['MTS-05-Q01','MTS-05-Q02','MTS-05-Q04'].includes(a.questionId));
  for(const [id,value] of [['prototype.quote.2beaf3b8d546',1],['prototype.quote.d9dd069a314c',2],['prototype.quote.00216de47ab5',2]])set(p,id,ref(id,value));
  result=check(p);assert.deepEqual(result.issues,[]);assert.deepEqual(validateQuoteReferences(p,references,result.selectedCollections),[]);
 }
});

test('cover requiredness rejects omitted facts and booleans without requiring duplicate source declarations',()=>{
 for(const id of ['MTS-05-Q01','MTS-05-Q02','MTS-05-Q04','MTS-05-Q05','MTS-05-Q06','MTS-05-Q07','MTS-05-Q08']) {
  const p=fixture();p.cover.responses.answers=p.cover.responses.answers.filter(a=>a.questionId!==id);
  assert.ok(check(p).issues.some(issue=>['cover-level-required','cover-fact-required','cover-answer-required'].includes(issue.code)),id);
 }
 const p=fixture();set(p,'MTS-05-Q05',true);set(p,'MTS-05-Q06',ref('excessLates',2));
 assert.deepEqual(check(p).issues.map(issue=>issue.code),['unsupported-all-sections-excess','unsupported-late-notification-excess']);
});

test('third-party cover marks retained limit answers inactive and does not invent activity context',()=>{
 const p=fixture();set(p,'MTS-05-Q01',ref('coverLevels',3));const before=structuredClone(p);
 assert.ok(check(p).issues.some(issue=>issue.code==='inactive-cover-answer-retained'));assert.deepEqual(p,before);
 p.cover.responses.answers=p.cover.responses.answers.filter(a=>['MTS-05-Q01','MTS-05-Q08'].includes(a.questionId));delete p.risk.business;
 assert.deepEqual(check(p).issues,[]);
 set(p,'MTS-05-Q01',ref('coverLevels',1));assert.ok(check(p).issues.some(issue=>issue.code==='cover-activity-context-required'));
});

test('customer limits depend on pinned activities and fire-theft limit uses business amount',()=>{
 const p=fixture();p.risk.business.activities[0].code=ref('mtOccupations',1);
 assert.ok(check(p).issues.some(issue=>issue.code==='cover-fact-required'&&issue.fact==='customerVehicleLimit'));
 const customer=references.collections.indemnityCustomerVehicles.find(row=>row.numericValue===15000);
 set(p,'MTS-05-Q03',ref('indemnityCustomerVehicles',customer.value));set(p,'MTS-05-Q01',ref('coverLevels',2));assert.deepEqual(check(p).issues,[]);
 const high=references.collections.indemnityCustomerVehicles.find(row=>row.numericValue>15000);set(p,'MTS-05-Q03',ref('indemnityCustomerVehicles',high.value));
 assert.ok(check(p).issues.some(issue=>issue.code==='fire-theft-limit-exceeded'));
 p.risk.business.activities[0].code.label='Forged';assert.ok(check(p).issues.some(issue=>issue.code==='cover-activity-context-required'));
});

test('customer loan declaration controls cover level and captured vehicle eligibility',()=>{
 const p=fixture();p.risk.vehicles=[{id:'aaaaaaaa-0000-4000-8000-000000000001',customerLoan:true}];
 assert.equal(check(p).issues[0].code,'customer-loan-cover-required');set(p,'MTS-05-Q08',true);
 assert.ok(check(p).issues.some(issue=>issue.code==='cover-answer-required'&&issue.fact==='MTS-05-Q09'));
 set(p,'MTS-05-Q09',ref('customerLoanCoverLevels',1));assert.deepEqual(check(p).issues,[]);
 set(p,'MTS-05-Q01',ref('coverLevels',2));assert.ok(check(p).issues.some(issue=>issue.code==='customer-loan-cover-ineligible'));
 set(p,'MTS-05-Q09',ref('customerLoanCoverLevels',2));assert.deepEqual(check(p).issues,[]);
});
