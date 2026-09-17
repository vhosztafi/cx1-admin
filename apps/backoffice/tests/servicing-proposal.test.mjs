import test from 'node:test';
import assert from 'node:assert/strict';
import { servicingDateFeedback, changeServicingDate, setCoverEffectiveIntent, setServicingDateBasis, matchingServicingEditor } from '../lib/servicing-proposal.ts';
import { projectServicingCapture, putServicingChange, putServicingVehicleChange } from '../lib/servicing-change-form.ts';
const intent=(localDate,localTime='00:00',utcOffsetMinutes)=>({localDate,localTime,timeZone:'Europe/London',...(utcOffsetMinutes===undefined?{}:{utcOffsetMinutes})});
const draft=()=>({schemaVersion:'1.0',baseVersionId:'base',reason:'Fictional adjustment',requestedBy:{kind:'internal'},commonEffectiveIntent:intent('2026-10-01'),changes:[{changeId:'cover',riskItemId:'section',kind:'cover',operation:'update',payload:{}},{changeId:'driver',riskItemId:'person',kind:'driver',operation:'remove'}]});

test('London gap and fold feedback requires a valid explicit offset without inventing an instant',()=>{
 assert.match(servicingDateFeedback(intent('2027-03-28','01:30')).error,/valid London/);
 assert.match(servicingDateFeedback(intent('2026-10-25','01:30')).error,/twice/);
 const first=servicingDateFeedback(intent('2026-10-25','01:30',60)),second=servicingDateFeedback(intent('2026-10-25','01:30',0));
 assert.equal(second.instant-first.instant,3600000); assert.equal(first.error,undefined);
 assert.match(servicingDateFeedback(intent('2026-12-01','10:00',60)).error,/offset/);
});
test('changing a local date clears a stale daylight offset and preserves the prior object',()=>{
 const before=intent('2026-10-25','01:30',60),after=changeServicingDate(before,'localDate','2026-12-01');
 assert.equal(after.utcOffsetMinutes,undefined); assert.equal(before.utcOffsetMinutes,60); assert.equal(after.localTime,'01:30');
});
test('only cover changes can gain an override and shared mode never silently discards one',()=>{
 const original=draft(); const proposal=setServicingDateBasis(original,'per-cover-change');
 const changed=setCoverEffectiveIntent(proposal,'cover',intent('2026-10-03'));
 assert.equal(changed.changes[0].effectiveIntent.localDate,'2026-10-03'); assert.equal(original.changes[0].effectiveIntent,undefined);
 assert.throws(()=>setCoverEffectiveIntent(proposal,'driver',intent('2026-10-03')));
 assert.throws(()=>setCoverEffectiveIntent(original,'cover',intent('2026-10-03')));
 assert.throws(()=>setCoverEffectiveIntent(proposal,'missing',intent('2026-10-03')));
 assert.throws(()=>setServicingDateBasis(changed,'shared'));
 const cleared=setCoverEffectiveIntent(changed,'cover',undefined);
 assert.equal(setServicingDateBasis(cleared,'shared').dateBasis,'shared');
 assert.equal(changed.changes[0].effectiveIntent.localDate,'2026-10-03');
});
test('saved comparison is usable only for the same draft, revision and strong ETag',()=>{
 const view={etag:'"AAAAAAAAAAE="',data:{id:'draft',revisionId:'revision'}},editor={etag:'"AAAAAAAAAAE="',data:{draftId:'draft',revisionId:'revision'}};
 assert.equal(matchingServicingEditor(view,editor),true);
 assert.equal(matchingServicingEditor(view,{...editor,etag:'"two"'}),false);
 assert.equal(matchingServicingEditor(view,{...editor,data:{...editor.data,revisionId:'old'}}),false);
 assert.equal(matchingServicingEditor(view,{...editor,data:{...editor.data,draftId:'foreign'}}),false);
 assert.equal(matchingServicingEditor(view,null),false);
 assert.equal(matchingServicingEditor({...view,etag:'W/"one"'},{...editor,etag:'W/"one"'}),false);
});

test('typed local replacement clears fields while retaining identity and original issued capture',()=>{
 const base={schemaVersion:'1.0',productCode:'motor-trade-road-risks',insured:{legalName:'Old'},risk:{drivers:[{id:'driver',fullName:'Original',dateOfBirth:'1980-01-01'}]},cover:{requestedSections:[]}};
 const proposal={...draft(),changes:[{changeId:'change',riskItemId:'driver',kind:'driver',operation:'update',payloadMode:'replace',payload:{fullName:'Corrected'}}]};
 const projected=projectServicingCapture(base,proposal,'policy','client');
 assert.deepEqual(projected.risk.drivers,[{id:'driver',fullName:'Corrected'}]);assert.equal(base.risk.drivers[0].dateOfBirth,'1980-01-01');
 assert.throws(()=>projectServicingCapture(base,{...proposal,changes:[{...proposal.changes[0],riskItemId:'foreign'}]},'policy','client'));
});
test('editing an existing proposal retains its change ID and stable addition ID',()=>{
 const proposal={...draft(),changes:[]}; const row={changeId:'change',riskItemId:'new-driver',kind:'driver',operation:'add',payload:{fullName:'First'}};
 const added=putServicingChange(proposal,row),updated=putServicingChange(added,{...row,payload:{fullName:'Edited'}});
 assert.equal(updated.changes.length,1);assert.equal(updated.changes[0].riskItemId,'new-driver');assert.equal(added.changes[0].payload.fullName,'First');assert.equal(proposal.changes.length,0);
});

test('typed projection treats alternate UUID casing as the same item identity',()=>{
 const id='aaaa0000-0000-4000-8000-000000000001';
 const base={schemaVersion:'1.0',productCode:'motor-trade-road-risks',risk:{drivers:[{id,fullName:'Original'}]}};
 const proposal={...draft(),changes:[{changeId:'change',riskItemId:id.toUpperCase(),kind:'driver',operation:'update',payload:{fullName:'Corrected'}}]};
 assert.equal(projectServicingCapture(base,proposal,'policy','client').risk.drivers[0].fullName,'Corrected');
});

test('specified vehicle declarations select only their target and preserve issued capture and membership order',()=>{
 const base={risk:{vehicles:[{id:'one'},{id:'two'}],specifiedVehicleIds:['one','two'],specifiedVehiclesRequested:true}};
 const change={changeId:'change',riskItemId:'one',kind:'vehicle',operation:'update',payload:{},specifiedVehicle:{selected:true,required:true}};
 const project=(changes)=>projectServicingCapture(base,{...draft(),changes},'policy','client');
 assert.deepEqual(project([change]).risk.specifiedVehicleIds,['one','two']);
 const added=project([{...change,riskItemId:'three',operation:'add'}]);
 assert.deepEqual(added.risk.specifiedVehicleIds,['one','two','three']);
 const removed=project([{...change,operation:'remove',specifiedVehicle:{selected:false,required:false}}]);
 assert.deepEqual(removed.risk.specifiedVehicleIds,['two']); assert.equal(removed.risk.specifiedVehiclesRequested,false);
 assert.deepEqual(base.risk.specifiedVehicleIds,['one','two']); assert.equal(base.risk.vehicles.length,2);
});
test('local vehicle declaration context rejects conflicting requirements and invalid targets',()=>{
 const base={risk:{vehicles:[{id:'one'}],drivers:[{id:'driver'}]}};
 const change={changeId:'change',riskItemId:'one',kind:'vehicle',operation:'update',payload:{},specifiedVehicle:{selected:true,required:true}};
 const project=(changes)=>projectServicingCapture(base,{...draft(),changes},'policy','client');
 assert.throws(()=>project([{...change,operation:'remove'}]));
 assert.throws(()=>project([{...change,kind:'driver',riskItemId:'driver'}]));
 assert.throws(()=>project([change,{...change,changeId:'second',specifiedVehicle:{selected:false,required:false}}]));
});


test('applying an explicit vehicle requirement synchronizes prior vehicle declarations without mutating them',()=>{
 const previous={changeId:'old',riskItemId:'one',kind:'vehicle',operation:'add',payload:{},specifiedVehicle:{selected:true,required:true}};
 const proposal={...draft(),changes:[previous]};
 const next=putServicingVehicleChange(proposal,{...previous,changeId:'new',riskItemId:'two',specifiedVehicle:{selected:false,required:false}});
 assert.equal(next.changes.length,2); assert.equal(next.changes[0].specifiedVehicle.required,false);
 assert.equal(next.changes[0].specifiedVehicle.selected,true); assert.equal(previous.specifiedVehicle.required,true);
 assert.throws(()=>putServicingVehicleChange(proposal,{...previous,kind:'driver'}));
});

test('premises replacement clears nested address fields without changing another premises or issued capture',()=>{
 const base={risk:{premises:[{id:'site-one',address:{street:'First',postcode:'AB1 2CD'}},{id:'site-two',address:{street:'Second',postcode:'EF3 4GH'}}]}};
 const proposal={...draft(),changes:[{changeId:'edit',riskItemId:'site-one',kind:'premises',operation:'update',payloadMode:'replace',payload:{address:{street:'Corrected'}}}]};
 const result=projectServicingCapture(base,proposal,'policy','client');
 assert.deepEqual(result.risk.premises[0],{id:'site-one',address:{street:'Corrected'}});
 assert.deepEqual(result.risk.premises[1],base.risk.premises[1]);
 assert.equal(base.risk.premises[0].address.postcode,'AB1 2CD');
 const removed=projectServicingCapture(base,{...proposal,changes:[{...proposal.changes[0],operation:'remove'}]},'policy','client');
 assert.deepEqual(removed.risk.premises,[base.risk.premises[1]]);
});
