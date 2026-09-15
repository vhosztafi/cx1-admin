import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuoteReferences} from '../scripts/quote-semantic-contract.mjs';
import {createQuoteValidationPipeline} from '../scripts/quote-validation-pipeline.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../${path}`,import.meta.url),'utf8'));
const questions=await read('contracts/quote-question-catalogue.json'),references=await read('contracts/reference-data/motor-trade-capture.json');
const ref=(collection,row)=>({collection,value:row.value,label:row.text,version:references.version});

test('all direct vehicle/premises options match actual rendered source choices and have bound typed identities',async()=>{
 const controls=(await read('docs/design/control-inventory.json')).controls,rendered=(await read('docs/design/source/prototype-render-data.json')).items;
 assert.equal(questions.directReferenceFields.length,3);
 for(const field of questions.directReferenceFields) {
  const control=controls.find(c=>c.id===field.controlId),source=rendered.find(r=>r.method===control.method&&r.path===control.path&&r.label===control.label&&r.tabs.some(tab=>control.tabs.includes(tab)));
  assert.deepEqual(field.sourceOptions,source.options);assert.deepEqual(references.collections[field.collection].map(row=>row.text),source.options);
  const [collection,property]=field.canonicalPath.replace('risk.','').split('[].');
  for(const row of references.collections[field.collection]) {
   const p={risk:{[collection]:[{[property]:ref(field.collection,row)}]}};assert.deepEqual(validateQuoteReferences(p,references),[]);
   p.risk[collection][0][property].value=String(row.value);assert.ok(validateQuoteReferences(p,references).length);
  }
 }
});

test('prototype premises activity is distinct from physical premise type and other reference families',async()=>{
 const {proposal:p,context}=await read('contracts/examples/quote-capture-motor-trade-combined.json'),validate=await createQuoteValidationPipeline();
 assert.equal(p.risk.premises[0].use.label,'Showroom');assert.equal(p.risk.premises[0].declaredUse.label,'Sales only');
 assert.equal(validate(JSON.stringify(p),context).status,'section-checks-pass');
 p.risk.premises[0].declaredUse=p.risk.premises[0].use;assert.ok(validate(JSON.stringify(p),context).issues.some(i=>i.stage==='references'));
});

test('composed checks require direct prototype choices and reject stale identities without changing captured data',async()=>{
 const {proposal:p,context}=await read('contracts/examples/quote-capture-motor-trade-combined.json'),validate=await createQuoteValidationPipeline();
 const before=structuredClone(p);validate(JSON.stringify(p),context);assert.deepEqual(p,before);
 p.risk.vehicles[0].body.version='old';assert.ok(validate(JSON.stringify(p),context).issues.some(i=>i.stage==='references'));
 delete p.risk.vehicles[0].body;delete p.risk.premises[0].declaredUse;delete p.risk.premises[0].security;
 assert.equal(validate(JSON.stringify(p),context).issues.filter(i=>i.code==='required-prototype-reference').length,3);
});
