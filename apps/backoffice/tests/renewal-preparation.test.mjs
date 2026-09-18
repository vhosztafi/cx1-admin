import test from 'node:test';
import assert from 'node:assert/strict';
import {renewalCommand,confirmRenewalReceipt,sendRenewal,renewalExperienceDisplay} from '../lib/renewal-preparation.ts';
const draftId='10000000-0000-0000-0000-000000000001',fence='10000000-0000-0000-0000-000000000002';
const resourceId='10000000-0000-0000-0000-000000000003',etag='"AAAAAAAAAAE="';
const scope={draftId,fence,etag};
const receipt={draftId,resourceId,kind:'renewal',updatedAt:'2026-09-18T16:00:00Z'};

test('pre-rating renewal commands retain exact scope and cannot target another route',()=>{
 const input={termMonths:6};const command=renewalCommand(scope,'/preparation',input);input.termMonths=12;
 assert.ok(Object.isFrozen(command));assert.ok(Object.isFrozen(command.scope));
 assert.equal(JSON.parse(command.body).termMonths,6);assert.equal(command.method,'POST');
 assert.equal(renewalCommand(scope,'/experience',{paid:'0.00'}).method,'PUT');
 for(const path of ['/rate','/preparation?approved=true','/experience/foreign/reviews','/experience/uploads/extra'])assert.throws(()=>renewalCommand(scope,path,input));
 assert.throws(()=>renewalCommand({...scope,etag:'W/'+etag},'/preparation',input));
});
test('only an exact owned renewal receipt and strong response fence confirm a mutation',()=>{
 const command=renewalCommand(scope,'/preparation',{termMonths:12});
 assert.equal(confirmRenewalReceipt(command,receipt,etag),receipt);
 for(const change of [{draftId:fence},{resourceId:'invalid'},{kind:'adjustment'},{updatedAt:'invalid'}])
  assert.throws(()=>confirmRenewalReceipt(command,{...receipt,...change},etag));
 assert.throws(()=>confirmRenewalReceipt(command,receipt,null));
});
test('uncertain experience upload repeats immutable bytes, key, lease and draft version',async()=>{
 const file=new File(['fictional claims evidence'],'claims.txt',{type:'text/plain'});
 const command=renewalCommand(scope,'/experience/uploads',undefined,file);const requests=[];const original=globalThis.fetch;
 globalThis.fetch=async(url,init)=>{if(url.endsWith('/csrf'))return Response.json({requestToken:'csrf'});requests.push(init);
  if(requests.length===1)throw new TypeError('Response lost after commit');return Response.json(receipt,{headers:{ETag:etag}});};
 try {await assert.rejects(sendRenewal(command));await sendRenewal(command);
  for(const request of requests){assert.equal(request.headers['Idempotency-Key'],command.key);assert.equal(request.headers['If-Match'],etag);
   assert.equal(request.headers['X-Edit-Lease'],fence);assert.equal(await request.body.get('file').text(),'fictional claims evidence');}
 } finally {globalThis.fetch=original;}
});
test('missing unreviewed zero-denominator and stale experience never display a zero loss ratio',()=>{
 const experience={id:resourceId,paid:'0.00',outstanding:'0.00',earnedPremium:'1000.00'};
 const reviewed={experience,review:{experienceVersionId:resourceId,outcome:'accepted'}};
 assert.equal(renewalExperienceDisplay({experience:null,review:null},true).ratio,null);
 assert.equal(renewalExperienceDisplay({experience,review:null},true).ratio,null);
 assert.equal(renewalExperienceDisplay({...reviewed,experience:{...experience,earnedPremium:'0.00'}},true).ratio,null);
 assert.equal(renewalExperienceDisplay(reviewed,false).ratio,null);
 assert.equal(renewalExperienceDisplay({...reviewed,review:{...reviewed.review,experienceVersionId:fence}},true).ratio,null);
 assert.equal(renewalExperienceDisplay(reviewed,true).ratio,0);
 assert.equal(renewalExperienceDisplay({...reviewed,experience:{...experience,paid:'600.00'}},true).ratio,0.6);
});
