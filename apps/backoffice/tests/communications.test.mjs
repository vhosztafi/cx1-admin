import test from 'node:test';
import assert from 'node:assert/strict';
import {communicationCommand,sendCommunicationCommand,CommunicationError} from '../lib/communications-api.ts';
import {uncertainQuoteFailure} from '../lib/quotes.ts';
const id=n=>`aaaaaaaa-0000-4000-8000-${String(n).padStart(12,'0')}`;
const draft=()=>({body:'Fictional saved text',recipientContactIds:[id(2)],attachmentVersionIds:[id(3)]});
const etag='"AAAAAAAAAAA="';
const receipt=()=>({id:id(4),threadId:id(1),...draft(),state:'draft',etag,authorLabel:'Fictional author',createdAt:'2026-09-22T08:00:00Z'});
test('commands pin retry bytes and enforce closed audiences and limits',()=>{
 const input=draft(),command=communicationCommand('create-draft',id(1),input);input.body='Changed';input.attachmentVersionIds.push(id(5));
 assert.equal(JSON.parse(command.body).body,'Fictional saved text');assert.deepEqual(JSON.parse(command.body).attachmentVersionIds,[id(3)]);assert.ok(Object.isFrozen(command));
 for(const bad of [{visibility:'agency',subject:'Review'},{visibility:'internal',subject:'Review',relationshipId:id(2)},{visibility:'public',subject:'Review'}])assert.throws(()=>communicationCommand('thread',id(1),bad));
 assert.throws(()=>communicationCommand('note',id(1),{body:'  '}));assert.throws(()=>communicationCommand('note',id(1),{body:'Text',visibility:'agency'}));
 assert.throws(()=>communicationCommand('create-draft',id(1),{...draft(),recipientContactIds:[id(2),id(2)]}));
 assert.throws(()=>communicationCommand('create-draft',id(1),{...draft(),attachmentVersionIds:Array.from({length:21},(_,i)=>id(i+10))}));
 assert.throws(()=>communicationCommand('update-draft',id(4),draft()));
 assert.equal(JSON.parse(communicationCommand('create-draft',id(1),{body:'',recipientContactIds:[],attachmentVersionIds:[]}).body).body,'');
});
test('draft receipts must match exact parent body selections and version',async()=>{
 const original=globalThis.fetch,command=communicationCommand('create-draft',id(1),draft()),calls=[];
 try{
  globalThis.fetch=async(url,init)=>{calls.push(init);return new Response(JSON.stringify(receipt()),{headers:{ETag:etag}});};
  await sendCommunicationCommand(command,'csrf');await sendCommunicationCommand(command,'csrf');assert.equal(calls[0].body,calls[1].body);assert.equal(calls[0].headers['Idempotency-Key'],calls[1].headers['Idempotency-Key']);
  for(const change of [{threadId:id(8)},{body:'Other'},{attachmentVersionIds:[]},{state:'sent'},{etag:'"different"'}]){
   globalThis.fetch=async()=>new Response(JSON.stringify({...receipt(),...change}),{headers:{ETag:etag}});await assert.rejects(sendCommunicationCommand(command,'csrf'),uncertainQuoteFailure);
  }
 }finally{globalThis.fetch=original;}
});
test('note success cannot be borrowed from a different record',async()=>{
 const original=globalThis.fetch,command=communicationCommand('note',id(1),{body:'Internal text'});
 try{globalThis.fetch=async()=>new Response(JSON.stringify({id:id(3),subjectRecordId:id(8),body:'Internal text',authorLabel:'Author',createdAt:'2026-09-22T08:00:00Z'}));await assert.rejects(sendCommunicationCommand(command,'csrf'),uncertainQuoteFailure);}finally{globalThis.fetch=original;}
});
test('busy responses remain uncertain while stale versions require review',async()=>{
 const original=globalThis.fetch,command=communicationCommand('update-draft',id(4),draft(),etag);
 try{
  globalThis.fetch=async()=>new Response(JSON.stringify({code:'command-busy'}),{status:409});await assert.rejects(sendCommunicationCommand(command,'csrf'),uncertainQuoteFailure);
  globalThis.fetch=async()=>new Response('{}',{status:412});await assert.rejects(sendCommunicationCommand(command,'csrf'),error=>error instanceof CommunicationError&&!uncertainQuoteFailure(error));
 }finally{globalThis.fetch=original;}
});
