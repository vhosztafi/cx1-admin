import test from 'node:test';
import assert from 'node:assert/strict';
import {commercialServicingCapture,updateCommercialServicing,removeCommercialChange} from '../lib/commercial-servicing.ts';
const policyId='aaaaaaaa-0000-4000-8000-000000000001',clientId='aaaaaaaa-0000-4000-8000-000000000002',locationId='aaaaaaaa-0000-4000-8000-000000000003';
const base={schemaVersion:'1.0',format:'commercial-combined-capture-1',productCode:'commercial-combined',insured:{legalName:'Issued insured'},risk:{locations:[{id:locationId,reference:'L1',buildings:'10.00',sprinklers:true}],wages:[],losses:[],business:{description:'Issued business'}},cover:{contractWorks:{selected:true,sumInsured:'10.00'}}};
const proposal=()=>({schemaVersion:'1.0',baseVersionId:policyId,reason:'Fictional adjustment',requestedBy:{kind:'internal'},commonEffectiveIntent:{localDate:'2026-10-01',localTime:'00:00',timeZone:'Europe/London'},changes:[]});
test('commercial field edits produce typed replacements without mutating issued data',()=>{
 const original=JSON.stringify(base), next=structuredClone(base);next.risk.locations[0].buildings='0.00';delete next.risk.locations[0].reference;next.risk.locations[0].sprinklers=false;
 const p=updateCommercialServicing(base,proposal(),next,policyId,clientId);assert.equal(p.changes.length,1);assert.equal(p.changes[0].kind,'commercial-location');assert.equal(p.changes[0].riskItemId,locationId);
 assert.equal(p.changes[0].payloadMode,'replace');assert.equal('id' in p.changes[0].payload,false);
 assert.deepEqual(commercialServicingCapture(base,p),next);assert.equal(JSON.stringify(base),original);
 const repeat=updateCommercialServicing(base,p,next,policyId,clientId);assert.deepEqual(repeat,p);
});
test('commercial adds and removals preserve stable identities and remove dependent proposals',()=>{
 const next=structuredClone(base),id=crypto.randomUUID();next.risk.locations.push({id,reference:'New site'});
 const added=updateCommercialServicing(base,proposal(),next,policyId,clientId);assert.equal(added.changes[0].operation,'add');
 const changed=structuredClone(next);changed.risk.locations[1].reference='Edited new site';
 const edited=updateCommercialServicing(base,added,changed,policyId,clientId);assert.equal(edited.changes.length,1);assert.equal(edited.changes[0].changeId,added.changes[0].changeId);assert.equal(edited.changes[0].operation,'add');
 assert.deepEqual(removeCommercialChange(edited,edited.changes[0].changeId).changes,[]);
 const removed=structuredClone(changed);removed.risk.locations.splice(1,1);assert.deepEqual(updateCommercialServicing(base,edited,removed,policyId,clientId).changes,[]);
 removed.risk.locations=[];const deletion=updateCommercialServicing(base,proposal(),removed,policyId,clientId);assert.equal(deletion.changes[0].operation,'remove');assert.equal('payload' in deletion.changes[0],false);
});
test('editing a later cover slice preserves earlier dated proposal and exact change identities',()=>{
 const p=proposal();p.dateBasis='per-cover-change';
 p.changes=[{changeId:crypto.randomUUID(),riskItemId:policyId,kind:'commercial-cover',operation:'update',payload:{contractWorks:{sumInsured:'20.00'}}},
 {changeId:crypto.randomUUID(),riskItemId:policyId,kind:'commercial-cover',operation:'update',effectiveIntent:{localDate:'2026-11-01',localTime:'00:00',timeZone:'Europe/London'},payload:{contractWorks:{sumInsured:'30.00'}}}];
 const next=commercialServicingCapture(base,p);assert.equal(next.cover.contractWorks.sumInsured,'30.00');next.cover.contractWorks.sumInsured='40.00';
 const edited=updateCommercialServicing(base,p,next,policyId,clientId);assert.deepEqual(edited.changes[0],p.changes[0]);assert.equal(edited.changes[1].changeId,p.changes[1].changeId);assert.deepEqual(edited.changes[1].effectiveIntent,p.changes[1].effectiveIntent);
 assert.equal(commercialServicingCapture(base,edited).cover.contractWorks.sumInsured,'40.00');
});
test('foreign subjects and motor changes cannot become a local commercial preview',()=>{
 const p=proposal();p.changes=[{changeId:crypto.randomUUID(),riskItemId:crypto.randomUUID(),kind:'commercial-location',operation:'update',payload:{reference:'Foreign'}}];
 assert.throws(()=>commercialServicingCapture(base,p));p.changes[0].kind='driver';assert.throws(()=>commercialServicingCapture(base,p));
});
