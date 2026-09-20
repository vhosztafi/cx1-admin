import test from 'node:test';
import assert from 'node:assert/strict';
import { decisionRequest, conditionProof } from '../lib/servicing-referrals.ts';
const ids = Array.from({length:5},(_,n)=>`20000000-0000-0000-0000-00000000000${n+1}`);
const rows = ids.slice(0,2).map(id=>({id,etag:'"AAAAAAAAAAE="'}));
test('selected referral decisions are all-or-none and use the single route for one row',()=>{
 assert.throws(()=>decisionRequest(ids[2],rows,[ids[0],ids[4]],'approve','Approved after review',[]));
 assert.throws(()=>decisionRequest(ids[2],rows,[ids[0],ids[0]],'approve','Approved after review',[]));
 const single=decisionRequest(ids[2],rows,[ids[0]],'approve','Approved after review',[]);
 assert.equal(single.path,`/referrals/${ids[0]}/decisions`); assert.equal(single.body.decision.etag,rows[0].etag);
 const selected=decisionRequest(ids[2],rows,[ids[0],ids[1]],'decline','Declined after review',[]);
 assert.equal(selected.path,'/referrals/decisions'); assert.equal(selected.body.decisions.length,2);
});
test('queries require a question and documentary conditions; unrelated outcome fields are omitted',()=>{
 const decide=(outcome,conditions,question)=>decisionRequest(ids[2],rows,[ids[0]],outcome,'Decision reason recorded',conditions,question);
 assert.throws(()=>decide('query',[], 'Tell us more'));
 assert.throws(()=>decide('query',[{code:'provide-trading-history'}],''));
 assert.throws(()=>decide('query',[{code:'named-drivers-only',driverIds:[ids[3]]}],'Tell us more'));
 assert.throws(()=>decide('approve-with-conditions',[]));
 assert.throws(()=>decide('approve-with-conditions',Array.from({length:21},()=>({code:'provide-trading-history'}))));
 assert.throws(()=>decide('query',[{code:'provide-trading-history'}],'Short'));
 assert.throws(()=>decide('invented',[]));
 const result=decide('approve',[{code:'provide-trading-history'}],'Ignored question').body.decision;
 assert.equal(result.conditions,undefined); assert.equal(result.question,undefined);
});
test('condition proof matches all dated targets; rejected reviewed content can support a rejected resolution',()=>{
 const context={draftId:ids[0],cycleId:ids[1],revisionId:ids[2],ratingId:ids[3]};
 const requirement={code:'photocard-both-sides',riskItemId:ids[4],inputFingerprint:'a'.repeat(64),context,effectiveDates:['2026-10-01T00:00:00Z']};
 const condition={kind:'documentary',code:'provide-driver-proof',definition:{code:'provide-driver-proof',driverId:ids[4],requirementCode:requirement.code},clauses:[{effectiveAt:requirement.effectiveDates[0],targetIds:[ids[4]]}]};
 const proof={...context,code:requirement.code,riskItemId:ids[4],inputFingerprint:requirement.inputFingerprint,withdrawn:false,latestReviewId:ids[0],reviewOutcome:'rejected'};
 assert.equal(conditionProof(condition,requirement,proof,'satisfied'),false);
 assert.equal(conditionProof(condition,requirement,proof,'rejected'),true);
 assert.equal(conditionProof(condition,{...requirement,effectiveDates:[]},proof,'rejected'),false);
 assert.equal(conditionProof(condition,requirement,{...proof,withdrawn:true},'rejected'),false);
});

test('signed condition resolution requires the exact owned terms proof',()=>{
 const context={draftId:ids[0],cycleId:ids[1],revisionId:ids[2],ratingId:ids[3]};
 const requirement={code:'signed-statement',riskItemId:null,termsVersionId:ids[4],inputFingerprint:'a'.repeat(64),context,effectiveDates:['2026-10-01T00:00:00Z']};
 const condition={kind:'documentary',code:'provide-signed-statement',definition:{code:'provide-signed-statement',termsVersionId:ids[4],termsHash:'b'.repeat(64)},clauses:[{effectiveAt:requirement.effectiveDates[0],targetIds:[]}]};
 const proof={...context,...requirement,withdrawn:false,latestReviewId:ids[0],reviewOutcome:'accepted'};
 assert.equal(conditionProof(condition,requirement,proof,'satisfied'),true);
 assert.equal(conditionProof({...condition,definition:{...condition.definition,termsVersionId:ids[3]}},requirement,proof,'satisfied'),false);
 assert.equal(conditionProof(condition,requirement,{...proof,termsVersionId:ids[2]},'satisfied'),false);
});

test('commercial query and condition resolution retain their exact subject and dated proof',()=>{
 const condition={kind:'documentary',code:'provide-cc-location-proof',definition:{code:'provide-cc-location-proof',riskItemId:ids[4]},clauses:[{effectiveAt:'2026-10-01T00:00:00Z',targetIds:[ids[4]]}]};
 assert.equal(decisionRequest(ids[2],rows,[ids[0]],'query','Please review this location',[condition.definition],'Please provide exact location proof').body.decision.conditions[0].riskItemId,ids[4]);
 assert.throws(()=>decisionRequest(ids[2],rows,[ids[0]],'query','Please review this location',[{code:'provide-cc-invented-proof'}],'Please provide exact location proof'));
 const context={draftId:ids[0],cycleId:ids[1],revisionId:ids[2],ratingId:ids[3]};
 const requirement={code:'cc-location-proof',riskItemId:ids[4],inputFingerprint:'a'.repeat(64),context,effectiveDates:['2026-10-01T00:00:00Z']};
 const proof={...context,...requirement,withdrawn:false,latestReviewId:ids[0],reviewOutcome:'accepted'};
 assert.equal(conditionProof(condition,requirement,proof,'satisfied'),true);
 for(const bad of [{...requirement,riskItemId:ids[3]},{...requirement,effectiveDates:[]},{...requirement,inputFingerprint:'b'.repeat(64)}])assert.equal(conditionProof(condition,bad,proof,'satisfied'),false);
 assert.equal(conditionProof(condition,requirement,{...proof,withdrawn:true},'satisfied'),false);
});
