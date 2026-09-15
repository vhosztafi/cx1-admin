import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {reconcileQuoteEntity} from '../scripts/quote-entity-reconciliation.mjs';
import {createQuoteValidationPipeline} from '../scripts/quote-validation-pipeline.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const ref=value=>({collection:'companyTypes',value,label:references.collections.companyTypes.find(row=>row.value===value).text,version:references.version});
const check=p=>reconcileQuoteEntity(p,questions,references);

test('legal entities retain distinct meanings across every source company category for both products',()=>{
 for(const productCode of questions.products)for(const [entityType,allowed] of [['sole-trader',[1]],['partnership',[4]],['limited-company',[2,3]],['llp',[4]]]) {
  for(const company of references.collections.companyTypes) {
   const p={productCode,insured:{entityType,declaredCompanyType:ref(company.value),companyNumber:'DEMO1234'}};
   if(allowed.includes(company.value)){const before=structuredClone(p);assert.deepEqual(check(p),[]);assert.deepEqual(p,before);}
   else assert.equal(check(p).filter(i=>i.code==='conflicting-legal-entity').length,2);
  }
 }
});

test('incorporated entities require company number and untrusted source identities cannot settle their meaning',()=>{
 for(const [entityType,value] of [['limited-company',3],['llp',4]]) {
  const p={productCode:'motor-trade-road-risks',insured:{entityType,declaredCompanyType:ref(value)}};
  assert.equal(check(p)[0].code,'incorporated-company-number-required');p.insured.companyNumber='   ';assert.equal(check(p)[0].code,'incorporated-company-number-required');
  p.insured.companyNumber='DEMO1234';p.insured.declaredCompanyType.label='Forged';assert.equal(check(p)[0].code,'legal-entity-company-context-required');
 }
});

test('prototype entity choices map exactly and composed capture requires the separate legal entity',async()=>{
 const manifest=await read('quote-prototype-bindings.json');assert.deepEqual(manifest.controls.find(c=>c.controlId==='CTL-212ce9265e52').bindings[0].valueMapping,{'Sole trader':'sole-trader',Partnership:'partnership','Limited company':'limited-company',LLP:'llp'});
 const {proposal:p,context}=await read('examples/quote-capture-motor-trade-combined.json'),validate=await createQuoteValidationPipeline();
 delete p.insured.entityType;assert.ok(validate(JSON.stringify(p),context).issues.some(i=>i.code==='legal-entity-required'));
 p.insured.entityType='limited-company';assert.ok(validate(JSON.stringify(p),context).issues.some(i=>i.code==='conflicting-legal-entity'));assert.ok(validate(JSON.stringify(p),context).issues.some(i=>i.code==='incorporated-company-number-required'));
});
