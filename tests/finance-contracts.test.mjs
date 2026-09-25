import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';

const contract=readFileSync('docs/design/FINANCE-CONTRACTS.md','utf8');
const research=readFileSync('.planning/phases/10-accounting-and-insurer-reporting/10-RESEARCH.md','utf8');
const plans=Array.from({length:13},(_,i)=>readFileSync(`.planning/phases/10-accounting-and-insurer-reporting/10-${String(i+1).padStart(2,'0')}-PLAN.md`,'utf8'));

test('finance contract keeps insurance, cash and date bases distinct',()=>{
  for(const needle of ['IssueFinancialObligation','JournalLine','source component','PostedAt','Europe/London','AccountingPeriods.HoldAsync','from-inclusive/to-exclusive','effective date','posting date','decimal(15,2)','integer pence']){
    assert.ok(contract.includes(needle),`Missing ${needle}`);
  }
  assert.match(contract,/opening\s*=.*before from/);
  assert.match(contract,/closing\s*=\s*opening\s*\+\s*period/);
  assert.match(contract,/never.*recalculated from current rates/i);
});

test('allocation, refund and adapter contracts state the risky counterexamples',()=>{
  for(const needle of ['two', 'reversal','residual','unpaid invoice 100/credit 100','paid 100/credit 30','paid 10/credit 100','reservation','requester cannot approve own request','provider outcome independently','Timeout after success','same operation','No real transfer']){
    assert.ok(contract.toLowerCase().includes(needle.toLowerCase()),`Missing ${needle}`);
  }
  assert.match(contract,/equal date\/reference\/amount with a different key is a distinct/);
  assert.match(contract,/no synthetic reconciliation posting is created/);
});

test('bordereau and closure contracts have immutable version and negative gates',()=>{
  for(const needle of ['source membership','exact version','zero unresolved row failures','exact version/hash','CSV','formula','closed period','new posting','replay','scope']){
    assert.ok(contract.toLowerCase().includes(needle.toLowerCase()),`Missing ${needle}`);
  }
  assert.ok(research.includes('## Validation Architecture'));
  for(const req of ['FIN-01','FIN-02','FIN-03','FIN-04','FIN-05','FIN-06','FIN-07','FIN-08']){
    assert.ok(plans.some(x=>x.includes(req)),`Missing plan coverage for ${req}`);
  }
});
