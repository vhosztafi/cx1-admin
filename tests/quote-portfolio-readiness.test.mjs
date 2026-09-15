import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuotePortfolioReadiness} from '../scripts/quote-portfolio-readiness.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ref=(collection,value)=>({collection,value,label:references.collections[collection].find(row=>row.value===value).text,version:references.version});
const fixture=()=>({productCode:'motor-trade-road-risks',risk:{responses:{answers:[{questionId:'MTS-10-Q01',kind:'boolean',value:true},{questionId:'MTS-10-Q02',kind:'percentage',value:10000}]}}});
const set=(p,id,value)=>{const a=p.risk.responses.answers.find(a=>a.questionId===id);if(a)a.value=value;else p.risk.responses.answers.push({questionId:id,value});};
const check=p=>validateQuotePortfolioReadiness(p,questions,references);

test('portfolio percentages and transporter/detail bounds preserve exact basis-point totals',()=>{
 for(const productCode of questions.products){const p=fixture();p.productCode=productCode;assert.deepEqual(check(p),[]);}
 const p=fixture();set(p,'MTS-10-Q02',9901);set(p,'MTS-10-Q19',true);set(p,'MTS-10-Q20',99);set(p,'MTS-10-Q21',2);
 assert.deepEqual(check(p).map(i=>i.code),['portfolio-minimum-one-percent','transporter-minimum-three-vehicles']);
 set(p,'MTS-10-Q02',9900);set(p,'MTS-10-Q20',100);set(p,'MTS-10-Q21',3);assert.deepEqual(check(p),[]);
 set(p,'MTS-10-Q19',false);assert.ok(check(p).some(i=>i.code==='inactive-answer-retained'));
});

test('sports and motorcycle selections require real driver eligibility rather than mere positive percentages',()=>{
 const p=fixture();set(p,'MTS-10-Q01',false);p.risk.responses.answers=p.risk.responses.answers.filter(a=>a.questionId!=='MTS-10-Q02');set(p,'MTS-10-Q03',true);set(p,'MTS-10-Q04',10000);
 assert.equal(check(p)[0].code,'sports-portfolio-context-required');set(p,'MTS-06-Q01',ref('driverPlans',3));set(p,'MTS-06-Q05',ref('aadMaxVehicleGrouping',1));assert.equal(check(p)[0].code,'sports-portfolio-ineligible');set(p,'MTS-06-Q05',ref('aadMaxVehicleGrouping',2));assert.deepEqual(check(p),[]);
 set(p,'MTS-10-Q04',5000);set(p,'MTS-10-Q07',true);set(p,'MTS-10-Q08',5000);assert.equal(check(p)[0].code,'motorcycle-portfolio-context-required');set(p,'MTS-06-Q07',ref('aadMaxMotorcycleCc',1));assert.equal(check(p)[0].code,'motorcycle-portfolio-ineligible');set(p,'MTS-06-Q07',ref('aadMaxMotorcycleCc',2));assert.deepEqual(check(p),[]);
});

test('captured vehicle facts cannot silently contradict portfolio declarations',()=>{
 const p=fixture();const row=references.collections.vehicleType.find(r=>r.category==='Commercial');p.risk.vehicles=[{id:'aaaaaaaa-0000-4000-8000-000000000001',vehicleType:ref('vehicleType',row.value),grossWeightKg:3501,imported:true,seats:8,modifications:[{id:'aaaaaaaa-0000-4000-8000-000000000002'}]}];const before=structuredClone(p);
 const ids=check(p).filter(i=>i.code==='portfolio-declaration-conflicts-with-vehicle').map(i=>i.questionId);
 for(const id of ['MTS-10-Q05','MTS-10-Q11','MTS-10-Q13','MTS-10-Q26'])assert.ok(ids.includes(id));assert.deepEqual(p,before);
 p.risk.specifiedVehicleIds=[p.risk.vehicles[0].id.toUpperCase()];assert.ok(!check(p).some(i=>i.code==='portfolio-declaration-conflicts-with-vehicle'&&i.questionId==='MTS-10-Q13'));
});
