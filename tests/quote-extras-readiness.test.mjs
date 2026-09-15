import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuoteExtrasReadiness} from '../scripts/quote-extras-readiness.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const id='aaaaaaaa-0000-4000-8000-000000000001';
const ref=(collection,value=references.collections[collection][0].value)=>({collection,value,label:references.collections[collection].find(row=>row.value===value).text,version:references.version});
const answer=(questionId,value)=>({questionId,kind:typeof value==='boolean'?'boolean':'reference',value});
const fixture=()=>({schemaVersion:'1.0',productCode:'motor-trade-road-risks',termIntent:{kind:'short-period',localStartDate:'2026-01-01',localStartTime:'00:00',localEndDate:'2026-02-01',localEndTime:'00:00',timeZone:'Europe/London'},risk:{responses:{answers:[answer('MTS-06-Q01',ref('driverPlans',1))]},drivers:[{id,usage:ref('driverUsages',references.collections.driverUsages.find(row=>row.isSocialDomesticPleasure===true).value)}]},cover:{responses:{answers:[answer('MTS-05-Q01',ref('coverLevels',1)),answer('MTS-11-Q07',false),answer('MTS-11-Q08',false),answer('MTS-11-Q09',ref('thirdPartyDamageLimits'))]}}});
const set=(p,n,value)=>p.cover.responses.answers.push(answer(`MTS-11-Q${String(n).padStart(2,'0')}`,value));
const check=p=>validateQuoteExtrasReadiness(p,questions,references);
const trip=()=>({id:'aaaaaaaa-0000-4000-8000-000000000002',registration:'DEMO 01',startsOn:'2026-01-01',endsOn:'2026-01-31',area:ref('europeanArea'),driverIds:[id],cover:ref('europeanTripCover'),usage:ref('europeanTripUsage',2)});

test('extras independent fields and conditional windscreen declarations preserve false',()=>{
 for(const product of questions.products){const p=fixture();p.productCode=product;assert.deepEqual(check(p),[]);}
 const p=fixture();p.cover.responses.answers.splice(1,1);assert.equal(check(p)[0].code,'extras-answer-required');
 set(p,7,false);set(p,4,true);assert.equal(check(p).filter(issue=>issue.code==='extras-answer-required').length,2);
 set(p,5,ref('windScreenCovers'));set(p,6,false);assert.deepEqual(check(p),[]);
 p.cover.responses.answers[0].value=ref('coverLevels',3);assert.equal(check(p)[0].code,'extras-comprehensive-cover-required');
});

test('temporary European cover requires all fields and actual short-period bounds',()=>{
 const p=fixture();set(p,13,true);assert.equal(check(p)[0].code,'european-cover-row-required');p.cover.temporaryEuropeanCover=[trip()];assert.deepEqual(check(p),[]);
 for(const field of ['registration','startsOn','endsOn','area','cover','usage']){const copy=structuredClone(p);delete copy.cover.temporaryEuropeanCover[0][field];assert.ok(check(copy).some(issue=>issue.code==='european-trip-field-required'&&issue.path.endsWith('/'+field)),field);}
 const row=p.cover.temporaryEuropeanCover[0];row.endsOn='2026-02-02';assert.equal(check(p)[0].code,'european-trip-after-policy');
 row.startsOn='2025-12-31';assert.ok(check(p).some(issue=>issue.code==='european-trip-before-policy'));
 row.startsOn='2026-01-01';row.endsOn=row.startsOn;assert.equal(check(p)[0].code,'european-trip-end-must-follow-start');
 delete p.termIntent.localEndDate;assert.ok(check(p).some(issue=>issue.code==='european-trip-term-required'));
});

test('European driver requirements use trusted plan, stable driver IDs and declared usage',()=>{
 const p=fixture();set(p,13,true);const row=trip();p.cover.temporaryEuropeanCover=[row];row.driverIds=[];assert.equal(check(p)[0].code,'european-trip-drivers-required');
 row.driverIds=[id.toUpperCase()];assert.deepEqual(check(p),[]);
 p.risk.drivers[0].usage=ref('driverUsages',references.collections.driverUsages.find(row=>row.isSocialDomesticPleasure===false).value);assert.equal(check(p)[0].code,'european-trip-usage-ineligible');
 p.risk.responses.answers[0].value=ref('driverPlans',3);row.driverIds=[];assert.equal(check(p)[0].code,'european-trip-usage-ineligible');
 row.usage=ref('europeanTripUsage',1);assert.deepEqual(check(p),[]);
 p.risk.responses.answers[0].value.label='Forged';assert.equal(check(p)[0].code,'european-driver-plan-required');
});

test('inactive annual rows and demonstration details are retained with explicit issues',()=>{
 const p=fixture();set(p,10,false);p.cover.annualEuropeanCover=[{registration:'DEMO 01',usage:ref('europeanTripUsage',1)}];const before=structuredClone(p);
 assert.equal(check(p)[0].code,'inactive-european-cover-retained');assert.deepEqual(p,before);
 delete p.cover.annualEuropeanCover;set(p,1,true);assert.equal(check(p)[0].code,'extras-answer-required');set(p,2,ref('demonstrationCovers'));
 p.risk.drivers[0].responses={answers:[{questionId:'MTS-06-Q26',kind:'date',value:'2020-01-01'}]};assert.equal(check(p)[0].code,'extras-answer-required');set(p,3,false);assert.deepEqual(check(p),[]);
});
