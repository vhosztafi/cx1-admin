import test from 'node:test';
import assert from 'node:assert/strict';
import {adjustmentIssueCommand,confirmAdjustmentIssue,sendAdjustmentIssue} from '../lib/servicing-issue.ts';
const ids=Array.from({length:16},(_,i)=>`10000000-0000-4000-8000-${String(i+1).padStart(12,'0')}`);
const scope={draftId:ids[0],cycleId:ids[1],revisionId:ids[2],fence:ids[3],etag:'"AAAAAAAAAAE="'};
const input={cycleId:ids[1],ratingId:ids[4],termsVersionId:ids[5],acceptanceId:ids[6],termsHash:'a'.repeat(64),assuranceHash:'b'.repeat(64),reason:'Issue accepted fictional changes'};
const receipt={policyId:ids[7],policyReference:'MT-TEST',draftId:ids[0],draftEtag:'"AAAAAAAAAAI="',termId:ids[8],transactionId:ids[9],decisionId:ids[10],
 versionId:ids[11],versionIds:[ids[11]],obligationId:ids[12],journalId:ids[13],accountingPeriodId:ids[14],postingDate:'2026-09-18',currency:'GBP',amountDue:'0.00',amountCredit:'20.00',netAmount:'-20.00',documentRequestIds:ids.slice(0,3),midIntentIds:[ids[15]],processedAt:'2026-09-18T12:00:00Z'};
test('issue freezes acceptance, price hashes and lease for an exact retry',async()=>{
 const original={...scope},body={...input},command=adjustmentIssueCommand(original,body);original.etag=receipt.draftEtag;body.termsHash='c'.repeat(64);
 const saved=globalThis.fetch,calls=[];
 globalThis.fetch=async(url,options)=>{if(url.endsWith('/csrf'))return Response.json({requestToken:'csrf'});calls.push({url,...options});return Response.json(receipt,{status:201,headers:{ETag:receipt.draftEtag}});};
 try{await sendAdjustmentIssue(command);await sendAdjustmentIssue(command);}finally{globalThis.fetch=saved;}
 assert.equal(calls.length,2);assert.equal(calls[0].body,calls[1].body);assert.equal(JSON.parse(calls[0].body).termsHash,input.termsHash);
 assert.deepEqual(calls[0].headers,calls[1].headers);assert.equal(calls[0].headers['If-Match'],scope.etag);
});
test('issue rejects wrong receipt owner, missing versions and incomplete side effects',()=>{
 const command=adjustmentIssueCommand(scope,input);assert.deepEqual(confirmAdjustmentIssue(command,receipt,receipt.draftEtag),receipt);
 for(const bad of [{draftId:ids[9]},{versionId:ids[3]},{versionIds:[]},{midIntentIds:[]},{documentRequestIds:[]},{amountCredit:'-20.00'},{draftEtag:scope.etag}])
  assert.throws(()=>confirmAdjustmentIssue(command,{...receipt,...bad},receipt.draftEtag));
 assert.throws(()=>adjustmentIssueCommand(scope,{...input,cycleId:ids[9]}));
});
