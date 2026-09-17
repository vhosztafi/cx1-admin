import test from 'node:test';
import assert from 'node:assert/strict';
import { servicingRatingDisplayState } from '../lib/servicing-rating.ts';
const now=Date.parse('2026-09-17T12:00:00Z');
const cycle={state:'rated',applicable:true,result:{expiresAt:'2026-09-17T12:01:00Z',detailsAvailable:true}};
test('rating display expires at the exact boundary without waiting for another poll',()=>{
 assert.equal(servicingRatingDisplayState(cycle,'current','current',now),'rated');
 assert.equal(servicingRatingDisplayState(cycle,'current','current',now+60000),'expired');
});
test('a changed draft fence cannot display the old result as current',()=>{
 assert.equal(servicingRatingDisplayState(cycle,'old','current',now),'stale');
 assert.equal(servicingRatingDisplayState({...cycle,applicable:false},'current','current',now),'stale');
 assert.equal(servicingRatingDisplayState({...cycle,result:{...cycle.result,detailsAvailable:false}},'current','current',now),'stale');
});
test('failed and pending states do not become rated from stale amounts',()=>{
 assert.equal(servicingRatingDisplayState({...cycle,state:'failed'},'current','current',now),'failed');
 assert.equal(servicingRatingDisplayState({...cycle,state:'rating-pending',result:null},'current','current',now),'rating-pending');
 assert.equal(servicingRatingDisplayState(null,'current','current',now),'unrated');
});
