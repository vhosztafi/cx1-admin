import test from 'node:test';
import assert from 'node:assert/strict';
import {referralWorkHash,referralWorkId} from '../lib/servicing-referral-work.ts';

test('referral work navigation round-trips a saved identifier without accepting other routes',()=>{
 const id='10000000-0000-4000-8000-000000000001';
 assert.equal(referralWorkId(referralWorkHash(id)),id);
 for(const value of ['#servicing-referrals','#servicing-referral-00000000-0000-0000-0000-000000000000','#servicing-referral-'+id+'?other=record','#servicing-referral-../other','https://external.example/'+id])assert.equal(referralWorkId(value),null);
 assert.throws(()=>referralWorkHash('../other'));assert.throws(()=>referralWorkHash('00000000-0000-0000-0000-000000000000'));
});
