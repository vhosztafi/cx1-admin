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
 assert.equal(questions.directReferenceFields.length,4);
 for(const field of questions.directReferenceFields) {
  const control=controls.find(c=>c.id===field.controlId),source=rendered.find(r=>r.method===control.method&&r.path===control.path&&r.label===control.label&&r.tabs.some(tab=>control.tabs.includes(tab)));
  assert.deepEqual(field.sourceOptions,source.options);assert.deepEqual(references.collections[field.collection].map(row=>row.text),source.options);
  for(const row of references.collections[field.collection]) {
   const p={};let cursor=p;const parts=field.canonicalPath.split('.'),selection=ref(field.collection,row);
   parts.forEach((part,index)=>{const key=part.replace(/\[\]$/,'');if(index===parts.length-1)cursor[key]=selection;else if(part.endsWith('[]')){cursor[key]=[{}];cursor=cursor[key][0];}else {cursor[key]={};cursor=cursor[key];}});
   assert.deepEqual(validateQuoteReferences(p,references),[]);
   selection.value=String(row.value);assert.ok(validateQuoteReferences(p,references).length);
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

test('incident type preserves third-party injury independently of source claim classification and scopes missing choices per loss',async()=>{
 const {proposal:p,context}=await read('contracts/examples/quote-capture-motor-trade-combined.json');
 const specific=references.collections['prototype.incident-type'].find(row=>row.text==='Third party injury');assert.ok(specific);
 const generic=references.collections.driverClaimTypes.find(row=>row.text==='Accident Claim');
 p.risk.drivers[0].losses=[
  {id:'aaaaaaaa-0000-4000-8000-000000000098',declaredType:ref('prototype.incident-type',specific),type:ref('driverClaimTypes',generic)},
  {id:'aaaaaaaa-0000-4000-8000-000000000099'},
 ];
 const validate=await createQuoteValidationPipeline(),before=structuredClone(p),result=validate(JSON.stringify(p),context);
 assert.ok(result.issues.some(i=>i.code==='required-prototype-reference'&&i.path==='/risk/drivers/0/losses/1/declaredType'));
 assert.ok(!result.issues.some(i=>i.stage==='references'));assert.deepEqual(p,before);
 p.risk.drivers[0].losses[0].declaredType=p.risk.drivers[0].losses[0].type;
 assert.ok(validate(JSON.stringify(p),context).issues.some(i=>i.stage==='references'&&i.path.includes('/losses/0/declaredType')));
});
