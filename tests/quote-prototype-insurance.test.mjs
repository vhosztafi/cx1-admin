import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuotePrototypeInsurance} from '../scripts/quote-prototype-insurance.mjs';
import {createQuoteValidationPipeline} from '../scripts/quote-validation-pipeline.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const claim='prototype.quote.1bdc05ff8b3d',reason='prototype.no-claims.reason',origin='prototype.quote.c0650c760167';
const set=(p,id,value)=>{const answers=p.risk.previousInsurance.responses.answers,entry=answers.find(a=>a.questionId===id);if(entry)entry.value=value;else answers.push({questionId:id,kind:questions.mappings.find(m=>m.questionId===id).answerKind,value});};
const remove=(p,id)=>{p.risk.previousInsurance.responses.answers=p.risk.previousInsurance.responses.answers.filter(a=>a.questionId!==id);};
const check=p=>validateQuotePrototypeInsurance(p,questions);

test('conditional no-discount reason is versioned, required, nonblank and scoped to Road Risks',async()=>{
 const validate=await createQuoteValidationPipeline(),{proposal:p,context}=await read('examples/quote-capture-motor-trade-road-risks.json');
 assert.deepEqual(check(p),[]);remove(p,reason);assert.ok(check(p).some(i=>i.code==='conditional-answer-required'));
 set(p,reason,'   ');assert.ok(check(p).some(i=>i.code==='conditional-answer-required'));
 set(p,reason,'Fictional explanation.');assert.equal(validate(JSON.stringify(p),context).status,'section-checks-pass');
 const before=structuredClone(p);check(p);assert.deepEqual(p,before);
 p.risk.previousInsurance.responses.questionSetVersion='mt-capture-e410d61471e481ed';assert.equal(validate(JSON.stringify(p),context).status,'invalid-draft');
 p.risk.previousInsurance.responses.questionSetVersion=questions.version;p.productCode='motor-trade-combined';assert.equal(validate(JSON.stringify(p),context).status,'invalid-draft');assert.deepEqual(check(p),[]);
});

test('claimed discount needs origin, positive years, explicit basis and expiry without reusing no-claim explanations',async()=>{
 const {proposal:p}=await read('examples/quote-capture-motor-trade-road-risks.json'),insurance=p.risk.previousInsurance;
 set(p,claim,true);assert.ok(check(p).some(i=>i.code==='inactive-answer-retained'));remove(p,reason);delete insurance.expiresOn;
 assert.equal(check(p).filter(i=>i.code==='prototype-insurance-answer-required').length,4);
 insurance.noClaimsYears=0;insurance.noClaimsYearsBasis='exact';insurance.expiresOn='2026-09-15';
 const option=references.collections[origin][0];set(p,origin,{collection:origin,value:option.value,label:option.text,version:references.version});
 assert.ok(check(p).some(i=>i.code==='claimed-discount-years-must-be-positive'));insurance.noClaimsYears=5;insurance.noClaimsYearsBasis='at-least';assert.deepEqual(check(p),[]);
 set(p,claim,false);set(p,reason,'Not transferring the discount.');set(p,'prototype.quote.61a13bb828b2',true);
 for(const code of ['discount-protection-without-claim','inactive-discount-origin-retained','inactive-claimed-discount-years'])assert.ok(check(p).some(i=>i.code===code));
});

test('prototype insurer and declaration omissions fail while Combined follows its own capture stage',async()=>{
 const {proposal:p}=await read('examples/quote-capture-motor-trade-road-risks.json');
 p.risk.previousInsurance={};assert.equal(check(p).filter(i=>i.code==='prototype-insurance-answer-required').length,4);
 p.risk.previousInsurance.insurer='   ';assert.ok(check(p).some(i=>i.path==='/risk/previousInsurance/insurer'));
 p.productCode='motor-trade-combined';assert.deepEqual(check(p),[]);
});
