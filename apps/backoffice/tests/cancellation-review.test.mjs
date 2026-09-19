import test from 'node:test';
import assert from 'node:assert/strict';
import {formatCancellationMoney,cancellationCommand,sendCancellation} from '../lib/cancellation-review.ts';

test('cancellation credits retain signs and pennies without binary arithmetic',()=>{
 assert.equal(formatCancellationMoney('-326.96'),'−£326.96');
 assert.equal(formatCancellationMoney('64.11'),'£64.11');
 assert.equal(formatCancellationMoney('9999999999999.99'),'£9,999,999,999,999.99');
 assert.equal(formatCancellationMoney('0.00'),'£0.00');
 for(const value of ['1.001','NaN','--4.00','01.00'])assert.equal(formatCancellationMoney(value),'Unavailable');
});
test('cancellation retry retains exact version, lease, key and immutable body',async()=>{
 const command=cancellationCommand('aaaaaaaa-0000-4000-8000-000000000001','"AAAAAAAAAAE="','bbbbbbbb-0000-4000-8000-000000000001',
  'cancellation-preview',{previewHash:'a'.repeat(64)});
 assert.ok(Object.isFrozen(command));const prior=globalThis.fetch;const calls=[];
 globalThis.fetch=async(url,init)=>{
  if(String(url).endsWith('/csrf'))return new Response(JSON.stringify({requestToken:'csrf'}),{status:200});
  calls.push({url,headers:init.headers,body:init.body});
  return new Response(JSON.stringify({draftId:'aaaaaaaa-0000-4000-8000-000000000001',resourceId:'cccccccc-0000-4000-8000-000000000001',draftEtag:'"AAAAAAAAAAI="'}),{status:201,headers:{ETag:'"AAAAAAAAAAI="'}});
 };
 try{await sendCancellation(command);await sendCancellation(command);assert.deepEqual(calls[0],calls[1]);assert.equal(calls[0].headers['X-Edit-Lease'],command.fence);}
 finally{globalThis.fetch=prior;}
});
