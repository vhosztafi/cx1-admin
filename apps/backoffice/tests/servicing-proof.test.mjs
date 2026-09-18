import test from 'node:test';
import assert from 'node:assert/strict';
import { currentProof, proofCommand, confirmProofReceipt, sendProof, proofReadState } from '../lib/servicing-proof.ts';
const ids = Array.from({length: 6}, (_, n) => `10000000-0000-0000-0000-00000000000${n + 1}`);
const scope = { draftId: ids[0], cycleId: ids[1], revisionId: ids[2], fence: ids[3], etag: '"AAAAAAAAAAE="' };
const receipt = { id: ids[4], ...scope, draftEtag: '"AAAAAAAAAAI="' };
test('lease-version refresh retains same-page forms without authorizing stale writes',()=>{
 const data={items:[{id:ids[4]}]},read={url:'/owned-page',etag:scope.etag,data};
 assert.deepEqual(proofReadState(read,'/owned-page',receipt.draftEtag,''),{data,current:false});
 assert.deepEqual(proofReadState(read,'/different-page',scope.etag,''),{data:null,current:false});
 assert.deepEqual(proofReadState(read,'/owned-page',scope.etag,'unavailable'),{data,current:false});
 assert.deepEqual(proofReadState(read,'/owned-page',scope.etag,''),{data,current:true});
});
test('proof matching binds the exact cycle, revision, rating, purpose, target and fingerprint', () => {
  const requirement = { code:'photocard', riskItemId:ids[4], inputFingerprint:'a'.repeat(64), context:{...scope, ratingId:ids[5]} };
  const association = { ...requirement.context, ...requirement, withdrawn:false };
  assert.equal(currentProof(association, requirement), true);
  for (const field of ['cycleId','revisionId','ratingId','code','riskItemId','inputFingerprint']) assert.equal(currentProof({...association, [field]:'different'}, requirement), false, field);
  assert.equal(currentProof({...association, withdrawn:true}, requirement), false);
});
test('commands snapshot form and scope and reject foreign cycles and routes', () => {
  const input = {...scope}, body = { cycleId:scope.cycleId, reason:'Proof reviewed today' };
  const command = proofCommand(input, '/evidence', body);
  input.etag = 'changed'; body.reason = 'changed';
  assert.equal(command.scope.etag, scope.etag); assert.equal(JSON.parse(command.body).reason, 'Proof reviewed today');
  assert.ok(Object.isFrozen(command)); assert.ok(Object.isFrozen(command.scope));
  assert.throws(() => proofCommand(scope, '/evidence', {cycleId:ids[4]}));
  assert.throws(() => proofCommand(scope, '/evidence?other=draft', body));
  assert.throws(() => proofCommand({...scope, etag:'W/"AAAAAAAAAAE="'}, '/evidence', body));
});

test('carrier commands share immutable retry scope and reject foreign or unowned endpoints',()=>{
 const body={cycleId:scope.cycleId,caseEtag:scope.etag,body:'Fictional carrier request'};
 for(const path of ['/capacity',...['submissions','query-replies','chases','responses','assignment','actions'].map(kind=>`/capacity/${ids[4]}/${kind}`),`/capacity-conditions/${ids[5]}/resolutions`]) {
  const input={...body};const command=proofCommand(scope,path,input);input.body='Changed after transmission';
  assert.ok(Object.isFrozen(command));assert.equal(JSON.parse(command.body).body,body.body);assert.equal(JSON.parse(command.body).cycleId,scope.cycleId);
  assert.throws(()=>proofCommand(scope,path,{...body,cycleId:ids[5]}));
 }
 for(const path of [`/capacity/${ids[4]}/approve`,`/capacity/${ids[4]}/submissions?bypass=true`,`/quotes/${ids[4]}/capacity`])assert.throws(()=>proofCommand(scope,path,body));
});

test('capacity response proof also requires the exact submission identity',()=>{
  const requirement={code:'capacity-response',riskItemId:null,capacitySubmissionId:ids[4],inputFingerprint:'a'.repeat(64),context:{...scope,ratingId:ids[5]}};
  const association={...requirement.context,...requirement,withdrawn:false};
  assert.equal(currentProof(association,requirement),true);
  assert.equal(currentProof({...association,capacitySubmissionId:ids[3]},requirement),false);
  assert.equal(currentProof({...association,capacitySubmissionId:null},requirement),false);
});

test('signature proof cannot be reused for different terms even with the same visible purpose',()=>{
 const requirement={code:'signed-statement',riskItemId:null,termsVersionId:ids[4],inputFingerprint:'a'.repeat(64),context:{...scope,ratingId:ids[5]}};
 const association={...requirement.context,...requirement,withdrawn:false};
 assert.equal(currentProof(association,requirement),true);
 assert.equal(currentProof({...association,termsVersionId:ids[3]},requirement),false);
 assert.equal(currentProof({...association,termsVersionId:null},requirement),false);
});

test('terms retry retains the exact contract, recipients, acceptance proof and assurance',()=>{
 for(const path of ['/terms/prepare','/terms/send','/acceptances']) {
  const body={cycleId:scope.cycleId,termsVersionId:ids[4],assuranceHash:'a'.repeat(64),recipientContactIds:[ids[5]],evidenceAssociationId:ids[3]};
  const command=proofCommand(scope,path,body);body.termsVersionId=ids[2];body.assuranceHash='b'.repeat(64);body.recipientContactIds.push(ids[2]);
  assert.equal(JSON.parse(command.body).termsVersionId,ids[4]);assert.equal(JSON.parse(command.body).assuranceHash,'a'.repeat(64));
  assert.deepEqual(JSON.parse(command.body).recipientContactIds,[ids[5]]);assert.equal(command.scope.etag,scope.etag);
  assert.throws(()=>proofCommand(scope,path,{...body,cycleId:ids[5]}));
 }
});
test('only a receipt for the exact saved scope and strong matching ETag confirms success', () => {
  const command = proofCommand(scope, '/evidence', {cycleId:scope.cycleId});
  assert.equal(confirmProofReceipt(command, receipt, receipt.draftEtag), receipt);
  for (const field of ['draftId','cycleId','revisionId','draftEtag','id']) assert.throws(() => confirmProofReceipt(command, {...receipt, [field]:'foreign'}, receipt.draftEtag), field);
  assert.throws(() => confirmProofReceipt(command, receipt, null));
  assert.throws(() => confirmProofReceipt(command, receipt, 'W/' + receipt.draftEtag));
});
test('an uncertain upload retries the same immutable bytes, metadata, identity and fences', async () => {
  const file = new File(['fictional proof'], 'demo.txt', {type:'text/plain'});
  const command = proofCommand(scope, '/evidence/uploads', undefined, file);
  const requests = []; const original = globalThis.fetch;
  globalThis.fetch = async (url, init) => {
    if (url.endsWith('/csrf')) return Response.json({requestToken:'csrf'});
    requests.push(init);
    if (requests.length === 1) throw new TypeError('Connection lost after commit');
    return Response.json(receipt, {headers:{ETag:receipt.draftEtag}});
  };
  try {
    await assert.rejects(sendProof(command)); await sendProof(command);
    for (const request of requests) {
      assert.equal(request.headers['Idempotency-Key'], command.key); assert.equal(request.headers['If-Match'], scope.etag);
      assert.equal(request.headers['X-Edit-Lease'], scope.fence); assert.equal(request.headers['Content-Type'], undefined);
      assert.equal(await request.body.get('file').text(), 'fictional proof');
      assert.equal(request.body.get('fileName'), 'demo.txt'); assert.equal(request.body.get('contentType'), 'text/plain');
    }
  } finally { globalThis.fetch = original; }
});
