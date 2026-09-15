import test from 'node:test';
import assert from 'node:assert/strict';
import {compareQuoteProposals} from '../scripts/quote-revision-diff.mjs';
const id=n=>`aaaaaaaa-0000-4000-8000-${String(n).padStart(12,'0')}`;

test('keyed driver edits retain both snapshot pointers after reordering',()=>{
  const before={risk:{drivers:[{id:id(1),fullName:'First'},{id:id(2),fullName:'Second'}]}};
  const after={risk:{drivers:[{id:id(2),fullName:'Changed'},{id:id(1),fullName:'First'}]}};
  const original=structuredClone([before,after]);
  const changes=compareQuoteProposals(before,after);
  assert.equal(changes.length,2);
  assert.deepEqual(changes[0],{kind:'changed',path:'/risk/drivers/0/fullName',itemId:id(2),before:{path:'/risk/drivers/1/fullName',json:'"Second"'},after:{path:'/risk/drivers/0/fullName',json:'"Changed"'}});
  assert.equal(changes[1].kind,'reordered');assert.deepEqual(JSON.parse(changes[1].before.json),before.risk.drivers);
  assert.deepEqual([before,after],original);
});

test('child additions and removals are complete records rather than index-based replacements',()=>{
  const old={id:id(1),convictions:[{id:id(3),points:3}]};const added={id:id(2),fullName:'New'};
  const changes=compareQuoteProposals({risk:{drivers:[old]}},{risk:{drivers:[added]}});
  assert.deepEqual(changes.map(change=>change.kind),['removed','added']);
  assert.deepEqual(JSON.parse(changes[0].before.json),old);assert.ok(!('after' in changes[0]));
  assert.deepEqual(JSON.parse(changes[1].after.json),added);assert.ok(!('before' in changes[1]));
});

test('missing, null, false, zero and typed reference IDs remain distinguishable',()=>{
  const changes=compareQuoteProposals({risk:{a:null,b:false,c:0,d:1}},{risk:{b:0,c:false,d:'1',e:null}});
  assert.equal(changes.length,5);
  const removed=changes.find(change=>change.path==='/risk/a');assert.equal(removed.before.json,'null');assert.ok(!removed.after);
  const added=changes.find(change=>change.path==='/risk/e');assert.equal(added.after.json,'null');assert.ok(!added.before);
  const typed=changes.find(change=>change.path==='/risk/d');assert.equal(typed.before.json,'1');assert.equal(typed.after.json,'"1"');
});

test('comparison ignores object property order but retains array order and escaped pointer keys',()=>{
  assert.deepEqual(compareQuoteProposals({risk:{values:[{value:1,label:'One'}]}},{risk:{values:[{label:'One',value:1}]}}),[]);
  const changes=compareQuoteProposals({risk:{'a/b~c':[1,2]}},{risk:{'a/b~c':[2,1]}});
  assert.equal(changes[0].path,'/risk/a~1b~0c');assert.equal(changes[0].before.json,'[1,2]');assert.equal(changes[0].after.json,'[2,1]');
  const long='Fictional detail '.repeat(1000);
  const full=compareQuoteProposals({risk:{details:long}},{risk:{details:'Replaced'}});
  assert.equal(JSON.parse(full[0].before.json),long);
});

test('ambiguous duplicate child IDs are rejected while nested child identities survive edits',()=>{
  assert.throws(()=>compareQuoteProposals({risk:{drivers:[{id:id(1)},{id:id(1).toUpperCase()}]}},{risk:{drivers:[]}}),/duplicate-item-id/);
  const changes=compareQuoteProposals({risk:{drivers:[{id:id(1),losses:[{id:id(2),amount:'1.00'}]}]}},{risk:{drivers:[{id:id(1),losses:[{id:id(2),amount:'2.00'}]}]}});
  assert.equal(changes[0].itemId,id(2));assert.equal(changes[0].path,'/risk/drivers/0/losses/0/amount');
});
