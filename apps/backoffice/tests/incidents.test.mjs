import test from 'node:test';
import assert from 'node:assert/strict';
import {incidentCommand,sendIncidentCommand,IncidentError} from '../lib/incidents-api.ts';
const id=n=>`aaaaaaaa-0000-4000-8000-${String(n).padStart(12,'0')}`,etag='"AAAAAAAAAAA="';
const draft=()=>({policyId:id(1),productCode:'motor-trade-road-risks',description:'Fictional observed vehicle damage',thirdPartyInvolvement:'unknown'});
const receipt=()=>({id:id(2),reference:'INC-0000001',revisionId:id(3),draft:draft(),state:'draft',missing:['occurrence'],createdAt:'2026-09-22T08:00:00Z',updatedAt:'2026-09-22T08:00:00Z'});
test('incident retry pins original bytes and version despite later edits',async()=>{
 const input=draft(),command=incidentCommand('update',id(2),input,etag,id(5));input.description='Changed locally';assert.equal(JSON.parse(command.body).description,draft().description);assert.ok(Object.isFrozen(command));
 assert.throws(()=>incidentCommand('update',id(2),input,undefined,id(5)));const calls=[],original=globalThis.fetch;
 try{globalThis.fetch=async(url,init)=>{calls.push(init);return new Response(JSON.stringify(receipt()),{headers:{ETag:etag}});};await sendIncidentCommand(command,'csrf',id(5));await sendIncidentCommand(command,'csrf',id(5));assert.equal(calls[0].body,calls[1].body);assert.equal(calls[0].headers['If-Match'],etag);assert.equal(calls[0].headers['Idempotency-Key'],calls[1].headers['Idempotency-Key']);}finally{globalThis.fetch=original;}
});
test('incident save only confirms the exact report and original policy',async()=>{
 const original=globalThis.fetch,command=incidentCommand('create',id(1),draft(),undefined,id(5));
 try{for(const changed of [{draft:{...draft(),description:'Unrelated report'}},{draft:{...draft(),policyId:id(8)}},{state:'handed-off'},{revisionId:'invalid'}]){
  globalThis.fetch=async()=>new Response(JSON.stringify({...receipt(),...changed}),{headers:{ETag:etag}});await assert.rejects(sendIncidentCommand(command,'csrf',id(5)));
 }}finally{globalThis.fetch=original;}
});
test('stale incident save has a definite failure and retains the command input',async()=>{
 const command=incidentCommand('update',id(2),draft(),etag,id(5)),original=globalThis.fetch;
 try{globalThis.fetch=async()=>new Response('{}',{status:412});await assert.rejects(sendIncidentCommand(command,'csrf',id(5)),error=>error instanceof IncidentError&&error.status===412&&error.message.includes('retained'));assert.equal(JSON.parse(command.body).description,draft().description);}finally{globalThis.fetch=original;}
});
test('an unconfirmed command cannot be retried under another signed-in actor',async()=>{
 const command=incidentCommand('create',id(1),draft(),undefined,id(5)),original=globalThis.fetch;let requests=0;
 try{globalThis.fetch=async()=>{requests++;return new Response(JSON.stringify(receipt()),{headers:{ETag:etag}});};await assert.rejects(sendIncidentCommand(command,'csrf',id(6)));assert.equal(requests,0);}finally{globalThis.fetch=original;}
});
