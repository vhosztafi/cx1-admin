import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
const read=async p=>JSON.parse(await readFile(p,'utf8'));
const path='.planning/phases/09-tasks-documents-communication-and-incidents/09-SOURCE-INVENTORY.json';
test('operational source ledger preserves all direct and inherited original identities',async()=>{
 const [ledger,original,seven,eight]=await Promise.all([read(path),read('docs/design/control-inventory.json'),read('.planning/phases/07-policy-lifecycle-and-history/07-SOURCE-INVENTORY.json'),read('.planning/phases/08-commercial-combined-back-office/08-SOURCE-INVENTORY.json')]);
 const direct=original.controls.filter(x=>Number(x.phase)===9);assert.equal(direct.length,62);
 const required=new Set([...direct.map(x=>x.id),...seven.controls.filter(x=>x.ownerPhase===9).map(x=>x.id),...eight.policyControls.filter(x=>x.ownerPhase===9).map(x=>x.controlId)]);
 const ids=new Set(ledger.controls.map(x=>x.controlId));assert.equal(ids.size,ledger.controls.length);
 for(const id of required)assert.ok(ids.has(id),id);
 assert.equal(ledger.sourceSha256,original.sourceSha256);
 const by=new Map(original.controls.map(x=>[x.id,x]));
 for(const row of ledger.controls){const source=by.get(row.controlId);assert.ok(source,row.controlId);assert.equal(row.source.method,source.method);assert.equal(row.source.path,source.path);assert.deepEqual(row.source.handlers,source.handlers??{});assert.match(row.ownerPlan,/^(09|11|12)-\d{2}$|^phase-(11|12)$/);assert.ok(row.disposition);}
});
test('operational source keeps raw display occurrence identities and commercial claims ownership',async()=>{
 const [ledger,render,eight]=await Promise.all([read(path),read('docs/design/source/prototype-render-data.json'),read('.planning/phases/08-commercial-combined-back-office/08-SOURCE-INVENTORY.json')]);
 for(const row of ledger.displayOccurrences){assert.deepEqual(row.source,render.items[row.sourceOccurrence]);assert.ok(row.ownerPlan);}
 for(const method of ['pTasks','pTask','pLogClaim','pClaim'])assert.deepEqual(ledger.displayOccurrences.filter(x=>x.source.method===method).map(x=>x.sourceOccurrence),render.items.flatMap((x,i)=>x.method===method?[i]:[]));
 const claims=eight.policyDisplayItems.filter(x=>x.ownerPhase===9);assert.equal(claims.length,33);
 assert.deepEqual(ledger.commercialClaimsOccurrences.map(x=>x.sourceOccurrence),claims.map(x=>x.sourceOccurrence));
});
test('source branches absent from default rendering have explicit owners and source evidence',async()=>{
 const ledger=await read(path),source=await readFile('docs/design/source/prototype-template.txt','utf8');
 for(const id of ['TASK-REOPEN','TASK-BULK-COMPLETE','TASK-BULK-REASSIGN','TASK-BULK-DUE','INCIDENT-VEHICLE','INCIDENT-DRIVER','INCIDENT-DRIVABLE','INCIDENT-HANDOFF-READY'])assert.ok(ledger.branches.some(x=>x.id===id),id);
 for(const row of ledger.branches){assert.ok(source.includes(row.sourceNeedle),row.id);assert.match(row.ownerPlan,/^09-\d{2}$/);assert.ok(row.expectedBehavior);}
 assert.ok(ledger.controls.some(x=>x.source.method==='pMatch'));
 assert.ok(ledger.controls.some(x=>x.source.method==='pAsAt'));
 assert.ok(ledger.controls.some(x=>x.source.method==='pReporting'&&x.ownerPhase===12));
});
