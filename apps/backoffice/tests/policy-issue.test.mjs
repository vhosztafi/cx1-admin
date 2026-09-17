import test from 'node:test';
import assert from 'node:assert/strict';
import { issuePolicyCommand, sendPolicyIssue, policyCoverageLabel, quotedIssueAmount } from '../lib/policies-api.ts';
test('issue confirmation distinguishes net agency due from separate and direct collection with penny rounding', () => {
 const terms = { rating: { grossPayable: '1120.01', brokerCommission: '100.00', fee: '20.01' }, settlement: { collector: 'agency', mode: 'net-remittance', feeShareBps: 5000 } };
 assert.equal(quotedIssueAmount(terms), '1010.00');
 assert.equal(quotedIssueAmount({...terms, settlement: {...terms.settlement, mode: 'separate-payment'}}), '1120.01');
 assert.equal(quotedIssueAmount({...terms, settlement: {...terms.settlement, collector: 'mga'}}), '1120.01');
 assert.throws(() => quotedIssueAmount({...terms, rating: {...terms.rating, grossPayable: 'NaN'}}));
});
const id=n=>`aaaaaaaa-0000-4000-8000-${String(n).padStart(12,'0')}`;
const etag='"AAAAAAAAAAE="';
const input={cycleId:id(2),ratingId:id(3),acceptanceId:id(4),termsHash:'a'.repeat(64),assuranceHash:'b'.repeat(64),reason:'Issue reviewed and accepted cover'};
test('issue freezes the exact accepted context and refuses incomplete or open commands',()=>{
 const body={...input};const command=issuePolicyCommand(id(1),etag,body);body.acceptanceId=id(5);
 assert.deepEqual(JSON.parse(command.body),input);assert.ok(Object.isFrozen(command));
 for(const change of [{acceptanceId:''},{assuranceHash:'stale'},{reason:' '},{premium:'1.00'},{reason:'x'.repeat(1001)}])assert.throws(()=>issuePolicyCommand(id(1),etag,{...input,...change}));
 assert.throws(()=>issuePolicyCommand(id(1),'W/"old"',input));
});
test('issue retry keeps the key and verifies every returned identity against the quote',async()=>{
 const original=globalThis.fetch;const calls=[];const command=issuePolicyCommand(id(1),etag,input);
 const receipt={policyId:id(10),policyReference:'PL-MT-0000000001',quoteId:id(1),quoteEtag:etag,termId:id(11),versionId:id(12),transactionId:id(13),obligationId:id(14),documentRequestIds:[id(15),id(16),id(17)]};
 try{
  globalThis.fetch=async(url,options)=>{calls.push(options);return new Response(JSON.stringify(receipt),{status:201,headers:{'Content-Type':'application/json',ETag:etag}});};
  assert.equal((await sendPolicyIssue(command,'csrf')).policyId,id(10));assert.equal((await sendPolicyIssue(command,'csrf')).policyId,id(10));
  assert.equal(calls[0].body,calls[1].body);assert.equal(calls[0].headers['Idempotency-Key'],calls[1].headers['Idempotency-Key']);
  for(const change of [{quoteId:id(99)},{policyId:''},{documentRequestIds:[]},{documentRequestIds:[id(15),id(15),id(17)]},{quoteEtag:'"stale"'}]){
   globalThis.fetch=async()=>new Response(JSON.stringify({...receipt,...change}),{status:201,headers:{'Content-Type':'application/json',ETag:etag}});
   await assert.rejects(()=>sendPolicyIssue(command,'csrf'));
  }
 }finally{globalThis.fetch=original;}
});
test('coverage status uses the actual half-open term rather than claiming future cover is in force',()=>{
 const from='2026-10-01T00:00:00Z',to='2027-10-01T00:00:00Z';
 assert.equal(policyCoverageLabel(from,to,Date.parse(from)-1),'Inception scheduled');
 assert.equal(policyCoverageLabel(from,to,Date.parse(from)),'In force');
 assert.equal(policyCoverageLabel(from,to,Date.parse(to)),'Term ended');
});
