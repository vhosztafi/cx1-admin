import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuoteAdditionalReadiness} from '../scripts/quote-additional-readiness.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ref=(collection,value=references.collections[collection][0].value)=>({collection,value,label:references.collections[collection].find(row=>row.value===value).text,version:references.version});
const answer=(questionId,value)=>({questionId,kind:typeof value==='boolean'?'boolean':'reference',value});
const fixture=()=>({productCode:'motor-trade-road-risks',risk:{business:{activities:[{code:ref('mtOccupations',5)}]},responses:{answers:[answer('MTS-06-Q01',ref('driverPlans',1))]},drivers:[{usage:ref('driverUsages',1)}]},cover:{responses:{answers:[answer('MTS-05-Q01',ref('coverLevels',3))]}}});
const check=p=>validateQuoteAdditionalReadiness(p,questions,references);

test('jockey radius is required for every pinned jockey activity and inactive for other activities',()=>{
 for(const productCode of questions.products)for(const activity of references.collections.mtOccupations) {
  const p=fixture();p.productCode=productCode;p.risk.business.activities[0].code=ref('mtOccupations',activity.value);
  assert.equal(check(p).some(i=>i.code==='car-jockey-radius-required'),activity.requireCarJockeyRadius===true);
 }
 const p=fixture();p.risk.responses.answers.push(answer('MTS-13-Q02',ref('carJockeyRadii')));assert.equal(check(p)[0].code,'inactive-car-jockey-radius-retained');
 p.risk.business.activities[0].code=ref('mtOccupations',9);assert.deepEqual(check(p),[]);
 p.risk.business.activities[0].code.label='Forged';assert.equal(check(p)[0].code,'additional-activity-context-required');
});

test('shunter optional false is preserved only with eligible activity, driver usage and cover',()=>{
 const p=fixture();p.risk.business.activities[0].code=ref('mtOccupations',26);p.risk.responses.answers.push(answer('MTS-13-Q03',false));const before=structuredClone(p);
 assert.deepEqual(check(p),[]);assert.deepEqual(p,before);
 p.risk.drivers[0].usage=ref('driverUsages',2);assert.equal(check(p)[0].code,'inactive-shunter-radius-retained');
 p.risk.responses.answers[0].value=ref('driverPlans',3);assert.deepEqual(check(p),[]);
 p.risk.business.activities.push({code:ref('mtOccupations',5)});assert.equal(check(p)[0].code,'inactive-shunter-radius-retained');
});

test('shunter dependencies cannot default missing driver context to eligible or omit monetary cover meaning',()=>{
 const p=fixture();p.risk.business.activities[0].code=ref('mtOccupations',26);p.risk.responses.answers.push(answer('MTS-13-Q03',true));p.risk.drivers=[];
 assert.equal(check(p)[0].code,'shunter-eligibility-context-required');
 p.risk.responses.answers[0].value=ref('driverPlans',3);p.cover.responses.answers[0].value=ref('coverLevels',1);
 assert.equal(check(p)[0].code,'shunter-eligibility-context-required');
 p.cover.responses.answers.push(answer('MTS-05-Q02',ref('indemnityOwnVehicles',1)));assert.deepEqual(check(p),[]);
 p.cover.responses.answers[1].value=ref('indemnityOwnVehicles',2);assert.equal(check(p)[0].code,'inactive-shunter-radius-retained');
});

test('additional material facts follow the source thousand-character bound without rewriting text',()=>{
 const p=fixture();p.risk.materialFacts='x'.repeat(1000);assert.deepEqual(check(p),[]);
 p.risk.materialFacts+='x';const before=structuredClone(p);assert.equal(check(p)[0].code,'material-facts-too-long');assert.deepEqual(p,before);
});
