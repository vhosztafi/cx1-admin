import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuoteDriverEligibility} from '../scripts/quote-driver-eligibility.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ref=(collection,value)=>({collection,value,label:references.collections[collection].find(row=>row.value===value).text,version:references.version});
const fixture=()=>({productCode:'motor-trade-road-risks',insured:{declaredCompanyType:ref('companyTypes',1)},termIntent:{localStartDate:'2026-01-01'},risk:{drivers:[{id:'aaaaaaaa-0000-4000-8000-000000000001',dateOfBirth:'1980-01-01',relationship:ref('driverRelationshipsPolicyHolder',2),usage:ref('driverUsages',1),responses:{answers:[{questionId:'MTS-06-Q31',kind:'boolean',value:false},{questionId:'MTS-06-Q32',kind:'boolean',value:false}]}}]}});
const set=(driver,n,value)=>{const id=`MTS-06-Q${n}`,a=driver.responses.answers.find(a=>a.questionId===id);if(a)a.value=value;else driver.responses.answers.push({questionId:id,value});};
const check=p=>validateQuoteDriverEligibility(p,questions,references);

test('driver relationships use company-specific pinned allowlists for both products',()=>{
 for(const productCode of questions.products)for(const company of references.collections.companyTypes)for(const relationship of references.collections.driverRelationshipsPolicyHolder) {
  const p=fixture();p.productCode=productCode;p.insured.declaredCompanyType=ref('companyTypes',company.value);p.risk.drivers[0].relationship=ref('driverRelationshipsPolicyHolder',relationship.value);
  assert.equal(check(p).some(issue=>issue.code==='driver-relationship-ineligible'),!company.relationshipOptions.includes(relationship.value));
 }
 const p=fixture();p.insured.declaredCompanyType.label='Forged';assert.equal(check(p)[0].code,'driver-relationship-context-required');
});

test('spouse age and policyholder uniqueness are per current proposal',()=>{
 const p=fixture(),d=p.risk.drivers[0];d.relationship=ref('driverRelationshipsPolicyHolder',5);d.dateOfBirth='2001-01-02';assert.equal(check(p)[0].code,'spouse-under-25');
 d.dateOfBirth='2001-01-01';assert.deepEqual(check(p),[]);d.relationship=ref('driverRelationshipsPolicyHolder',3);
 p.risk.drivers.push({...structuredClone(d),id:'aaaaaaaa-0000-4000-8000-000000000002'});assert.equal(check(p).filter(i=>i.code==='duplicate-policyholder-driver').length,2);
});

test('personal and other cover declarations preserve automatic source conditions and usage restrictions',()=>{
 const p=fixture(),d=p.risk.drivers[0];set(d,31,true);set(d,32,true);
 assert.deepEqual(check(p).map(i=>i.code),['personal-cover-motor-trade-only','other-cover-motor-trade-only']);
 d.usage=ref('driverUsages',2);assert.deepEqual(check(p),[]);d.dateOfBirth='2005-01-02';assert.equal(check(p)[0].code,'other-cover-under-21');
 d.dateOfBirth='1980-01-01';d.relationship=ref('driverRelationshipsPolicyHolder',3);set(d,31,false);set(d,32,false);
 assert.deepEqual(check(p).map(i=>i.code),['personal-cover-required-for-relationship','other-cover-required-for-relationship']);
 d.usage.label='Forged';assert.equal(check(p)[0].code,'driver-usage-context-required');
});

test('over-1000cc motorcycles require age30 and two complete licence years with trusted selections',()=>{
 const p=fixture(),d=p.risk.drivers[0];set(d,25,ref('driverMotorcycleCovers',6));set(d,26,'2024-01-01');d.dateOfBirth='1996-01-01';assert.deepEqual(check(p),[]);
 d.dateOfBirth='1996-01-02';assert.equal(check(p)[0].code,'motorcycle-over-1000-ineligible');d.dateOfBirth='1996-01-01';set(d,26,'2024-01-02');assert.equal(check(p)[0].code,'motorcycle-over-1000-ineligible');
 set(d,26,'2027-01-01');assert.equal(check(p)[0].code,'motorcycle-eligibility-context-required');
});

test('active bans use calendar-month clamping and allow expiry on policy start without rewriting history',()=>{
 const p=fixture(),d=p.risk.drivers[0];d.convictions=[{occurredOn:'2024-01-31',disqualified:true,banMonths:1}];p.termIntent.localStartDate='2024-02-28';const before=structuredClone(p);
 assert.equal(check(p)[0].code,'driver-ban-active-at-policy-start');assert.deepEqual(p,before);
 p.termIntent.localStartDate='2024-02-29';assert.deepEqual(check(p),[]);
 d.convictions[0].occurredOn='2025-01-31';p.termIntent.localStartDate='2025-02-28';assert.deepEqual(check(p),[]);
 delete p.termIntent.localStartDate;assert.equal(check(p)[0].code,'ban-policy-start-required');
});
