import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {randomUUID} from 'node:crypto';
import {commercialDemoRenewalInput,prepareCommercialDemoRenewal} from './commercial-demo-renewal.mjs';

function plus(value,pence){const [whole,fraction]=value.split('.');const amount=BigInt(whole)*100n+BigInt(fraction)+pence;return`${amount/100n}.${String(amount%100n).padStart(2,'0')}`;}

export const commercialDemoAdjustment=context=>ratedServicing({...context,kind:'adjustment'});
export const commercialDemoRenewal=context=>ratedServicing({...context,kind:'renewal'});

async function ratedServicing({policy,get,command,until,knownAt,directory,kind}){
 const prefix=kind,termPath=`/api/v1/terms/${policy.termId}/drafts`;
 const effective=new Date(Date.parse(policy.snapshot.term.startsAt)+14*86400000).toISOString().slice(0,10);
 const recorded=JSON.parse(await readFile(directory+'/commands.json','utf8')).commands[prefix+':create'];
 const createInput=kind==='renewal'?(recorded?undefined:await commercialDemoRenewalInput(policy,get)):
  {kind:'adjustment',baseVersionId:policy.versionId,commonEffectiveIntent:{localDate:effective,localTime:'00:00',timeZone:'Europe/London'},reason:'Fictional commercial demonstration stock adjustment'};
 const created=await command(prefix+':create',termPath,createInput,termPath);
 const route='/api/v1/drafts/'+created.id;
 let draft=(await get(route)).data,lease;
 let receipt;
 if(draft.state==='issued'){
  const saved=JSON.parse(await readFile(directory+'/commands.json','utf8')).commands[prefix+':issue'];
  assert.ok(saved,'The retained draft was issued outside this demo journal; review its history.');
  receipt=await command(prefix+':issue',route+'/issue',undefined,route);
 }else{
  const expired=draft.lease&&Date.parse(draft.lease.expiresAt)<=Date.parse(knownAt());
  const acquired=await command(prefix+(expired?':reacquire/'+draft.lease.generation:':lease'),route+'/lease',{mode:'acquire'},route);
  lease=acquired.lease.leaseToken;
  const write=(step,suffix,data,method='POST',file)=>command(prefix+':'+step,route+suffix,data,route,file,{method,lease});
  draft=(await get(route)).data;
  if(kind==='renewal')await prepareCommercialDemoRenewal({route,write,get});
  else{
   const proposal=structuredClone(draft.proposal),location=policy.snapshot.risk.locations[0];
   proposal.changes=[{changeId:randomUUID(),riskItemId:location.id,kind:'commercial-property',operation:'update',payload:{stock:plus(location.stock,11111n)}}];
   await write('proposal','/proposal',proposal,'PUT');
  }
  await write('rate','/rate',{revisionId:(await get(route)).data.revisionId,reason:`Rate the saved fictional commercial ${kind}`});
  await until(route+'/ratings',x=>x.current?.applicable);
  const uploaded=await write('upload','/evidence/uploads',undefined,'POST',{name:'fictional-commercial-servicing.txt',content:'Fictional commercial servicing evidence and customer acceptance for demonstration only.'});
  async function proof(purpose){
   assert.ok(purpose);const step=`proof:${purpose.code}:${purpose.riskItemId??'policy'}:${purpose.termsVersionId??'risk'}`;
   const association=await write(step+':attach','/evidence',{cycleId:purpose.context.cycleId,fileId:uploaded.id,requirementCode:purpose.code,inputFingerprint:purpose.inputFingerprint,
    ...(purpose.riskItemId?{riskItemId:purpose.riskItemId}:{}),reason:'Supply exact fictional commercial servicing proof'});
   const saved=(await get(route+`/evidence?cycleId=${purpose.context.cycleId}&pageSize=50`)).data.items.find(x=>x.id===association.id);assert.ok(saved);
   await write(step+':review',`/evidence/${saved.id}/reviews`,{cycleId:purpose.context.cycleId,associationEtag:saved.etag,outcome:'accepted',expectedFingerprint:purpose.inputFingerprint,reason:'Review exact fictional commercial servicing proof'});
   return saved.id;
  }
  for(const item of (await get(route+'/evidence/requirements')).data.requirements)if(!item.satisfied)await proof(item.requirement);
  const referrals=(await get(route+'/referrals')).data;assert.equal(referrals.nextCursor,null);
  if(referrals.items.length)await write('decisions','/referrals/decisions',{cycleId:referrals.cycleId,decisions:referrals.items.map(x=>({referralId:x.id,etag:x.etag,outcome:'approve',reason:'Approve the reviewed fictional adjustment within current authority'}))});
  let terms=(await get(route+'/terms')).data;assert.ok(terms.templates.length);
  const prepared=await write('prepare','/terms/prepare',{cycleId:terms.cycleId,ratingId:terms.ratingId,templateVersionId:terms.templates[0].id});
  await proof((await get(route+'/evidence/requirements')).data.requirements.find(x=>x.requirement.code==='signed-statement'&&x.requirement.termsVersionId===prepared.id)?.requirement);
  terms=(await get(route+'/terms')).data;assert.ok(terms.recipientOptions.length);
  await write('send','/terms/send',{cycleId:terms.cycleId,termsVersionId:terms.terms.id,recipientContactIds:[terms.recipientOptions[0].id]});
  await until(route+'/terms',x=>x.delivery?.state==='delivered');
  const proofId=await proof((await get(route+'/evidence/requirements')).data.requirements.find(x=>x.requirement.code==='acceptance-proof')?.requirement);
  terms=(await get(route+'/terms')).data;
  await write('accept','/acceptances',{cycleId:terms.cycleId,ratingId:terms.ratingId,termsVersionId:terms.terms.id,deliveryId:terms.delivery.id,termsHash:terms.terms.termsHash,assuranceHash:terms.assuranceHash,
   accepterLabel:'Fictional commercial demonstration customer',acceptedAt:knownAt(),channel:'written',evidenceAssociationId:proofId});
  terms=(await get(route+'/terms')).data;assert.equal(terms.acceptanceApplicable,true);
  receipt=await write('issue','/issue',{cycleId:terms.cycleId,ratingId:terms.ratingId,termsVersionId:terms.terms.id,acceptanceId:terms.acceptance.id,termsHash:terms.terms.termsHash,assuranceHash:terms.assuranceHash,reason:`Issue the accepted fictional commercial demonstration ${kind}`});
 }
 assert.deepEqual(receipt.midIntentIds,[]);
 const issued=(await get(`/api/v1/policies/${policy.id}/terms/${receipt.termId}/versions/${receipt.versionIds.at(-1)}`)).data;
 assert.equal(issued.snapshot.risk.locations[0].stock,kind==='renewal'?policy.snapshot.risk.locations[0].stock:plus(policy.snapshot.risk.locations[0].stock,11111n));
 const original=(await get(`/api/v1/policies/${policy.id}/terms/${policy.termId}/versions/${policy.versionId}`)).data;
 assert.equal(original.contentHash,policy.contentHash);assert.deepEqual(original.snapshot,policy.snapshot);
 await writeFile(directory+`/${kind}.json`,JSON.stringify({draftId:created.id,receipt,issued},null,2));return issued;
}
