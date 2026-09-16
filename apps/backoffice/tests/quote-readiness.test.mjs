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
