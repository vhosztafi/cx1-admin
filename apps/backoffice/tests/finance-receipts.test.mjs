import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import {minorUnits, decimalMoney, moneyLabel, combinedBalance, invoiceCandidates, receiptCommand, sendFinanceCommand, receiptPaymentState,
  statementEquation, FinanceCommandError} from '../lib/finance-api.ts';

const id=n=>`aaaaaaaa-0000-4000-8000-${String(n).padStart(12,'0')}`;
const receipt={id:id(1),agencyId:id(2),assignmentId:id(3),amount:'100.00',residual:'70.00',
  payerKind:'agency',payerId:id(2),allocations:[]};

test('money uses exact pence and posting alone never means paid',()=>{
 assert.equal(minorUnits('-123456.07'),-12345607);
 assert.equal(decimalMoney(-12345607),'-123456.07');
 assert.throws(()=>minorUnits('1.2'));assert.throws(()=>minorUnits('1e3'));
 assert.equal(receiptPaymentState({amountDue:'20.00',allocated:'0.00'}),'Unpaid');
 assert.equal(receiptPaymentState({amountDue:'20.00',allocated:'10.00'}),'Part paid');
 assert.equal(receiptPaymentState({amountDue:'20.00',allocated:'20.00'}),'Paid');
 assert.deepEqual(statementEquation({opening:'10.00',debits:'20.00',credits:'5.00',closing:'25.00'}),
  {opening:1000,debits:2000,credits:500,closing:2500,balanced:true});
 assert.equal(moneyLabel('90071992547409.91'),'£90,071,992,547,409.91');
 assert.deepEqual(combinedBalance('90071992547409.91','90071992547409.91'),
  {label:'£180,143,985,094,819.82',credit:false});
 assert.equal(statementEquation({opening:'90071992547409.91',debits:'1.00',credits:'1.00',closing:'90071992547409.91'}).balanced,true);
 const movement={sourceKind:'insurance',transactionId:id(8),policyId:id(9),debtorKind:'agency',debtorId:id(2),
  debtorDelta:'90071992547409.91',dueDate:null};
 assert.throws(()=>invoiceCandidates([movement,{...movement,sourceKind:'correction'}]));
});

test('receipt commands pin saved IDs, assignment ETag, exact amount and key',()=>{
 const command=receiptCommand('allocate',receipt,{invoiceId:id(4),amount:'30.00'},'same-command-key-0001');
 assert.equal(command.url,`/api/v1/finance/receipts/${id(1)}/allocations`);
 assert.equal(command.etag,`"${id(3).replaceAll('-','')}"`);
 assert.equal(command.key,'same-command-key-0001');
 assert.deepEqual(JSON.parse(command.body),{items:[{invoiceId:id(4),amount:'30.00'}]});
 assert.throws(()=>receiptCommand('allocate',receipt,{invoiceId:id(4),amount:'70.01'},'same-command-key-0002'));
 assert.throws(()=>receiptCommand('allocate',{...receipt,payerKind:'unidentified'},
  {invoiceId:id(4),amount:'1.00'},'same-command-key-0003'));
});

test('uncertain response retries identical bytes and key; stale conflict retains draft',async()=>{
 const original=globalThis.fetch,calls=[];
 const command=receiptCommand('allocate',receipt,{invoiceId:id(4),amount:'30.00'},'same-command-key-0004');
 try{
  globalThis.fetch=async(url,init)=>{calls.push([url,init]);throw new TypeError('lost response');};
  await assert.rejects(()=>sendFinanceCommand(command,'csrf'),e=>e instanceof FinanceCommandError&&e.uncertain);
  globalThis.fetch=async(url,init)=>{calls.push([url,init]);return Response.json({receiptId:id(1),allocationIds:[id(5)]});};
  await sendFinanceCommand(command,'csrf');
  assert.equal(calls[0][1].body,calls[1][1].body);
  assert.equal(calls[0][1].headers['Idempotency-Key'],calls[1][1].headers['Idempotency-Key']);
  globalThis.fetch=async()=>Response.json({code:'receipt-version-stale',detail:'private'}, {status:412});
  await assert.rejects(()=>sendFinanceCommand(command,'csrf'),e=>e instanceof FinanceCommandError&&
   e.conflict&&!e.uncertain&&!e.message.includes('private'));
 }finally{globalThis.fetch=original;}
});

test('saved GUID response casing is accepted for exact receipt and reversal identity',async()=>{
 const original=globalThis.fetch;
 try{
  const command=receiptCommand('reverse',receipt,{allocationId:id(5).toUpperCase(),reason:'Verified reversal reason'},'same-command-key-0005');
  globalThis.fetch=async()=>Response.json({receiptId:id(1).toUpperCase(),allocationId:id(5),reversalId:id(6)});
  const saved=await sendFinanceCommand(command,'csrf');
  assert.equal(saved.reversalId,id(6));
 }finally{globalThis.fetch=original;}
});

test('accounting views contain real saved records and accessible recovery controls',()=>{
 const receiptView=readFileSync(new URL('../components/finance/receipts.tsx',import.meta.url),'utf8');
 const accountView=readFileSync(new URL('../components/finance/accounts.tsx',import.meta.url),'utf8');
 for(const phrase of ['Assign payer','Allocate amount','Reverse allocation','Retry same action','Review saved version'])
  assert.ok(receiptView.includes(phrase),phrase);
 for(const phrase of ['Opening','Debits','Credits','Closing','Download statement'])
  assert.ok(accountView.includes(phrase),phrase);
 assert.ok(!receiptView.includes('Math.random()'));
 assert.ok(!accountView.includes('Math.random()'));
 const receiptType=readFileSync(new URL('../../../contracts/generated/finance-receipts.ts',import.meta.url),'utf8');
 assert.match(receiptType,/invoiceId: string/);
});
