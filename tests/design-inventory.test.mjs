import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
const json=async p=>JSON.parse(await readFile(new URL(`../${p}`,import.meta.url),'utf8'));
const inv=await json('docs/design/control-inventory.json');
const map=await json('docs/design/funnel-field-mapping.json');
test('inventory anchors the actual immutable prototype source',async()=>{
 const bytes=await readFile(new URL('../docs/prototype/Cover MGA Back Office-4.html',import.meta.url));
 assert.equal(inv.sourceSha256,createHash('sha256').update(bytes).digest('hex'));
 const extracted=await json('docs/design/source/prototype-evidence.json');
 assert.deepEqual(inv.sourceRecords.map(x=>x.line),extracted.evidence.map(x=>x.line));
});
test('every discovered interaction has unique identity and valid requirement ownership',async()=>{
 const req=await readFile(new URL('../.planning/REQUIREMENTS.md',import.meta.url),'utf8');
 assert.equal(new Set(inv.controls.map(x=>x.id)).size,inv.controls.length);
 for(const c of inv.controls){assert.ok(req.includes(`**${c.requirement}**`),c.id);assert.ok(c.permission&&c.failure&&c.persistence&&c.acceptance,c.id);assert.ok(c.phase>=1&&c.phase<=13);}
});
test('all modal confirmations and all quote product wizards are represented',()=>{
 for(const kind of inv.modalKinds) assert.ok(inv.controls.some(c=>c.method==='modalVals'&&c.tabs.includes(kind)),kind);
 for(const product of ['Motor Trade Road Risks','Motor Trade Combined','Commercial Combined']){
  const count=product==='Commercial Combined'?10:9;
  for(let step=1;step<=count;step++)assert.ok(inv.controls.some(c=>c.method==='pNewQuote'&&c.tabs.includes(`${product}:step-${step}`)),`${product}/${step}`);
 }
});
test('conditional declaration detail and vehicle proportions are mapped',()=>{
 assert.ok(map.mappings.some(m=>m.rawPath==='anyOfDriversHadSpecialTermsAppliedDetails'));
 assert.ok(map.mappings.some(m=>m.rawPath==='percentageOfStandardCars'));
 assert.ok(map.mappings.some(m=>m.rawPath==='proposerPolicyStartDate'&&m.proposedSection==='term'));
 assert.ok(map.mappings.some(m=>m.source.includes('specified-questions.json')));
});
