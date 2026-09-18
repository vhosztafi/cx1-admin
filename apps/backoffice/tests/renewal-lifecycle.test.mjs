import test from 'node:test';
import assert from 'node:assert/strict';
import {renewalLapseCommand,sendRenewalLapse} from '../lib/renewal-lifecycle.ts';
const ids=Array.from({length:4},(_,i)=>`10000000-0000-4000-8000-${String(i+1).padStart(12,'0')}`);
const etag='"AAAAAAAAAAE="',next='"AAAAAAAAAAI="';
const receipt={id:ids[0],policyId:ids[1],termId:ids[2],termEtag:next,effectiveAt:'2027-09-18T08:00:00Z',recordedAt:'2026-09-18T18:00:00Z',mode:'manual',notificationId:ids[3]};
test('lapse preserves the term version, reason and operation key after a lost response',async()=>{
 const command=renewalLapseCommand(ids[2],etag,'  Fictional customer declined renewal  ');assert.equal(Object.isFrozen(command),true);
 const original=globalThis.fetch,calls=[];
 globalThis.fetch=async(url,options)=>{if(url.endsWith('/csrf'))return Response.json({requestToken:'csrf'});calls.push(options);if(calls.length===1)throw new TypeError('Lost response');return Response.json(receipt,{status:201,headers:{ETag:next}});};
 try{await assert.rejects(sendRenewalLapse(command));assert.deepEqual(await sendRenewalLapse(command),receipt);}finally{globalThis.fetch=original;}
 assert.equal(calls[0].body,calls[1].body);assert.deepEqual(calls[0].headers,calls[1].headers);assert.equal(calls[1].headers['If-Match'],etag);
 assert.equal(JSON.parse(calls[1].body).reason,'Fictional customer declined renewal');
});
test('lapse rejects wrong-term or unverifiable successful responses',async()=>{
 const original=globalThis.fetch,command=renewalLapseCommand(ids[2],etag,'Fictional customer declined renewal');
 try{for(const change of [{termId:ids[1]},{notificationId:''},{termEtag:etag},{mode:'issued'},{effectiveAt:'not-a-date'}]){
  globalThis.fetch=async url=>url.endsWith('/csrf')?Response.json({requestToken:'csrf'}):Response.json({...receipt,...change},{status:201,headers:{ETag:next}});
  await assert.rejects(sendRenewalLapse(command));
 }}finally{globalThis.fetch=original;}
});
test('lapse requires a strong owned version and a bounded substantive reason',()=>{
 for(const reason of ['','short','line\nbreak in reason','x'.repeat(1001)])assert.throws(()=>renewalLapseCommand(ids[2],etag,reason));
 assert.throws(()=>renewalLapseCommand(ids[2],'W/'+etag,'Fictional decline reason'));
 assert.throws(()=>renewalLapseCommand('00000000-0000-0000-0000-000000000000',etag,'Fictional decline reason'));
});
