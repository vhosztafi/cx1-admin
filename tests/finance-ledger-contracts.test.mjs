import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';

const api=JSON.parse(readFileSync(new URL('../contracts/openapi.json',import.meta.url)));

test('finance ledger and transaction reads require finance authority while linked policy finance permits current policy scope',()=>{
 const ledger=api.paths['/finance/ledger'].get;
 const transaction=api.paths['/finance/transactions/{transactionId}'].get;
 const policy=api.paths['/policies/{policyId}/finance'].get;
 assert.equal(ledger['x-permission'],'finance-read');
 assert.equal(transaction['x-permission'],'finance-read');
 assert.match(policy['x-permission'],/policy-read/);
 assert.equal(ledger.parameters.find(x=>x.name==='agencyId').required,true);
 assert.equal(ledger['x-runtime-status'],'phase-10-02-implemented');
});

test('ledger contract exposes canonical signed decimal values and source dates without asserting payment',()=>{
 const row=api.components.schemas.FinanceLedgerRow;
 assert.deepEqual(row.properties.debtorDelta,api.components.schemas.FinanceAccountSummary.properties.agencyReceivable);
 assert.match(row.properties.debtorDelta.pattern,/\\\./);
 assert.equal(row.properties.postingDate.format,'date');
 assert.equal(row.properties.effectiveAt.format,'date-time');
 assert.ok(!row.properties.status.enum.includes('paid'));
 assert.ok(!row.properties.sourceKind.enum?.includes('allocation'));
 const types=readFileSync(new URL('../contracts/generated/finance.ts',import.meta.url),'utf8');
 assert.match(types,/debtorDelta: FinanceMoney/);
 assert.match(types,/postingDate: string/);
});
