import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { driverOptionStates, resolvedDriverField } from '../lib/quote-driver-options.ts';
import { readinessTarget } from '../lib/quote-readiness.ts';
const catalogue=JSON.parse(await readFile(new URL('../../../contracts/reference-data/motor-trade-capture.json',import.meta.url),'utf8'));
const reference=(collection,value)=>({collection,value,version:catalogue.version,label:catalogue.collections[collection].find(row=>row.value===value).text});
const answer=(questionId,value)=>({questionId,kind:'reference',value});
const base=()=>({schemaVersion:'1.0',productCode:'motor-trade-road-risks',termIntent:{localStartDate:'2026-01-01'},
 cover:{responses:{answers:[answer('MTS-05-Q01',reference('coverLevels',1)),answer('MTS-05-Q02',reference('indemnityOwnVehicles',5))]}},
 risk:{business:{activities:[{code:reference('mtOccupations',5)}]},drivers:[{id:'first',dateOfBirth:'2008-01-01',licence:{issuedOn:'2025-01-01'}},{id:'second',dateOfBirth:'2004-01-01',licence:{issuedOn:'2024-01-01'}}]}});
const field=(id)=>({id,questionId:id,group:'driver',path:'responses',label:'Driver option',kind:'reference',choices:[],unavailable:true});
test('unanswered driver options resolve independently from age and policy limits without fabricating answers',()=>{
 const proposal=base(),before=structuredClone(proposal),states=driverOptionStates(proposal,catalogue);
 const young=resolvedDriverField(field('MTS-06-Q59'),0,states),older=resolvedDriverField(field('MTS-06-Q60'),1,states);
 assert.equal(young.collection,'youngDriverConfiguration/0/indemnities');assert.equal(young.unavailable,false);assert.deepEqual(young.choices.map(row=>row.value),[1,2]);
 assert.equal(older.collection,'youngDriverConfiguration/2/cCs');assert.deepEqual(older.choices.map(row=>row.value),[3,4,5]);
 assert.deepEqual(proposal,before);assert.equal(proposal.risk.drivers[0].responses,undefined);
 assert.deepEqual(readinessTarget({path:'/risk/drivers/0/responses/answers',questionId:'MTS-06-Q59'},proposal,[],[field('MTS-06-Q59')],states),{stage:3,label:'Driver 1 · Driver option'});
});
test('changed age retains a recorded answer and routes readiness to explicit clearing',()=>{
 const proposal=base();proposal.risk.drivers[0].responses={answers:[answer('MTS-06-Q60',reference('youngDriverConfiguration/0/cCs',1))]};
 proposal.risk.drivers[0].dateOfBirth='2001-01-01';const before=structuredClone(proposal),states=driverOptionStates(proposal,catalogue);
 assert.equal(resolvedDriverField(field('MTS-06-Q60'),0,states).unavailableReason,'inactive');assert.deepEqual(proposal,before);
 assert.deepEqual(readinessTarget({path:'/risk/drivers/0/responses/answers/0/value',questionId:'MTS-06-Q60'},proposal,[],[field('MTS-06-Q60')],states),{stage:3,label:'Clear Driver 1 · Driver option'});
});
test('missing context and an empty eligible option family are distinct and neither guesses a choice',()=>{
 const proposal=base();proposal.cover.responses.answers[1].value=reference('indemnityOwnVehicles',2);
 let resolved=resolvedDriverField(field('MTS-06-Q59'),0,driverOptionStates(proposal,catalogue));assert.equal(resolved.unavailableReason,'inactive');assert.deepEqual(resolved.choices,[]);
 proposal.risk.business.activities=[];resolved=resolvedDriverField(field('MTS-06-Q59'),0,driverOptionStates(proposal,catalogue));assert.equal(resolved.unavailableReason,'context');
 assert.equal(resolvedDriverField(field('MTS-06-Q60'),0,driverOptionStates(proposal,undefined)).unavailable,true);
});
