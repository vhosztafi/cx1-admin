import test from 'node:test';
import assert from 'node:assert/strict';
import { taskCommand, taskBulkCommand, sendTaskCommand, taskTypes } from '../lib/tasks-api.ts';
import { uncertainQuoteFailure } from '../lib/quotes.ts';
const id=n=>`aaaaaaaa-0000-4000-8000-${String(n).padStart(12,'0')}`;
const etag='"AAAAAAAAAAE="';

test('task command freezes user intent and retains source types',()=>{
 const body={typeCode:'complaint',title:'Review',priority:'normal',assignment:{kind:'unassigned'}};
 const command=taskCommand('create',id(1),null,body);
 body.title='Changed';assert.equal(JSON.parse(command.body).title,'Review');assert.ok(Object.isFrozen(command));
 assert.equal(JSON.parse(command.body).subjectRecordId,id(1));
 assert.ok(taskTypes.some(x=>x[0]==='agency-onboarding'));
 assert.throws(()=>taskCommand('update',id(1),'W/"stale"',body));
 assert.throws(()=>taskCommand('create',id(1),null,{...body,createdBy:id(2)}));
 assert.throws(()=>taskCommand('transition',id(1),etag,{state:'completed',reason:'   '}));
});

test('bulk selection pins each displayed version and refuses duplicate or oversized selections',()=>{
 const selected=[{id:id(1),etag}];const command=taskBulkCommand('complete',selected,{reason:'Reviewed'});
 selected[0].etag='"AAAAAAAAAAI="';assert.equal(JSON.parse(command.body).tasks[0].etag,etag);
 assert.throws(()=>taskBulkCommand('complete',[{id:id(1),etag},{id:id(1),etag:'"AAAAAAAAAAI="'}],{reason:'Reviewed'}));
 assert.throws(()=>taskBulkCommand('complete',Array.from({length:101},(_,i)=>({id:id(i+1),etag})),{reason:'Reviewed'}));
 assert.throws(()=>taskBulkCommand('due',[],{reason:'Reviewed',dueOn:'2026-10-01'}));
});

test('uncertain task retry sends identical bytes and key and rejects mismatched task receipts',async()=>{
 const original=globalThis.fetch,calls=[];const command=taskCommand('transition',id(1),etag,{state:'completed',reason:'Reviewed'});
 try{
  globalThis.fetch=async(url,init)=>{calls.push([url,init]);return new Response(JSON.stringify({id:id(1),etag,state:'completed'}),{headers:{ETag:etag}});};
  await sendTaskCommand(command,'csrf');await sendTaskCommand(command,'csrf');
  assert.equal(calls[0][1].body,calls[1][1].body);assert.equal(calls[0][1].headers['Idempotency-Key'],calls[1][1].headers['Idempotency-Key']);
  globalThis.fetch=async()=>new Response(JSON.stringify({id:id(2),etag,state:'completed'}),{headers:{ETag:etag}});
  await assert.rejects(()=>sendTaskCommand(command,'csrf'),error=>uncertainQuoteFailure(error));
 }finally{globalThis.fetch=original;}
});

test('bulk receipt must acknowledge exactly the selected identities',async()=>{
 const original=globalThis.fetch,command=taskBulkCommand('complete',[{id:id(1),etag},{id:id(2),etag}],{reason:'Reviewed'});
 try{
  for(const updatedIds of [[id(1)],[id(1),id(1)],[id(1),id(3)]]){
   globalThis.fetch=async()=>new Response(JSON.stringify({updatedIds}));await assert.rejects(()=>sendTaskCommand(command,'csrf'));
  }
  globalThis.fetch=async()=>new Response(JSON.stringify({updatedIds:[id(2),id(1)]}));assert.deepEqual((await sendTaskCommand(command,'csrf')).updatedIds,[id(2),id(1)]);
 }finally{globalThis.fetch=original;}
});

test('busy task commands stay uncertain and server diagnostics never become UI copy',async()=>{
 const original=globalThis.fetch,command=taskCommand('transition',id(1),etag,{state:'completed',reason:'Reviewed'});
 try{
  globalThis.fetch=async()=>new Response(JSON.stringify({code:'command-busy',detail:'private diagnostic'}),{status:409});
  await assert.rejects(()=>sendTaskCommand(command,'csrf'),error=>uncertainQuoteFailure(error)&&!error.message.includes('private'));
  globalThis.fetch=async()=>new Response(JSON.stringify({code:'stale-task',detail:'private diagnostic'}),{status:412});
  await assert.rejects(()=>sendTaskCommand(command,'csrf'),error=>!uncertainQuoteFailure(error)&&error.message.includes('record changed')&&!error.message.includes('private'));
 }finally{globalThis.fetch=original;}
 assert.throws(()=>taskBulkCommand('unknown',[{id:id(1),etag}],{reason:'Reviewed'}));
 assert.throws(()=>taskCommand('unknown',id(1),etag,{kind:'agency'}));
});
