import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import {validateQuoteDriverReadiness,requiredDriverQuestions,driverHistoryGroups} from '../scripts/quote-driver-readiness.mjs';
import {validateQuoteQuestions,validateQuoteReferences,quoteMappingsForProduct} from '../scripts/quote-semantic-contract.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);
const draft=ajv.compile(await read('schemas/quote-draft.schema.json'));
const q=n=>`MTS-06-Q${String(n).padStart(2,'0')}`;
const mapping=n=>questions.mappings.find(row=>row.owner===q(n));
const ref=(collection,value=references.collections[collection][0].value)=>({collection,value,label:references.collections[collection].find(row=>row.value===value).text,version:references.version});
const setPath=(item,path,value)=>{const keys=path.split('.');const leaf=keys.pop();for(const key of keys)item=item[key]??=( {} );item[leaf]=value;};
const set=(driver,n,value)=>{
 const row=mapping(n);
 if(row.contractKind==='Answer') {
  driver.responses??={questionSetVersion:questions.version,answers:[]};
  const answers=driver.responses.answers;const entry=answers.find(answer=>answer.questionId===q(n));
  if(entry)entry.value=value;else answers.push({questionId:q(n),kind:row.answerKind,value});
 } else setPath(driver,row.canonicalPath.replace('risk.drivers[].',''),value);
};
const fixture=()=>{
 const driver={id:'aaaaaaaa-0000-4000-8000-000000000001'};
 for(const n of requiredDriverQuestions) {
  const row=mapping(n);
  set(driver,n,row.optionCollection?ref(row.optionCollection):row.answerKind==='boolean'?false:row.contractKind==='Date'?'1980-01-01':'Example');
 }
 driver.address.postcode='AB1 2CD';driver.licence.issuedOn='2000-01-01';set(driver,21,true);set(driver,25,ref('driverMotorcycleCovers',1));set(driver,28,ref('driverTradeEmploymentBasises',1));
 return {schemaVersion:'1.0',productCode:'motor-trade-road-risks',termIntent:{localStartDate:'2026-01-01'},risk:{drivers:[driver]}};
};
const check=proposal=>validateQuoteDriverReadiness(proposal,questions,references);

test('driver section accepts explicit negative answers and checks each independent field',()=>{
 for(const product of questions.products) {
  const proposal=fixture();proposal.productCode=product;
  assert.equal(draft(proposal),true,JSON.stringify(draft.errors));
  assert.deepEqual(check(proposal),[]);
  assert.deepEqual(validateQuoteQuestions(proposal,quoteMappingsForProduct(questions,product),questions.version),[]);
  assert.deepEqual(validateQuoteReferences(proposal,references),[]);
 }
 for(const n of requiredDriverQuestions) {
  const proposal=fixture(),driver=proposal.risk.drivers[0],row=mapping(n);
  if(row.contractKind==='Answer')driver.responses.answers=driver.responses.answers.filter(answer=>answer.questionId!==q(n));
  else {const keys=row.canonicalPath.replace('risk.drivers[].','').split('.');const leaf=keys.pop();delete keys.reduce((item,key)=>item[key],driver)[leaf];}
  assert.ok(check(proposal).some(issue=>issue.code==='required-driver-field'&&issue.questionId===q(n)),q(n));
 }
});

test('residency, motorcycle date and disability details use their actual controlling answers',()=>{
 const proposal=fixture(),driver=proposal.risk.drivers[0];set(driver,21,false);set(driver,25,ref('driverMotorcycleCovers',2));set(driver,46,true);
 assert.deepEqual(check(proposal).map(issue=>issue.questionId),[q(22),q(26),q(47)]);
 set(driver,22,'2001-01-01');set(driver,26,'1999-12-31');set(driver,47,'Fictional details');
 assert.equal(check(proposal)[0].code,'motorcycle-licence-before-driving-licence');
 set(driver,26,'2000-01-01');assert.deepEqual(check(proposal),[]);
});

test('source age and young licence anniversary boundaries are explicit',()=>{
 const proposal=fixture(),driver=proposal.risk.drivers[0];
 for(const [birth,expected] of [['1941-01-01',false],['1940-01-01',true],['2009-01-01',false],['2009-01-02',true]]) {
  driver.dateOfBirth=birth;assert.equal(check(proposal).some(issue=>issue.code==='driver-age-out-of-range'),expected,birth);
 }
 driver.dateOfBirth='2005-01-01';driver.licence.issuedOn='2025-01-02';assert.ok(check(proposal).some(issue=>issue.code==='young-driver-licence-experience'));
 driver.licence.issuedOn='2025-01-01';assert.deepEqual(check(proposal),[]);
 driver.licence.type=ref('driverLicenceTypes',3);assert.ok(check(proposal).some(issue=>issue.code==='provisional-licence-not-covered'));
});

test('each active history requires a row and every source field while preserving false and zero',()=>{
 for(const group of driverHistoryGroups) {
  const proposal=fixture(),driver=proposal.risk.drivers[0];set(driver,group.parent,group.collection?ref(group.collection,group.active):group.active);
  assert.equal(check(proposal)[0].code,'driver-history-required');
  const item={id:'aaaaaaaa-0000-4000-8000-000000000002'};driver[group.key]=[item];
  for(const n of group.fields) {
   const row=mapping(n),path=row.canonicalPath.split(`${group.key}[].`)[1];
   setPath(item,path,row.optionCollection?ref(row.optionCollection):row.contractKind==='boolean'?false:row.contractKind==='Date'?'2020-01-01':row.contractKind==='Amount'?'0.00':row.contractKind==='integer'||row.contractKind==='number'?0:'Example');
  }
  assert.deepEqual(check(proposal),[],group.key);
  assert.equal(draft(proposal),true,JSON.stringify(draft.errors));
  for(const n of group.fields) {
   const copy=structuredClone(proposal);const field=mapping(n).canonicalPath.split(`${group.key}[].`)[1];delete copy.risk.drivers[0][group.key][0][field];
   assert.ok(check(copy).some(issue=>issue.code==='required-driver-history-field'&&issue.questionId===q(n)),q(n));
  }
  set(driver,group.parent,group.collection?ref(group.collection,1):false);const before=structuredClone(proposal);
  assert.equal(check(proposal)[0].code,'inactive-driver-history-retained');assert.deepEqual(proposal,before);
 }
});

test('disqualification requires ban length and occupation identities are unique per driver',()=>{
 const proposal=fixture(),driver=proposal.risk.drivers[0];set(driver,33,true);driver.convictions=[{occurredOn:'2020-01-01',code:ref('driverMTConvictionCodes'),fine:'0.00',points:0,disqualified:true}];
 assert.equal(check(proposal)[0].code,'ban-length-required');driver.convictions[0].banMonths=0;assert.deepEqual(check(proposal),[]);
 set(driver,28,ref('driverTradeEmploymentBasises',2));driver.occupations=[{occupation:ref('occupations'),businessUseRequired:false},{occupation:ref('occupations'),businessUseRequired:false}];
 assert.equal(check(proposal)[0].code,'duplicate-driver-occupation');
});
