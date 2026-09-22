import test from 'node:test';
import assert from 'node:assert/strict';
import {claimsCommand,sendClaimsCommand} from '../lib/claims-api.ts';
const id=n=>`aaaaaaaa-0000-4000-8000-${String(n).padStart(12,'0')}`,etag='"AAAAAAAAAAA="';
test('claims retries preserve exact revision, actor, key and ETag',async()=>{
 const body={revisionId:id(2),resolutionId:id(3),providerId:id(4)},command=claimsCommand('log-and-handoff',id(1),id(5),etag,body);body.revisionId=id(9);assert.equal(JSON.parse(command.body).revisionId,id(2));assert.ok(Object.isFrozen(command));
 const old=globalThis.fetch,calls=[];try{globalThis.fetch=async(url,init)=>{calls.push(init);return new Response(JSON.stringify({id:id(6),kind:'operational-claims',state:'pending'}));};await sendClaimsCommand(command,'csrf',id(5));await sendClaimsCommand(command,'csrf',id(5));assert.equal(calls[0].headers['Idempotency-Key'],calls[1].headers['Idempotency-Key']);assert.equal(calls[0].headers['If-Match'],etag);await assert.rejects(sendClaimsCommand(command,'csrf',id(7)));assert.equal(calls.length,2);}finally{globalThis.fetch=old;}
});
test('unrelated jobs cannot confirm claims submissions',async()=>{
 const command=claimsCommand('refresh',id(1),id(5),etag,{}),old=globalThis.fetch;
 try{for(const job of[{id:id(6),kind:'operational-delivery',state:'pending'},{id:'invalid',kind:'operational-claims',state:'pending'},{id:id(6),kind:'operational-claims',state:'sent'}]){globalThis.fetch=async()=>new Response(JSON.stringify(job));await assert.rejects(sendClaimsCommand(command,'csrf',id(5)));}}finally{globalThis.fetch=old;}
});
