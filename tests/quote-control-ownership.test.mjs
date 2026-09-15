import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
const read=async path=>JSON.parse(await readFile(new URL(`../${path}`,import.meta.url),'utf8'));
const ownership=await read('contracts/quote-control-ownership.json');
const inventory=await read('docs/design/control-inventory.json');
const apiMap=await read('docs/design/api-control-map.json');

test('all 364 quote candidate controls retain exact source identity and operation dependencies',()=>{
  assert.equal(ownership.sourceSha256,inventory.sourceSha256);
  assert.equal(ownership.controls.length,364);
  assert.equal(new Set(ownership.controls.map(row=>row.controlId)).size,364);
  for(const row of ownership.controls) {
    const source=inventory.controls.find(control=>control.id===row.controlId);
    assert.ok(source,row.controlId);
    for(const field of ['method','path','label'])assert.equal(row[field],source[field]);
    const reviewed=apiMap.controls.find(control=>control.controlId===row.controlId);
    assert.deepEqual(row.operationDependencies.map(item=>item.operationId),reviewed.operationIds);
    assert.ok(row.rationale);assert.match(row.runtimeStatus,/pending/);
  }
});

test('Commercial Combined wizard stages and location/wage/loss modals remain Phase8',()=>{
  const rows=ownership.controls.filter(row=>row.products.every(product=>product==='commercial-combined'));
  assert.equal(rows.length,166);
  for(const row of rows){assert.equal(row.featurePhase,8);assert.deepEqual(row.activePhase05Operations,[]);}
  for(const row of ownership.controls.filter(row=>row.sourceModalKinds.includes('addprem')))
    assert.deepEqual(row.products,['motor-trade-combined']);
});

test('Fleet cannot create quotes and global report/search dependencies do not imply Phase5 completion',()=>{
  const fleet=ownership.controls.find(row=>row.label==='Motor Trade Fleet');
  assert.equal(fleet.disposition,'unavailable');assert.equal(fleet.featurePhase,null);assert.deepEqual(fleet.activePhase05Operations,[]);
  const global=ownership.controls.filter(row=>['pDashboard','pSearch','pReporting'].includes(row.method)||row.label==='Advanced Search');
  assert.equal(global.length,14);
  for(const row of global){assert.equal(row.featurePhase,12);assert.deepEqual(row.activePhase05Operations,[]);}
  assert.equal(ownership.controls.filter(row=>row.featurePhase===5).length,183);
});

test('shared quote controls distinguish capture from later rating and policy history operations',()=>{
  const rating=ownership.controls.filter(row=>row.operationDependencies.some(dependency=>dependency.operationId==='rateQuote'));
  assert.ok(rating.length>0);
  for(const row of rating){assert.equal(row.operationDependencies.find(item=>item.operationId==='rateQuote').implementationPhase,6);assert.ok(!row.activePhase05Operations.includes('rateQuote'));}
  const mixed=ownership.controls.filter(row=>row.method==='pRisks'&&row.operationDependencies.some(item=>item.operationId==='getQuote'));
  assert.ok(mixed.length>0);
  for(const row of mixed){assert.ok(row.activePhase05Operations.includes('getQuote'));assert.ok(!row.activePhase05Operations.includes('getPolicy'));assert.ok(!row.activePhase05Operations.includes('getPolicyAsAt'));}
  const product=ownership.controls.find(row=>row.controlId==='CTL-f90bbda7baf4');
  assert.ok(product.activePhase05Operations.includes('listProductVersions'));
});
