import assert from 'node:assert/strict';
import {writeFile} from 'node:fs/promises';

// Deliberately retain distinct business states. All outcomes come from scoped
// commands and the persistent local capacity adapter, never synthetic rows.
export async function commercialDemoReferrals({fixtures,get,command,until,knownAt,directory}){
 const results=[];
 for(const scenario of ['flood-referral','outside-appetite','conditional-capacity','capacity-contender']){
  const fixture=fixtures.find(x=>x.scenario===scenario);assert.ok(fixture);
  const route='/api/v1/quotes/'+fixture.quoteId;
  const quote=(await get(route)).data;
  assert.equal(quote.productCode,'commercial-combined');
  assert.equal(quote.proposal.risk.materialFacts,`Fictional commercial demo v1: ${scenario}`,'Review edited demonstration proposals before continuing.');
  await command(scenario+':rate',route+'/rate',{revisionId:quote.revisionId,reason:'Rate the fictional '+scenario+' commercial demonstration'},route);
  const assessment=await until(route+'/underwriting',x=>!!x.ratingId);
  const list=(await get('/api/v1/referrals?quoteId='+fixture.quoteId+'&pageSize=100')).data;
  assert.ok(!list.nextCursor,'Demonstration referral list must be complete.');
  if(scenario==='flood-referral')assert.ok(list.items.some(x=>x.ruleCode==='PR-05'&&x.state==='open'));
  if(scenario==='outside-appetite'){
   await command(scenario+':decline',route+'/referral-decisions',{cycleId:assessment.context.cycleId,
    decisions:list.items.map(x=>({referralId:x.id,etag:x.etag,outcome:'decline',reason:'Decline the fictional risk outside the published commercial appetite'}))},route);
   assert.equal((await get(route)).data.state,'declined');
  }
  const escalations=[];
  if(scenario==='conditional-capacity'){
   const carrier=list.items.filter(x=>['AU-05','AU-06'].includes(x.ruleCode));assert.ok(carrier.length);
   for(const referral of carrier){
    const prefix=scenario+':'+referral.id;
    const created=await command(prefix+':create',`/api/v1/referrals/${referral.id}/escalations`,{cycleId:assessment.context.cycleId,
     referralEtag:referral.etag,providerId:assessment.providerId,reason:'Request conditional capacity for the fictional commercial location'},route);
    const escalationRoute='/api/v1/escalations/'+created.id;
    let escalation=(await get(escalationRoute)).data;
    const scenarioVersion=escalation.scenarios.find(x=>x.label==='Demo: cc conditional proof');assert.ok(scenarioVersion);
    await command(prefix+':send',escalationRoute+'/send',{cycleId:assessment.context.cycleId,escalationEtag:escalation.etag,
     body:'Fictional demonstration: request the saved commercial capacity with documentary conditions.',evidenceAssociationIds:[],scenarioVersionId:scenarioVersion.id},route);
    escalation=await until(escalationRoute,x=>x.state==='conditional');
    assert.ok(escalation.currentResponseId);assert.ok(escalation.messages.some(x=>x.outcome==='approve-with-conditions'));
    escalations.push({id:escalation.id,state:escalation.state,responseId:escalation.currentResponseId});
   }
  }
  let exposure;
  if(scenario==='capacity-contender'){
   assert.equal(quote.proposal.risk.locations[0].buildings,'39900000.01','This older or edited scenario is preserved; review its capacity recipe before running.');
   exposure=(await get(route+'/commercial-exposure?'+new URLSearchParams({effectiveAt:'2026-11-15T12:00:00Z',knownAt:knownAt()}))).data;
   assert.equal(exposure.outcome,'exceeds-capacity');
   assert.ok(exposure.districts.some(x=>x.blocker==='commercial-district-capacity-exceeded'&&BigInt(x.bookSumInsured.replace('.',''))>0n));
  }
  const retained=(await get(route)).data;
  results.push({scenario,quoteId:fixture.quoteId,reference:retained.reference,state:retained.state,escalations,...(exposure?{exposure}:{})});
 }
 await writeFile(directory+'/referrals-and-capacity.json',JSON.stringify(results,null,2));return results;
}
