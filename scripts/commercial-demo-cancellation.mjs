import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';

export async function commercialDemoCancellation({policy,get,command,until,knownAt,directory}){
 const prefix='cancellation',termPath=`/api/v1/terms/${policy.termId}/drafts`;
 const date=new Date(Date.parse(policy.snapshot.term.startsAt)+30*86400000).toISOString().slice(0,10);
 const created=await command(prefix+':create',termPath,{kind:'cancellation',baseVersionId:policy.versionId,
  commonEffectiveIntent:{localDate:date,localTime:'09:00',timeZone:'Europe/London'},reason:'Fictional insured request to cancel the commercial renewal term'},termPath);
 const route='/api/v1/drafts/'+created.id;
 const draft=(await get(route)).data;
 if(draft.state!=='issued'){
  const expired=draft.lease&&Date.parse(draft.lease.expiresAt)<=Date.parse(knownAt());
  const acquired=await command(prefix+(expired?':reacquire/'+draft.lease.generation:':lease'),route+'/lease',{mode:'acquire'},route);
  const lease=acquired.lease.leaseToken;
  const write=(step,suffix,data,method='POST',file)=>command(prefix+':'+step,route+suffix,data,route,file,{method,lease});
  const proposal=structuredClone((await get(route)).data.proposal);proposal.cancellationReasonCode='insured-request';
  await write('proposal','/proposal',proposal,'PUT');
  const uploaded=await write('upload','/cancellation-evidence/uploads',undefined,'POST',{name:'fictional-commercial-cancellation.txt',purpose:'cancellation-request',content:'Fictional insured cancellation request for this exact commercial term and effective date. No cash refund or real insurer instruction is represented.'});
  await write('review',`/cancellation-evidence/${uploaded.resourceId}/reviews`,{outcome:'accepted',reason:'Review the fictional commercial insured request and effective date'});
  let preview=(await get(route+'/cancellation-preview')).data;assert.deepEqual(preview.blockers,[]);
  assert.ok(BigInt(preview.amounts.posting.invoiceDue.replace('.',''))<0n);
  await write('prepare','/cancellation-preview',{previewHash:preview.previewHash});
  preview=(await get(route+'/cancellation-preview')).data;
  await write('approve','/cancellation-approvals',{previewId:preview.previewId,previewHash:preview.previewHash,reason:'Approve the exact fictional commercial cancellation and posted credit'});
  preview=(await get(route+'/cancellation-preview')).data;
  await write('issue','/cancellation-issue',{previewId:preview.previewId,approvalId:preview.approvalId,previewHash:preview.previewHash,reason:'Issue the approved commercial cancellation without a cash payment'});
 }
 const issued=await until(route+'/cancellation-issue',x=>x.consequences.some(c=>c.kind==='cancellation-notice'&&c.state==='succeeded'));
 assert.equal(issued.cashPaid,'0.00');assert.ok(BigInt(issued.netAmount.replace('.',''))<0n);
 assert.equal(issued.consequences.some(x=>x.kind.includes('mid')),false);
 const retained=(await get(`/api/v1/policies/${policy.id}/terms/${issued.termId}/versions/${issued.versionId}`)).data;
 assert.deepEqual(retained.snapshot.risk,policy.snapshot.risk);assert.deepEqual(retained.snapshot.cover,policy.snapshot.cover);
 const beforeAt=new Date(Date.parse(issued.effectiveAt)-1).toISOString();
 const exposure=async effectiveAt=>(await get(`/api/v1/policies/${policy.id}/commercial-exposure?effectiveAt=${encodeURIComponent(effectiveAt)}&knownAt=${encodeURIComponent(knownAt())}`)).data;
 const before=await exposure(beforeAt),after=await exposure(issued.effectiveAt);
 assert.ok(before.districts.some(x=>BigInt(x.ownProposedSumInsured.replace('.',''))>0n));
 assert.equal(after.coverageState,'cancelled');assert.ok(after.districts.every(x=>x.ownProposedSumInsured==='0.00'));
 const original=(await get(`/api/v1/policies/${policy.id}/terms/${policy.termId}/versions/${policy.versionId}`)).data;
 assert.equal(original.contentHash,policy.contentHash);assert.deepEqual(original.snapshot,policy.snapshot);
 await writeFile(directory+'/cancellation.json',JSON.stringify({draftId:created.id,issued,before,after},null,2));return issued;
}
