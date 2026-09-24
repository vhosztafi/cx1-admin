import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {periodCloseCommand,correctionCommand,routeForFinanceTab,financeCommand,exactSum} from '../lib/finance-workspace-api.ts';

const id=n=>`aaaaaaaa-0000-4000-8000-${String(n).padStart(12,'0')}`;
const source=path=>readFileSync(new URL(path,import.meta.url),'utf8');

test('seven accounting tabs retain selected agency and saved record IDs',()=>{
 const tabs=['overview','transactions','accounts','payments','reconciliation','bordereaux','refunds'];
 for(const tab of tabs){
  const url=new URL(routeForFinanceTab(tab,{agencyId:id(1),batchId:id(2)}),'http://local');
  assert.equal(url.pathname,'/accounting');assert.equal(url.searchParams.get('tab'),tab);
  assert.equal(url.searchParams.get('agencyId'),id(1));
 }
 assert.equal(new URL(routeForFinanceTab('bordereaux',{batchId:id(2)}),'http://local').searchParams.get('batchId'),id(2));
 assert.throws(()=>routeForFinanceTab('payments',{agencyId:'foreign'}));
});

test('close and correction commands pin exact saved source, ETag, reason and stable key',()=>{
 const period={id:id(3),etag:'"AQIDBAUGBwg="'};
 const close=periodCloseCommand(period,'Reviewed bank and reporting evidence','stable-close-key-001');
 assert.equal(close.url,`/api/v1/finance/periods/${period.id}/close`);
 assert.equal(close.etag,period.etag);assert.equal(close.key,'stable-close-key-001');
 assert.deepEqual(JSON.parse(close.body),{reason:'Reviewed bank and reporting evidence'});
 const correction=correctionCommand('insurance',id(4),{
  debtorDelta:'-15.20',providerDelta:'10.00',cashDelta:'25.20',internalDelta:'0.00',
  effectiveAt:'2026-09-24T12:00:00.0000000+00:00',reason:'Correct posted historical classification'
 },'stable-correction-001');
 assert.equal(correction.url,'/api/v1/finance/corrections');
 assert.equal(JSON.parse(correction.body).originalSourceId,id(4));
 assert.throws(()=>correctionCommand('insurance',id(4),{
  debtorDelta:'1.00',providerDelta:'0.00',cashDelta:'0.00',internalDelta:'0.00',
  effectiveAt:'2026-09-24T12:00:00.0000000+00:00',reason:'Unbalanced historical movement'
 }));
});

test('each source tab has a live saved-record component and recovery controls',()=>{
 const workspace=source('../components/finance/workspace.tsx');
 for(const label of ['Overview','Transactions','Broker accounts','Payments','Reconciliation','Bordereaux','Refunds'])
  assert.ok(workspace.includes(label),label);
 for(const file of ['overview','transactions','reconciliation','bordereaux','refunds']){
  const view=source(`../components/finance/${file}.tsx`);
  assert.ok(view.includes('financeFetch')||view.includes('financeRequest'),file);
  assert.ok(!view.includes('Math.random()'),file);
 }
 assert.match(source('../components/finance/bordereaux.tsx'),/Correct mapping/);
 assert.match(source('../components/finance/bordereaux.tsx'),/Exclude row/);
 assert.match(source('../components/finance/refunds.tsx'),/Awaiting confirmation/);
});

test('exact aggregate remains penny-correct beyond safe Number addition and empty-body commands stay empty',()=>{
 assert.equal(exactSum(['90071992547409.91','0.01']),'£90,071,992,547,409.92');
 assert.equal(exactSum(['-12.01','2.00']),'-£10.01');
 assert.equal(financeCommand(`/api/v1/finance/bordereaux/${id(5)}/validations`,null).body,'');
});

test('every current finance source control and implied obligation has a recorded binding',()=>{
 const inventory=JSON.parse(source('../../../docs/design/finance-source-ledger.json'));
 const bindings=source('../../../.planning/phases/10-accounting-and-insurer-reporting/10-12-BINDINGS.md');
 const expected=[...inventory.directControls,...inventory.inheritedControls,...inventory.impliedObligations].map(item=>item.id);
 assert.equal(expected.length,38);
 const recorded=[...bindings.matchAll(/^\| (CTL-[a-z0-9]+|FIN-IMPLIED-[a-z0-9-]+) \|/gm)].map(match=>match[1]);
 assert.deepEqual(new Set(recorded),new Set(expected));assert.equal(recorded.length,expected.length);
});
