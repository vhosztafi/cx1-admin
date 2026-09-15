import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {reconcileQuoteCoverDeclarations} from '../scripts/quote-cover-declarations.mjs';
import {createQuoteValidationPipeline} from '../scripts/quote-validation-pipeline.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ref=(collection,value)=>({collection,value,label:references.collections[collection].find(row=>row.value===value).text,version:references.version});
const set=(responses,id,value)=>{const old=responses.answers.find(a=>a.questionId===id);if(old)old.value=value;else responses.answers.push({questionId:id,kind:questions.mappings.find(m=>m.questionId===id).answerKind,value});};
const check=p=>reconcileQuoteCoverDeclarations(p,questions,references);
const fixture=async()=> (await read('examples/quote-capture-motor-trade-road-risks.json')).proposal;

test('demonstration selections reconcile booleans and require source context for positive cover',async()=>{
 for(const productCode of questions.products) {
  const p=await fixture();p.productCode=productCode;assert.deepEqual(check(p),[]);
  set(p.cover.responses,'prototype.quote.becc2653d0de',true);assert.ok(check(p).some(i=>i.code==='demonstration-cover-context-required'));
  set(p.cover.responses,'MTS-11-Q01',false);assert.equal(check(p).filter(i=>i.code==='conflicting-demonstration-cover').length,2);
  set(p.cover.responses,'MTS-11-Q01',true);const before=structuredClone(p);assert.deepEqual(check(p),[]);assert.deepEqual(p,before);
 }
});

test('courtesy choices distinguish who insures the loan without collapsing captured arrangements',async()=>{
 const p=await fixture(),id='prototype.quote.af67cb40b4de';
 for(const [option,loan] of [[1,false],[2,true],[3,false]]) {
  set(p.risk.business.responses,id,ref(id,option));set(p.cover.responses,'MTS-05-Q08',loan);assert.deepEqual(check(p),[]);
  set(p.cover.responses,'MTS-05-Q08',!loan);assert.equal(check(p).filter(i=>i.code==='conflicting-courtesy-cover').length,2);
 }
 set(p.risk.business.responses,id,{...ref(id,2),label:'Forged'});assert.ok(!check(p).some(i=>i.code==='conflicting-courtesy-cover')); // Reference stage rejects it; no ordinal inference here.
});

test('private-use consistency compares named drivers without inventing unnamed driver permissions',async()=>{
 const p=await fixture(),id='prototype.quote.4c5df77ffba1';
 set(p.cover.responses,id,true);assert.ok(check(p).some(i=>i.code==='private-use-driver-required'));
 p.risk.drivers[0].usage=ref('driverUsages',2);assert.deepEqual(check(p),[]);
 set(p.cover.responses,id,false);assert.equal(check(p).filter(i=>i.code==='conflicting-private-use').length,2);
 set(p.cover.responses,id,true);p.risk.drivers[0].usage=ref('driverUsages',1);
 for(const plan of [2,3]){set(p.risk.responses,'MTS-06-Q01',ref('driverPlans',plan));assert.deepEqual(check(p),[]);}
});

test('composed pipeline catches missing prototype cover choices and demonstration drift',async()=>{
 const {proposal:p,context}=await read('examples/quote-capture-motor-trade-combined.json'),validate=await createQuoteValidationPipeline();
 set(p.cover.responses,'MTS-11-Q01',true);assert.ok(validate(JSON.stringify(p),context).issues.some(i=>i.stage==='cover-declarations'&&i.code==='conflicting-demonstration-cover'));
 p.cover.responses.answers=p.cover.responses.answers.filter(a=>!a.questionId.startsWith('prototype.'));
 p.risk.business.responses.answers=p.risk.business.responses.answers.filter(a=>a.questionId!=='prototype.quote.af67cb40b4de');
 assert.equal(validate(JSON.stringify(p),context).issues.filter(i=>i.stage==='cover-declarations'&&i.code==='required-prototype-cover-answer').length,3);
});
