import test from 'node:test';
import assert from 'node:assert/strict';
import { servicingRatingDisplayState, revisedTermPremium } from '../lib/servicing-rating.ts';
const now=Date.parse('2026-09-17T12:00:00Z');
const cycle={state:'rated',applicable:true,result:{expiresAt:'2026-09-17T12:01:00Z',detailsAvailable:true}};

test('revised term premium applies the signed premium movement without fees or floating-point loss',()=>{
 assert.equal(revisedTermPremium('1200.00','35.01'),'1235.01');
 assert.equal(revisedTermPremium('1200.00','-1199.99'),'0.01');
 assert.equal(revisedTermPremium('0.00','0.00'),'0.00');
 assert.equal(revisedTermPremium('9999999999999.98','0.01'),'9999999999999.99');
 assert.equal(revisedTermPremium(undefined,'1.00'),null);
 assert.equal(revisedTermPremium('1.00','-2.00'),null);
 assert.equal(revisedTermPremium('1.1','0.10'),null);
});
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
