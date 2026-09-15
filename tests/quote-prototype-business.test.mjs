import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuotePrototypeBusiness,prototypeBusinessGroups} from '../scripts/quote-prototype-business.mjs';
import {createQuoteValidationPipeline} from '../scripts/quote-validation-pipeline.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ref=(id,value)=>({collection:id,value,label:references.collections[id].find(row=>row.value===value).text,version:references.version});
const set=(p,id,value)=>{const answers=p.risk.business.responses.answers,index=answers.findIndex(a=>a.questionId===id);const answer={questionId:id,kind:questions.mappings.find(m=>m.questionId===id).answerKind,value};if(index<0)answers.push(answer);else answers[index]=answer;};
const check=p=>validateQuotePrototypeBusiness(p,questions,references);
const trader='prototype.quote.c6181a11c34c',employment='prototype.quote.34613c23e95d',occupation='prototype.quote-value.3fd9edd7e66e';

test('part-time traders need occupation and applicable employment for both Motor Trade products',async()=>{
 for(const product of questions.products) {
  const {proposal:p}=await read(`examples/quote-capture-${product}.json`);
  assert.deepEqual(check(p),[]);set(p,trader,ref(trader,2));
  assert.ok(check(p).some(i=>i.questionId===occupation));assert.ok(check(p).some(i=>i.code==='part-time-employment-required'));
  set(p,occupation,'  ');assert.ok(check(p).some(i=>i.questionId===occupation));
  set(p,occupation,'Bookkeeper');set(p,employment,ref(employment,2));assert.deepEqual(check(p),[]);
  const before=structuredClone(p);check(p);assert.deepEqual(p,before);
  set(p,trader,ref(trader,1));assert.deepEqual(check(p).map(i=>i.code),['inactive-main-occupation-retained','inactive-main-employment-retained']);
  set(p,trader,{...ref(trader,1),label:'Forged'});assert.ok(check(p).some(i=>i.code==='main-occupation-context-required'));
 }
});

test('every appetite and vehicle-characteristic declaration requires details when positive',async()=>{
 const {proposal}=await read('examples/quote-capture-motor-trade-road-risks.json');
 for(const group of prototypeBusinessGroups)for(const parent of group.parents) {
  const p=structuredClone(proposal),id=`prototype.quote.${parent}`,details=`prototype.quote-value.${group.details}`;
  set(p,id,true);assert.ok(check(p).some(i=>i.questionId===details));
  set(p,details,'  ');assert.ok(check(p).some(i=>i.questionId===details));
  set(p,details,'Fictional declaration details');assert.deepEqual(check(p),[]);
  set(p,id,false);assert.ok(check(p).some(i=>i.code==='inactive-prototype-details-retained'));
  p.risk.business.responses.answers=p.risk.business.responses.answers.filter(a=>a.questionId!==id);
  assert.ok(check(p).some(i=>i.code==='prototype-details-context-required'));assert.ok(check(p).some(i=>i.questionId===id));
 }
});

test('composed validation detects absent prototype answers and conditional details',async()=>{
 const validate=await createQuoteValidationPipeline(),{proposal:p,context}=await read('examples/quote-capture-motor-trade-combined.json');
 set(p,'prototype.quote.5f9e8331ac6f',true);
 assert.ok(validate(JSON.stringify(p),context).issues.some(i=>i.stage==='prototype-business'&&i.questionId==='prototype.quote-value.2d662a3ec81d'));
 p.risk.business.responses.answers=p.risk.business.responses.answers.filter(a=>!a.questionId.startsWith('prototype.'));
 const result=validate(JSON.stringify(p),context);assert.equal(result.status,'incomplete');assert.equal(result.issues.filter(i=>i.stage==='prototype-business').length,14);
});
