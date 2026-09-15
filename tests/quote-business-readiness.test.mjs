import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import {validateQuoteBusinessReadiness,businessReadinessOwners} from '../scripts/quote-business-readiness.mjs';
import {quoteMappingsForProduct,validateQuoteQuestions,validateQuoteReferences} from '../scripts/quote-semantic-contract.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json');
const references=await read('reference-data/motor-trade-capture.json');
const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);
const draft=ajv.compile(await read('schemas/quote-draft.schema.json'));
const ref=(collection,value=references.collections[collection][0].value)=>({collection,value,label:references.collections[collection].find(row=>row.value===value).text,version:references.version});
const answers=items=>({questionSetVersion:questions.version,answers:items.map(([questionId,kind,value])=>({questionId,kind,value}))});
const check=proposal=>validateQuoteBusinessReadiness(proposal,questions,references);
const fixture=(productCode='motor-trade-road-risks')=>({
 schemaVersion:'1.0',productCode,
 insured:{declaredCompanyType:ref('companyTypes',1),title:ref(questions.mappings.find(row=>row.owner==='MTS-01-Q07').optionCollection),firstName:'Alex',surname:'Example',contact:{email:'alex@example.test',mobile:'07123456789'},address:{postcode:'AB1 2CD',houseNumber:'1',street:'Example Street',county:'Example County'}},
 risk:{business:{startedOn:'2020-01-01',turnover:'0.00',wageRoll:'0.00',responses:answers([
 ['MTS-02-Q01','reference',ref('tradingFroms',1)],['MTS-03-Q04','boolean',false],['MTS-03-Q06','boolean',false],['MTS-03-Q08','count',1],['MTS-03-Q09','count',0],
 ])},declarations:answers(businessReadinessOwners.declarations.map(id=>[id,'boolean',false]))},
});

test('both products accept complete business sections with explicit false and zero without claiming full readiness',()=>{
 for(const product of questions.products) {
  const proposal=fixture(product);const before=structuredClone(proposal);
  assert.equal(draft(proposal),true,JSON.stringify(draft.errors));
  assert.deepEqual(validateQuoteQuestions(proposal,quoteMappingsForProduct(questions,product),questions.version),[]);
  assert.deepEqual(validateQuoteReferences(proposal,references),[]);
  assert.deepEqual(check(proposal),[]);assert.deepEqual(proposal,before);
 }
});

test('each required scalar or answer is checked even when its container is omitted',()=>{
 for(const owner of [...businessReadinessOwners.proposer,...businessReadinessOwners.business,...businessReadinessOwners.declarations,'MTS-02-Q01']) {
  const proposal=fixture();const row=questions.mappings.find(row=>row.owner===owner);
  const keys=row.canonicalPath.replace('[]','').split('.');const leaf=keys.pop();
  const parent=keys.reduce((item,key)=>item[key],proposal);
  if(row.contractKind==='Answer')parent[leaf]=parent[leaf].filter(answer=>answer.questionId!==owner);
  else delete parent[leaf];
  assert.ok(check(proposal).some(issue=>issue.code==='required-capture-field'&&issue.questionId===owner),owner);
 }
 const empty={schemaVersion:'1.0',productCode:'motor-trade-road-risks'};
 assert.equal(draft(empty),true);assert.ok(check(empty).length>=28);
});

test('company name and contact alternatives follow source conditions with trusted company identity',()=>{
 const proposal=fixture();proposal.insured.declaredCompanyType=ref('companyTypes',3);
 assert.ok(check(proposal).some(issue=>issue.questionId==='MTS-01-Q11'));
 proposal.insured.legalName='Example Motors Ltd';assert.deepEqual(check(proposal),[]);
 delete proposal.insured.contact.mobile;assert.ok(check(proposal).some(issue=>issue.code==='contact-number-required'));
 proposal.insured.contact.telephone='01234567890';assert.deepEqual(check(proposal),[]);
 proposal.insured.contact.mobile='061234567890';assert.deepEqual(check(proposal).map(issue=>issue.code),['contact-number-too-long','mobile-prefix-invalid']);
});

test('premises branch requires every row field and preserves inactive rows for explicit correction',()=>{
 const proposal=fixture();const selection=proposal.risk.business.responses.answers[0];selection.value=ref('tradingFroms',3);
 assert.deepEqual(check(proposal).map(issue=>issue.code),['premises-required']);
 const premise={id:'aaaaaaaa-0000-4000-8000-000000000001',use:ref('tradingPremiseTypes'),yearsTrading:0,sharedWorksite:false,address:{postcode:'AB1 2CD',houseNumber:'2',street:'Example Road',county:'Example County'}};
 proposal.risk.premises=[premise];assert.equal(draft(proposal),true,JSON.stringify(draft.errors));assert.deepEqual(check(proposal),[]);
 for(const owner of businessReadinessOwners.premises) {
  const copy=structuredClone(proposal);const keys=questions.mappings.find(row=>row.owner===owner).canonicalPath.replace('risk.premises[].','').split('.');const leaf=keys.pop();
  delete keys.reduce((item,key)=>item[key],copy.risk.premises[0])[leaf];
  assert.ok(check(copy).some(issue=>issue.questionId===owner&&issue.path.startsWith('/risk/premises/0/')),owner);
 }
 selection.value=ref('tradingFroms',1);const before=structuredClone(proposal);
 assert.equal(check(proposal)[0].code,'inactive-premises-retained');assert.deepEqual(proposal,before);
 selection.value.label='Forged';assert.equal(check(proposal)[0].code,'premises-context-required');
});

test('declaration and association details are required only for their affirmative parent',()=>{
 for(const owner of [...businessReadinessOwners.declarations,'MTS-03-Q04']) {
  const proposal=fixture();const response=owner.startsWith('MTS-12')?proposal.risk.declarations:proposal.risk.business.responses;
  response.answers.find(answer=>answer.questionId===owner).value=true;
  const child=questions.mappings.find(row=>row.requiredWhen?.questionId===owner);
  assert.ok(check(proposal).some(issue=>issue.questionId===child.questionId&&issue.code==='conditional-answer-required'));
  response.answers.push({questionId:child.questionId,kind:'text',value:'Fictional explanation'});assert.deepEqual(check(proposal),[]);
  response.answers.at(-1).value='  ';assert.ok(check(proposal).some(issue=>issue.code==='conditional-answer-required'));
 }
});

test('source minimums and unsupported product fail explicitly',()=>{
 const proposal=fixture();proposal.risk.business.startedOn='1899-12-31';proposal.risk.business.responses.answers.find(answer=>answer.questionId==='MTS-03-Q08').value=0;
 assert.deepEqual(check(proposal).map(issue=>issue.code),['vehicles-handled-minimum','business-start-too-early']);
 proposal.productCode='commercial-combined';assert.throws(()=>check(proposal),/unsupported-capture-product/);
});
