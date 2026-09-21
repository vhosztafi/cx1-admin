import test from 'node:test';
import assert from 'node:assert/strict';
import {readCommercialHistory} from '../scripts/verify-commercial-underwriting-browser.mjs';

function fixture(responses) {
  let calls=0;
  return {get calls(){return calls;},async get(){
    const [status,body]=responses[Math.min(calls++,responses.length-1)];
    return {status:()=>status,json:async()=>body,text:async()=>JSON.stringify(body)};
  }};
}
test('commercial first-page history read restarts after a worker changes discovery',async()=>{
  const request=fixture([[409,{code:'underwriting-history-changed'}],[200,{items:[{id:'saved-proof'}]}]]);
  assert.deepEqual(await readCommercialHistory(request,'http://127.0.0.1','/evidence?pageSize=100'),{items:[{id:'saved-proof'}]});
  assert.equal(request.calls,2);
});
test('commercial history retry is bounded',async()=>{
  const request=fixture([[409,{code:'underwriting-history-changed'}]]);
  await assert.rejects(()=>readCommercialHistory(request,'http://127.0.0.1','/evidence'));
  assert.equal(request.calls,4);
});
test('commercial history never retries authorization or unrelated conflicts',async()=>{
  for(const [status,code] of [[403,'forbidden'],[409,'stale-context'],[500,'unexpected']]) {
    const request=fixture([[status,{code}]]);
    await assert.rejects(()=>readCommercialHistory(request,'http://127.0.0.1','/evidence'));
    assert.equal(request.calls,1);
  }
});
test('commercial history never retries a stale continuation cursor',async()=>{
  const request=fixture([[409,{code:'underwriting-history-changed'}]]);
  await assert.rejects(()=>readCommercialHistory(request,'http://127.0.0.1','/evidence?cursor=old'));
  assert.equal(request.calls,1);
});
