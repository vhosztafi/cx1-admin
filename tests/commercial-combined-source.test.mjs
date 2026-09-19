import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
const read=async p=>JSON.parse(await readFile(p,'utf8'));
const ledgerPath='.planning/phases/08-commercial-combined-back-office/08-SOURCE-INVENTORY.json';
test('CC source ledger preserves every original capture control and question identity',async()=>{
 const [ledger,ownership,catalogue]=await Promise.all([read(ledgerPath),read('contracts/quote-control-ownership.json'),read('contracts/quote-question-catalogue.json')]);
 const original=ownership.controls.filter(x=>x.featurePhase===8).map(x=>x.controlId).sort();
 assert.equal(original.length,166);assert.equal(catalogue.deferredQuestions.length,109);
 assert.deepEqual(ledger.captureControls.map(x=>x.controlId).sort(),original);
 assert.deepEqual(ledger.questions.map(x=>x.questionId).sort(),catalogue.deferredQuestions.map(x=>x.questionId).sort());
 assert.equal(new Set(ledger.captureControls.map(x=>x.controlId)).size,166);
 for(const row of ledger.captureControls){assert.match(row.ownerPlan,/^08-\d{2}$/);assert.ok(row.operation);assert.ok(row.disposition);}
 for(const row of ledger.questions){assert.ok(row.targetPath);assert.ok(row.subjectScope);assert.ok(row.applicability);assert.ok(row.sourceControlId);}
});
test('CC policy controls retain all60 records and distinguish source fallback from live behavior',async()=>{
 const [ledger,inventory]=await Promise.all([read(ledgerPath),read('docs/design/control-inventory.json')]);
 const original=inventory.controls.filter(x=>x.method==='pCcPolicy');assert.equal(original.length,60);
 assert.deepEqual(ledger.policyControls.map(x=>x.controlId).sort(),original.map(x=>x.id).sort());
 assert.ok(ledger.policyControls.some(x=>x.disposition==='invalid-tab-fallback'));
 assert.equal(ledger.policyControls.find(x=>x.controlId==='CTL-94f70d0cd3fa').ownerPhase,9);
 assert.equal(ledger.policyControls.find(x=>x.controlId==='CTL-04e2a11eab51').ownerPhase,9);
 assert.equal(ledger.sourceSha256,inventory.sourceSha256);
});
test('CC branch catalogue includes independent readiness, referral and temporal capacity cases',async()=>{
 const ledger=await read(ledgerPath);const ids=new Set(ledger.branches.map(x=>x.id));
 for(const id of ['PR-04','PR-05','PR-08','PR-11','PR-12','PR-14','PR-17','PR-18','PR-21','AU-05','AU-06','LI-03','LI-05','LI-08','LI-11','LI-12','LI-15','LI-18','UW-18','UW-20','UW-30','CC-DISTRICT-CAPACITY'])assert.ok(ids.has(id),id);
 for(const row of ledger.branches){assert.ok(row.condition);assert.ok(row.ownerPlan);assert.ok(row.verification);}
});
test('CC policy display facts missing from capture receive explicit supplemental owners',async()=>{
 const ledger=await read(ledgerPath);assert.equal(ledger.policyDisplayItems.length,298);assert.equal(ledger.supplementalFields.length,7);
 for(const row of ledger.supplementalFields){assert.ok(row.targetPath);assert.match(row.ownerPlan,/^08-0[34]$/);assert.ok(row.reason);}
 assert.equal(ledger.policyDisplayItems.find(x=>x.source.label==='Outstanding balance').ownerPhase,10);
 assert.ok(ledger.policyDisplayItems.filter(x=>x.source.tabs.length===1&&x.source.tabs[0]==='Claims').every(x=>x.ownerPhase===9));
});
