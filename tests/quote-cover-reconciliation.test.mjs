import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {reconcileQuoteCover,coverReconciliationGroups} from '../scripts/quote-cover-reconciliation.mjs';
const catalogue=JSON.parse(await readFile(new URL('../contracts/reference-data/motor-trade-capture.json',import.meta.url),'utf8'));
const selection=(collection,value)=>{const row=catalogue.collections[collection].find(row=>row.value===value);assert.ok(row);return {collection,value,label:row.text,version:catalogue.version};};
const answer=(questionId,collection,value)=>({questionId,kind:'reference',value:selection(collection,value)});
const proposal=(...answers)=>({cover:{responses:{answers}}});

test('all prototype cover ordinals have explicit semantic meanings matching pinned source labels',()=>{
  for(const group of coverReconciliationGroups) {
    const rows=catalogue.collections[group.prototype];assert.equal(rows.length,group.values.length);
    rows.forEach((row,index)=>{
      const result=reconcileQuoteCover(proposal(answer(group.prototype,group.prototype,row.value)),catalogue);
      assert.deepEqual(result,{facts:{[group.fact]:group.values[index]},issues:[]});
      if(group.fact!=='coverLevel')assert.equal(row.text.replace(/[£,]/g,''),String(Number(group.values[index])));
    });
  }
});

test('equal source/prototype meanings reconcile despite different IDs without changing declarations',()=>{
  const own=catalogue.collections.indemnityOwnVehicles.find(row=>row.numericValue===10000);
  const input=proposal(answer('MTS-05-Q02','indemnityOwnVehicles',own.value),answer('prototype.quote.d9dd069a314c','prototype.quote.d9dd069a314c',2));
  const before=structuredClone(input);
  assert.deepEqual(reconcileQuoteCover(input,catalogue),{facts:{ownVehicleLimit:'10000.00'},issues:[]});
  assert.deepEqual(input,before);
});

test('the same numeric option ID can mean different limits and produces a conflict on both fields',()=>{
  const input=proposal(answer('MTS-05-Q02','indemnityOwnVehicles',2),answer('prototype.quote.d9dd069a314c','prototype.quote.d9dd069a314c',2));
  const result=reconcileQuoteCover(input,catalogue);
  assert.deepEqual(result.facts,{});
  assert.deepEqual(result.issues,[
    {code:'conflicting-cover-declarations',path:'/cover/responses/answers/0/value',fact:'ownVehicleLimit'},
    {code:'conflicting-cover-declarations',path:'/cover/responses/answers/1/value',fact:'ownVehicleLimit'},
  ]);
});

test('unresolved references cannot silently be replaced with another declaration or assumed zero',()=>{
  assert.deepEqual(reconcileQuoteCover({},catalogue),{facts:{},issues:[]});
  const input=proposal(answer('MTS-05-Q01','coverLevels',1),answer('prototype.quote.2beaf3b8d546','prototype.quote.2beaf3b8d546',1));
  input.cover.responses.answers[0].value.value='1';
  const result=reconcileQuoteCover(input,catalogue);
  assert.deepEqual(result.facts,{});assert.equal(result.issues[0].code,'unresolved-cover-selection');
  input.cover.responses.answers[0].value=selection('coverLevels',1);input.cover.responses.answers[1].value.label='Forged';
  assert.equal(reconcileQuoteCover(input,catalogue).issues[0].code,'unresolved-cover-selection');
  for(const collection of ['constructor','__proto__','not-a-collection']) {
    input.cover.responses.answers[0].value.collection=collection;
    assert.equal(reconcileQuoteCover(input,catalogue).issues[0].code,'unresolved-cover-selection');
  }
});
