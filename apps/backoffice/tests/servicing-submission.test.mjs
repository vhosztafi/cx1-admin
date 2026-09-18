import test from 'node:test';
import assert from 'node:assert/strict';
import {proofCommand,sendProof} from '../lib/servicing-proof.ts';
import {recordedSubmission,recoverSubmission} from '../lib/servicing-submission.ts';
const scope={draftId:'10000000-0000-4000-8000-000000000001',cycleId:'10000000-0000-4000-8000-000000000002',revisionId:'10000000-0000-4000-8000-000000000003',fence:'10000000-0000-4000-8000-000000000004',etag:'"AAAAAAAAAAE="'};

test('submission snapshots the saved revision and rejects a mismatched revision',()=>{
 const body={cycleId:scope.cycleId,revisionId:scope.revisionId,reason:'Submit this fictional change'};
 const command=proofCommand(scope,'/submit',body);body.reason='Changed later';
 assert.equal(JSON.parse(command.body).reason,'Submit this fictional change');
 assert.throws(()=>proofCommand(scope,'/submit',{...body,revisionId:scope.draftId}));
});

test('persisted recovery binds the original cycle even when it is historical',()=>{
 const command=proofCommand(scope,'/submit',{cycleId:scope.cycleId,revisionId:scope.revisionId,reason:'Submit this fictional change'});
 const item={id:scope.fence,cycleId:scope.cycleId,revisionId:scope.revisionId,ratingId:scope.draftId,inputHash:'a'.repeat(64),reason:'Submitted by a different editor',submittedBy:scope.fence,submittedAt:'2026-09-18T04:00:00Z',applicable:false};
 const page={draftId:scope.draftId,draftEtag:'"AAAAAAAAAAI="',current:null,items:[item],nextCursor:null};
 assert.equal(recordedSubmission(command,page),item);
 assert.equal(recordedSubmission(command,{...page,items:[{...item,cycleId:scope.draftId}]}),null);
 assert.throws(()=>recordedSubmission(command,{...page,draftId:scope.fence}));
 assert.throws(()=>recordedSubmission(command,{...page,items:[{...item,id:'invalid'}]}));
 assert.throws(()=>recordedSubmission(command,{...page,draftEtag:'weak'}));
});

test('recovery traverses bounded history and retains uncertainty on a version change',async()=>{
 const command=proofCommand(scope,'/submit',{cycleId:scope.cycleId,revisionId:scope.revisionId,reason:'Submit this fictional change'});
 const previous=globalThis.fetch,urls=[];
 const page={draftId:scope.draftId,draftEtag:'"AAAAAAAAAAI="',current:null,items:[],nextCursor:null};
 globalThis.fetch=async url=>{urls.push(url);return Response.json({...page,nextCursor:urls.length===1?'signed-cursor':null});};
 try {assert.equal(await recoverSubmission(command),null);assert.equal(urls.length,2);assert.ok(urls[1].includes('cursor=signed-cursor'));
  let call=0;globalThis.fetch=async()=>Response.json({...page,draftEtag:++call===1?page.draftEtag:scope.etag,nextCursor:'next'});
  await assert.rejects(recoverSubmission(command),/history changed/);
 } finally {globalThis.fetch=previous;}
});

test('uncertain submission retry retains its key, body, draft version and lease',async()=>{
 const command=proofCommand(scope,'/submit',{cycleId:scope.cycleId,revisionId:scope.revisionId,reason:'Submit this fictional change'});
 const previous=globalThis.fetch,requests=[];
 globalThis.fetch=async(url,init)=>{
  if(url.endsWith('/csrf'))return Response.json({requestToken:'csrf'});
  requests.push(init);if(requests.length===1)throw new TypeError('Lost response');
  return Response.json({id:scope.fence,draftId:scope.draftId,cycleId:scope.cycleId,revisionId:scope.revisionId,draftEtag:'"AAAAAAAAAAI="'},{headers:{ETag:'"AAAAAAAAAAI="'}});
 };
 try {await assert.rejects(sendProof(command));await sendProof(command);assert.deepEqual(requests[0],requests[1]);}
 finally {globalThis.fetch=previous;}
});
