import test from 'node:test';
import assert from 'node:assert/strict';
import { readinessTarget } from '../lib/quote-readiness.ts';
const proposal = {schemaVersion:'1.0', productCode:'motor-trade-combined'};
const issue = (path, questionId) => ({path, questionId, code:'required-capture-field'});
test('readiness links use source question ownership and never route later answers by storage container', () => {
 const questions = [{id:'trader', label:'Trader', stage:1, products:['motor-trade-combined']}];
 assert.deepEqual(readinessTarget(issue('/risk/business/responses/answers', 'trader'), proposal, questions), {stage:1,label:'Trader'});
 assert.equal(readinessTarget(issue('/risk/business/responses/answers', 'later-claims'), proposal, questions), undefined);
 assert.equal(readinessTarget(issue('/risk/premises/0/address'), proposal, questions), undefined);
 assert.equal(readinessTarget(issue('/insured'), proposal, questions), undefined);
 assert.equal(readinessTarget(issue('/termIntent'), proposal, questions), undefined);
 assert.deepEqual(readinessTarget(issue('/risk/business/startedOn'), proposal, questions), {stage:1,label:'Business start date'});
});
test('readiness resolves exact saved row fields, names and annual offset without inventing missing rows', () => {
 const draft={...proposal,termIntent:{kind:'annual'},risk:{business:{activities:[{id:'row-1'}]}}};
 assert.deepEqual(readinessTarget(issue('/risk/business/activities/0/turnoverBasisPoints'),draft,[]),{stage:2,label:'Occupation 1 turnover share (%)'});
 assert.equal(readinessTarget(issue('/risk/business/activities/4/code'),draft,[]),undefined);
 assert.deepEqual(readinessTarget(issue('/insured/proposerNames/2'),draft,[]),{stage:1,label:'Proposer 3 full name'});
 assert.equal(readinessTarget(issue('/insured/proposerNames/3'),draft,[]),undefined);
 assert.deepEqual(readinessTarget(issue('/termIntent/endUtcOffsetMinutes'),draft,[]),{stage:0,label:'Annual end clock offset'});
});


test('driver and history guidance resolves exact current rows and skips unavailable dynamic controls',()=>{
 const fields=[{id:'MTS-06-Q01',questionId:'MTS-06-Q01',group:'plan',label:'Driver Plan'},
 {id:'MTS-06-Q09',group:'driver',path:'firstName',label:'First Name'},
 {id:'MTS-06-Q36',group:'convictions',path:'fine',label:'Fine Amount'},
 {id:'prototype.addconv.prosecution-status',questionId:'prototype.addconv.prosecution-status',group:'convictions',label:'Pending or convicted'},
 {id:'MTS-06-Q59',questionId:'MTS-06-Q59',group:'driver',label:'Young driver indemnity limit',unavailable:true}];
 const draft={...proposal,risk:{drivers:[{id:'driver',convictions:[{id:'conviction',responses:{answers:[{questionId:'prototype.addconv.prosecution-status',value:{}}]}}]}]}};
 assert.deepEqual(readinessTarget(issue('/risk/responses/answers','MTS-06-Q01'),draft,[],fields),{stage:4,label:'Driver basis · Driver Plan'});
 assert.deepEqual(readinessTarget(issue('/risk/drivers/0/firstName','MTS-06-Q09'),draft,[],fields),{stage:4,label:'Driver 1 · First Name'});
 assert.deepEqual(readinessTarget(issue('/risk/drivers/0/convictions/0/fine','MTS-06-Q36'),draft,[],fields),{stage:5,label:'Driver 1 · Motoring convictions 1 · Fine Amount'});
 assert.deepEqual(readinessTarget(issue('/risk/drivers/0/convictions/0/responses/answers/0/value'),draft,[],fields),{stage:5,label:'Driver 1 · Motoring convictions 1 · Pending or convicted'});
 assert.equal(readinessTarget(issue('/risk/materialFacts','prototype.quote.36da21d3c935'),draft,[],fields),undefined);
 assert.equal(readinessTarget(issue('/risk/drivers/3/firstName'),draft,[],fields),undefined);
 assert.equal(readinessTarget(issue('/risk/drivers/0/responses/answers','MTS-06-Q59'),draft,[],fields),undefined);
 assert.deepEqual(readinessTarget(issue('/risk/business/responses/answers','prototype.quote.36da21d3c935'),draft,[],fields),{stage:5,label:'Accidents, claims or losses in the last three years'});
});
