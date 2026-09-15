import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {reconcileQuoteInsuranceDeclarations} from '../scripts/quote-insurance-reconciliation.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ref=(collection,value)=>({collection,value,label:references.collections[collection].find(row=>row.value===value).text,version:references.version});
const fixture=()=>({productCode:'motor-trade-road-risks',risk:{previousInsurance:{responses:{answers:[]}}}});
const set=(p,id,value)=>{const answers=p.risk.previousInsurance.responses.answers,entry=answers.find(a=>a.questionId===id);if(entry)entry.value=value;else answers.push({questionId:id,value});};
const check=p=>reconcileQuoteInsuranceDeclarations(p,questions,references);

test('NCB origins reconcile reordered prototype/source ordinals by business meaning',()=>{
 for(const productCode of questions.products)for(const [prototype,source] of [[1,3],[2,1],[3,2]]) {
  const p=fixture();p.productCode=productCode;set(p,'prototype.quote.c0650c760167',ref('prototype.quote.c0650c760167',prototype));set(p,'MTS-05-Q11',ref('noClaimBonusesEarned',source));assert.deepEqual(check(p),[]);
  set(p,'MTS-05-Q11',ref('noClaimBonusesEarned',4));assert.equal(check(p).filter(i=>i.code==='conflicting-ncb-origin').length,2);
 }
});

test('protection conflicts do not conflate different introductory or discount-claim questions',()=>{
 const p=fixture();set(p,'prototype.quote.61a13bb828b2',false);set(p,'MTS-05-Q17',true);assert.equal(check(p).length,2);
 set(p,'MTS-05-Q17',false);set(p,'prototype.quote.3fad63dd9abf',true);set(p,'MTS-05-Q14',false);set(p,'prototype.quote.1bdc05ff8b3d',false);assert.deepEqual(check(p),[]);
});

test('exact and at-least year declarations preserve capped NCB meaning without truncation',()=>{
 const p=fixture(),insurance=p.risk.previousInsurance;insurance.noClaimsYears=16;insurance.noClaimsYearsBasis='exact';set(p,'MTS-05-Q10',ref('noClaimBonuses',16));const before=structuredClone(p);assert.deepEqual(check(p),[]);assert.deepEqual(p,before);
 insurance.noClaimsYears=14;assert.equal(check(p)[0].code,'conflicting-ncb-years');insurance.noClaimsYearsBasis='at-least';assert.deepEqual(check(p),[]);
 set(p,'MTS-05-Q10',ref('noClaimBonuses',11));assert.equal(check(p)[0].code,'conflicting-ncb-years');insurance.noClaimsYears=5;assert.deepEqual(check(p),[]);
 insurance.noClaimsYearsBasis='exact';assert.equal(check(p)[0].code,'conflicting-ncb-years');delete insurance.noClaimsYearsBasis;assert.equal(check(p)[0].code,'ncb-years-context-required');
});
