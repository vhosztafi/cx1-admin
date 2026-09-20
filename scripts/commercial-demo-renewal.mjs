import assert from 'node:assert/strict';

export async function commercialDemoRenewalInput(policy,get){
 const preview=(await get(`/api/v1/terms/${policy.termId}/renewal-preview`)).data;
 assert.equal(preview.baseVersionId,policy.versionId,'Renewal must use the final known expiring version.');
 const intent=preview.termIntent;
 return{kind:'renewal',baseVersionId:preview.baseVersionId,commonEffectiveIntent:{localDate:intent.localStartDate,localTime:intent.localStartTime,timeZone:'Europe/London',utcOffsetMinutes:intent.utcOffsetMinutes},
  reason:'Fictional commercial renewal with explicitly supplied whole-risk experience'};
}

export async function prepareCommercialDemoRenewal({route,write,get}){
 await write('preparation','/renewal/preparation',{termMonths:12});
 const uploaded=await write('experience-upload','/renewal/experience/uploads',undefined,'POST',{
  name:'fictional-commercial-renewal-experience.txt',content:'Fictional supplied whole-risk experience: 1 January 2025 to 1 January 2026, zero claims, zero paid and outstanding, GBP1000 earned premium. Business demonstration only.'});
 const current=(await get(route+'/renewal/experience')).data;assert.ok(current.currentCommercialSubjects);
 const saved=await write('experience','/renewal/experience',{observationStartsOn:'2025-01-01',observationEndsOn:'2026-01-01',claimCount:0,paid:'0.00',outstanding:'0.00',earnedPremium:'1000.00',
  sourceCode:'agency',sourceReference:'Fictional supplied commercial whole-risk experience',evidenceAssociationId:uploaded.resourceId,commercialSubjects:current.currentCommercialSubjects},'PUT');
 await write('experience-review',`/renewal/experience/${saved.resourceId}/reviews`,{outcome:'accepted',reason:'Review the exact supplied fictional whole-risk commercial experience'});
 const reviewed=(await get(route+'/renewal/experience')).data;assert.equal(reviewed.review.outcome,'accepted');
 assert.deepEqual(reviewed.experience.commercialSubjects,reviewed.currentCommercialSubjects);
}
