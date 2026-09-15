import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {validateQuoteReferences} from '../scripts/quote-semantic-contract.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const manifest=await read('quote-prototype-bindings.json'),ownership=await read('quote-control-ownership.json'),questions=await read('quote-question-catalogue.json');

test('every phase5 source-bound control has exact capture paths in the real draft schema',async()=>{
 const schema=await read('schemas/quote-draft.schema.json');
 const expected=ownership.controls.filter(control=>control.featurePhase===5&&control.sourceFieldBindings.length);
 assert.deepEqual(manifest.controls.map(c=>c.controlId),expected.map(c=>c.controlId));
 assert.equal(manifest.sourceSha256,ownership.sourceSha256);assert.equal(manifest.questionVersion,questions.version);
 for(const control of manifest.controls)for(const binding of control.bindings)for(const path of binding.paths) {
  let node=schema;
  for(const part of path.split('.')) {
   while(node.$ref)node=schema.$defs[node.$ref.split('/').at(-1)];
   node=node.properties?.[part.replace(/\[\]$/,'')];assert.ok(node,`${control.controlId}: ${path}`);
   if(part.endsWith('[]')){node=node.items;assert.ok(node,path);}
  }
  assert.ok(!path.startsWith('term.'));assert.ok(!path.includes('+'));
 }
});

test('prototype questions retain their exact scoped identity and product subset',()=>{
 for(const control of manifest.controls)for(const binding of control.bindings.filter(b=>b.kind==='answer')) {
  const question=questions.mappings.find(q=>q.questionId===binding.questionId);
  assert.deepEqual(binding.paths,[question.canonicalPath]);assert.equal(binding.answerKind,question.answerKind);
  assert.deepEqual(binding.products,question.products);assert.ok(binding.products.every(p=>control.products.includes(p)));
 }
});

test('compound prototype fields explicitly split entered data and do not write derived UTC or root losses',()=>{
 const byId=id=>manifest.controls.find(c=>c.controlId===id).bindings[0];
 assert.deepEqual(byId('CTL-de4a2a5bf892').paths,['termIntent.localStartDate','termIntent.localStartTime','termIntent.timeZone','termIntent.utcOffsetMinutes']);
 assert.deepEqual(byId('CTL-f20ddc71047e').paths,['risk.vehicles[].make','risk.vehicles[].model']);
 assert.deepEqual(byId('CTL-f1de7df1850d').paths,['risk.vehicles[].declaredEngineSize','risk.vehicles[].grossWeightKg']);
 assert.ok(manifest.controls.flatMap(c=>c.bindings).every(b=>!b.paths.includes('risk.losses')));
});

test('driver status, usage and offence labels map completely to trusted source references',async()=>{
 const references=await read('reference-data/motor-trade-capture.json');
 const inventory=JSON.parse(await readFile(new URL('../docs/design/control-inventory.json',import.meta.url),'utf8')).controls;
 const rendered=JSON.parse(await readFile(new URL('../docs/design/source/prototype-render-data.json',import.meta.url),'utf8')).items;
 const controls=manifest.controls.filter(c=>c.bindings.some(b=>b.referenceMapping));assert.equal(controls.length,3);
 for(const control of controls) {
  const binding=control.bindings[0],original=inventory.find(c=>c.id===control.controlId);
  const source=rendered.find(r=>r.method===original.method&&r.path===original.path&&r.label===original.label&&r.tabs.some(t=>original.tabs.includes(t)));
  assert.deepEqual(Object.keys(binding.referenceMapping),source.options.filter(label=>!binding.placeholderOptions?.includes(label)));
  for(const selection of Object.values(binding.referenceMapping)) {
   const proposal={};let cursor=proposal;const parts=binding.paths[0].split('.');
   parts.forEach((part,index)=>{const list=part.endsWith('[]'),key=part.replace(/\[\]$/,'');if(index===parts.length-1)cursor[key]=selection;else if(list){cursor[key]=[{}];cursor=cursor[key][0];}else {cursor[key]={};cursor=cursor[key];}});
   assert.deepEqual(validateQuoteReferences(proposal,references),[]);
  }
 }
});

test('reference conversion preserves business meanings instead of prototype option ordinals',()=>{
 const role=manifest.controls.find(c=>c.controlId==='CTL-e7838b90abda').bindings[0].referenceMapping;
 assert.equal(role.Proprietor.value,3);assert.equal(role.Proprietor.label,'Policyholder');assert.equal(role.Employee.value,2);assert.equal(role['Business partner'].value,4);
 const usage=manifest.controls.find(c=>c.controlId==='CTL-1624517d8aa7').bindings[0].referenceMapping;
 assert.equal(usage['Motor trade + SD&P'].label,'Motor Trade with Social, Domestic and Pleasure');
 const offence=manifest.controls.find(c=>c.controlId==='CTL-7b5a86aac53f').bindings[0];
 assert.ok(!offence.referenceMapping['Select a code']);assert.deepEqual(offence.placeholderOptions,['Select a code']);
 for(const [label,reference] of Object.entries(offence.referenceMapping))assert.ok(reference.label.startsWith(label.slice(0,4)+' - '));
});
