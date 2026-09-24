import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { createHash } from 'node:crypto';

const inventory=JSON.parse(readFileSync('docs/design/control-inventory.json','utf8'));
const ledger=JSON.parse(readFileSync('docs/design/finance-source-ledger.json','utf8'));
const sourceBytes=readFileSync('docs/design/source/prototype-template.txt');
const source=sourceBytes.toString('utf8').split(/\r?\n/);
const planIds=new Set(Array.from({length:13},(_,i)=>`10-${String(i+1).padStart(2,'0')}`));
const reqs=new Set(['FIN-01','FIN-02','FIN-03','FIN-04','FIN-05','FIN-06','FIN-07','FIN-08','POL-01']);

test('the original finance source and direct control identities are exhaustively pinned',()=>{
  assert.equal(createHash('sha256').update(sourceBytes).digest('hex'),ledger.sourceSha256);
  assert.equal(ledger.inventorySourceSha256,inventory.sourceSha256);
  const original=inventory.controls.filter(x=>x.method==='pAccounting');
  assert.equal(original.length,24);
  assert.deepEqual(new Set(ledger.directControls.map(x=>x.id)),new Set(original.map(x=>x.id)));
  assert.equal(ledger.directControls.length,24);
  assert.equal(new Set(ledger.directControls.map(x=>x.id)).size,24);
  for(const item of ledger.directControls){
    const raw=original.find(x=>x.id===item.id);
    assert.equal(item.path,raw.path);
    assert.equal(item.kind,raw.kind);
    assert.ok(reqs.has(item.requirement),`${item.id} missing requirement`);
    assert.ok(planIds.has(item.plan),`${item.id} missing plan`);
    assert.equal(item.status,'planned');
  }
});

test('every nonblank pAccounting source line has exact text and planned ownership',()=>{
  assert.deepEqual(ledger.sourceRange,{first:2824,last:2989});
  assert.match(source[2823],/pAccounting\(\)/);
  assert.match(source[2989],/pReporting\(\)/);
  const expected=[];
  for(let line=2824;line<=2989;line++)if(source[line-1].trim())expected.push(line);
  assert.deepEqual(ledger.sourceLines.map(x=>x.line),expected);
  for(const item of ledger.sourceLines){
    assert.equal(item.id,`FIN-SRC-${String(item.line).padStart(5,'0')}`);
    assert.equal(item.source,source[item.line-1].trim());
    assert.ok(reqs.has(item.requirement),`${item.id} requirement`);
    assert.ok(planIds.has(item.plan),`${item.id} plan`);
    assert.equal(item.status,'planned');
  }
  assert.equal(ledger.counts.sourceLines,expected.length);
});

test('inherited links preserve original owner and finance destination',()=>{
  const expected=new Map([
    ['CTL-e834b4734421',[4,'FIN-01']],
    ['CTL-f68f49a17b06',[11,'FIN-06']],
    ['CTL-92991f22c2a4',[7,'POL-01']]
  ]);
  assert.equal(ledger.inheritedControls.length,expected.size);
  for(const item of ledger.inheritedControls){
    const original=inventory.controls.find(x=>x.id===item.id);
    assert.ok(original);
    assert.deepEqual([item.sourcePhase,item.requirement],expected.get(item.id));
    assert.equal(item.label,original.label);
    assert.equal(item.path,original.path);
    assert.notEqual(item.status,'verified');
  }
});

test('source obligations that lack active controls are explicit',()=>{
  const ids=new Set(ledger.impliedObligations.map(x=>x.id));
  for(const suffix of ['receipt-view','batch-failure-row-two','batch-failure-row-three','batch-submit-disabled','prior-batch-download','refund-approval','period-close']){
    assert.ok(ids.has(`FIN-IMPLIED-${suffix}`),suffix);
  }
  for(const item of ledger.impliedObligations){assert.ok(reqs.has(item.requirement));assert.ok(planIds.has(item.plan));assert.equal(item.status,'planned');}
});
