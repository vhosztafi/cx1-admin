import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {reconcileQuoteDriverDeclarations} from '../scripts/quote-driver-reconciliation.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ref=(collection,value)=>({collection,value,label:references.collections[collection].find(row=>row.value===value).text,version:references.version});
const fixture=()=>({productCode:'motor-trade-road-risks',termIntent:{localStartDate:'2026-01-01'},risk:{drivers:[{firstName:'Alex Jane',surname:'Example',fullName:'Alex Jane Example',dateOfBirth:'2000-01-01',licence:{type:ref('driverLicenceTypes',2),issuedOn:'2020-01-02'},responses:{answers:[]}}]}});
const set=(p,id,value)=>{const answers=p.risk.drivers[0].responses.answers,entry=answers.find(a=>a.questionId===id);if(entry)entry.value=value;else answers.push({questionId:id,value});};
const check=p=>reconcileQuoteDriverDeclarations(p,questions,references);

test('compound names compare without splitting names or rewriting whitespace',()=>{
 for(const productCode of questions.products){const p=fixture();p.productCode=productCode;p.risk.drivers[0].fullName=' alex   jane EXAMPLE ';const before=structuredClone(p);assert.deepEqual(check(p),[]);assert.deepEqual(p,before);p.risk.drivers[0].surname='Different';assert.equal(check(p)[0].code,'conflicting-driver-name');}
});

test('employment overlap validates both pinned declarations and conditional prototype detail',()=>{
 const p=fixture();set(p,'MTS-06-Q28',ref('driverTradeEmploymentBasises',1));set(p,'prototype.adddriver.trade-employment',ref('prototype.adddriver.trade-employment',2));
 assert.equal(check(p).filter(i=>i.code==='conflicting-driver-employment').length,2);assert.ok(check(p).some(i=>i.code==='prototype-other-occupation-required'));
 set(p,'MTS-06-Q28',ref('driverTradeEmploymentBasises',2));set(p,'prototype.adddriver.other-occupation','Example occupation');assert.deepEqual(check(p),[]);
 set(p,'prototype.adddriver.trade-employment',ref('prototype.adddriver.trade-employment',1));set(p,'MTS-06-Q28',ref('driverTradeEmploymentBasises',1));assert.equal(check(p)[0].code,'inactive-prototype-other-occupation');
});

test('declared licence and residency years compare complete anniversaries without inventing dates',()=>{
 const p=fixture();set(p,'MTS-06-Q21',true);set(p,'prototype.adddriver.residency-years',26);set(p,'prototype.adddriver.licence-years',5);assert.deepEqual(check(p),[]);
 set(p,'prototype.adddriver.licence-years',6);assert.equal(check(p)[0].code,'conflicting-driver-years');p.termIntent.localStartDate='2026-01-02';assert.deepEqual(check(p),[]);
 set(p,'MTS-06-Q21',false);assert.equal(check(p)[0].code,'driver-years-context-required');set(p,'MTS-06-Q22','2000-01-01');assert.deepEqual(check(p),[]);
 p.risk.drivers[0].licence.type=ref('driverLicenceTypes',4);assert.equal(check(p)[0].code,'driver-years-context-required');
});
