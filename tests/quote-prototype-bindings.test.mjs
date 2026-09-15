import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
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
