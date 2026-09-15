import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuoteActivityReadiness} from '../scripts/quote-additional-readiness.mjs';
import {validateQuoteTextBounds} from '../scripts/quote-source-rules.mjs';
import {validateQuoteBusinessReadiness} from '../scripts/quote-business-readiness.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ref=(collection,value)=>({collection,value,label:references.collections[collection].find(row=>row.value===value).text,version:references.version});

test('activity readiness rejects empty/missing selections, under-one-percent shares and mixed jockey activities',()=>{
 for(const productCode of questions.products) {
  const p={productCode},check=()=>validateQuoteActivityReadiness(p,questions,references);
  assert.equal(check()[0].code,'business-activity-required');p.risk={business:{activities:[{}]}};
  assert.deepEqual(check().map(i=>i.code),['business-activity-code-required','business-activity-minimum-one-percent']);
  p.risk.business.activities=[{code:ref('mtOccupations',9),turnoverBasisPoints:10000}];assert.deepEqual(check(),[]);
  p.risk.business.activities.push({code:ref('mtOccupations',5),turnoverBasisPoints:99});assert.deepEqual(check().map(i=>i.code),['car-jockey-must-be-only-activity','business-activity-minimum-one-percent']);
 }
});

test('source address limits apply to every repeated instance and optional address field without trimming',()=>{
 for(const field of ['postcode','houseNumber','street','town','city','county']) {
  const max=field==='postcode'?10:50;
  const address={[field]:'x'.repeat(max)};
  const p={insured:{address},risk:{premises:[{address:structuredClone(address)}],drivers:[{address:structuredClone(address)}]}};
  assert.deepEqual(validateQuoteTextBounds(p),[]);
  for(const item of [p.insured,...p.risk.premises,...p.risk.drivers])item.address[field]+='x';
  const before=structuredClone(p);assert.equal(validateQuoteTextBounds(p).length,3);assert.deepEqual(p,before);
 }
});

test('source VAT, disability explanation and conditional company-name text bounds are enforced',()=>{
 const p={productCode:'motor-trade-road-risks',insured:{declaredCompanyType:ref('companyTypes',3),legalName:'x'.repeat(51)},risk:{business:{responses:{answers:[{questionId:'MTS-03-Q07',kind:'text',value:'x'.repeat(21)}]}},drivers:[{responses:{answers:[{questionId:'MTS-06-Q47',kind:'text',value:'x'.repeat(51)}]}}]}};
 assert.equal(validateQuoteTextBounds(p).length,2);assert.ok(validateQuoteBusinessReadiness(p,questions,references).some(i=>i.code==='company-name-too-long'));
 p.insured.declaredCompanyType=ref('companyTypes',1);assert.ok(!validateQuoteBusinessReadiness(p,questions,references).some(i=>i.code==='company-name-too-long'));
});
