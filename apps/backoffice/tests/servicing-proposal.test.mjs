import test from 'node:test';
import assert from 'node:assert/strict';
import { servicingDateFeedback, changeServicingDate, setCoverEffectiveIntent, setServicingDateBasis, matchingServicingEditor } from '../lib/servicing-proposal.ts';
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
