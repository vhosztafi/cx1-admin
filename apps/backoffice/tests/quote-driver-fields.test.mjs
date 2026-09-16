import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { sourceDriverFields } from '../lib/quote-driver-fields.ts';
const read = async path => JSON.parse(await readFile(new URL(`../../../${path}`,import.meta.url),'utf8'));
const questions=await read('contracts/quote-question-catalogue.json'), references=await read('contracts/reference-data/motor-trade-capture.json');
const labels=Object.fromEntries((await read('docs/design/funnel-field-mapping.json')).mappings.map(row=>[row.owner,row.label]));
test('driver form projects every source driver and plan field with independent history scopes',()=>{
 const fields=sourceDriverFields(questions.mappings,references.bindings,references.collections,labels);
 const expected=questions.mappings.filter(row=>row.owner.startsWith('MTS-06-')||row.canonicalPath.startsWith('risk.drivers[].'));
 for(const row of expected) assert.equal(fields.filter(field=>field.id===row.owner).length,1,row.owner);
 assert.equal(new Set(fields.map(field=>field.id)).size,fields.length);
 assert.equal(fields.find(field=>field.id==='MTS-06-Q01').group,'plan');
 assert.equal(fields.find(field=>field.id==='MTS-06-Q36').group,'convictions');
 assert.equal(fields.find(field=>field.id==='MTS-06-Q36').kind,'money');
 assert.equal(fields.find(field=>field.id==='prototype.addinc.own-damage').group,'losses');
 assert.equal(fields.find(field=>field.id==='driver.fullName').path,'fullName');
 assert.equal(fields.find(field=>field.id==='driver.licence.testDate').path,'licence.testDate');
 for(const field of fields.filter(field=>field.kind==='reference'&&!field.unavailable)) assert.deepEqual(field.choices,references.collections[field.collection].map(({value,text})=>({value,text})));
 for(const field of fields.filter(field=>field.unavailable)) { assert.ok(['MTS-06-Q59','MTS-06-Q60'].includes(field.id));assert.deepEqual(field.choices,[]); }
 assert.ok(fields.every(field=>!field.label.includes('{{')));
});
test('missing pinned reference bindings fail closed rather than substitute options',()=>{
 assert.throws(()=>sourceDriverFields(questions.mappings,references.bindings.filter(row=>row.owner!=='MTS-06-Q08'),references.collections,labels));
});
