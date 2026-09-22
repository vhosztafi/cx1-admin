import test from 'node:test';
import assert from 'node:assert/strict';
import {midRetryCommand,sendMidRetry} from '../lib/mid-api.ts';
const id=n=>`aaaaaaaa-0000-4000-8000-${String(n).padStart(12,'0')}`,etag='"AAAAAAAAAAA="';
test('MID retry retains the original job, actor, body, key and ETag after a lost result',async()=>{
 const command=midRetryCommand(id(1),id(2),id(3),etag,'Retry this exact vehicle submission'),old=globalThis.fetch,calls=[];
 try{globalThis.fetch=async(url,init)=>{calls.push({url,...init});if(calls.length===1)throw new TypeError('lost response');return new Response(JSON.stringify({id:id(2),kind:'mid-update',state:'pending'}));};await assert.rejects(sendMidRetry(command,'csrf',id(3)));await sendMidRetry(command,'csrf',id(3));assert.equal(calls[0].body,calls[1].body);assert.equal(calls[0].headers['Idempotency-Key'],calls[1].headers['Idempotency-Key']);assert.equal(calls[0].headers['If-Match'],etag);assert.ok(Object.isFrozen(command));await assert.rejects(sendMidRetry(command,'csrf',id(4)));assert.equal(calls.length,2);}finally{globalThis.fetch=old;}
});
test('another job or integration cannot confirm a MID retry',async()=>{
 const command=midRetryCommand(id(1),id(2),id(3),etag,'Retry same source'),old=globalThis.fetch;
 try{for(const value of[{id:id(9),kind:'mid-update',state:'pending'},{id:id(2),kind:'operational-claims',state:'pending'},{id:id(2),kind:'mid-update',state:'sent'}]){globalThis.fetch=async()=>new Response(JSON.stringify(value));await assert.rejects(sendMidRetry(command,'csrf',id(3)));}}finally{globalThis.fetch=old;}
});
