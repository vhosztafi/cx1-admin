import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv from 'ajv/dist/2020.js';
import formats from 'ajv-formats';
import {applyRecoveryEvent,initialRecoveryState} from '../scripts/adapter-recovery-design.mjs';
const read=async name=>JSON.parse(await readFile(new URL(`../contracts/schemas/${name}`,import.meta.url),'utf8'));
const ajv=new Ajv({strict:true,allErrors:true});formats(ajv);
for(const name of ['policy.schema.json','policy-draft.schema.json'])ajv.addSchema(await read(name),`https://schemas.cover-mga.example/adapters/${name}`);
const schema=await read('adapters.schema.json');const validate=ajv.compile(schema);
const id='11111111-1111-4111-8111-111111111111';
const base={operationId:id,operationKey:'demo-payment-1',sourceVersionId:id,scenarioVersionId:id,correlationId:id,recordedAt:'2026-09-13T12:00:00Z'};
test('all nine internal ports have strictly compiled request and result variants',()=>{
 assert.equal(schema.oneOf.length,19);
 for(const kind of ['rating','document-generation','document-storage','email','lookup','mid','claims','payment','bordereau'])for(const direction of ['request','result'])assert.equal(schema.oneOf.filter(s=>s.properties.kind.const===kind&&s.properties.direction.const===direction).length,1);
});
test('payment payload permits approved obligation identity and rejects bank secrets or negative amounts',()=>{
 const message={...base,kind:'payment',direction:'request',payload:{obligationKind:'refund',obligationId:id,paymentKey:id,amount:'100.00',currency:'GBP',payeeReference:'DEMO-PAYEE'}};
 assert.ok(validate(message),JSON.stringify(validate.errors));
 assert.equal(validate({...message,payload:{...message.payload,bankPassword:'secret'}}),false);
 assert.equal(validate({...message,payload:{...message.payload,amount:'-100.00'}}),false);
 assert.equal(validate({...message,kind:'claims'}),false);
});
test('lookup payload uses discriminated query and cannot contain unrelated person data',()=>{
 const message={...base,kind:'lookup',direction:'request',payload:{referenceDataVersionId:id,query:{kind:'vehicle',registration:'DEMO 01'}}};
 assert.ok(validate(message),JSON.stringify(validate.errors));
 assert.equal(validate({...message,payload:{...message.payload,query:{kind:'vehicle',registration:'DEMO 01',healthDetails:'private'}}}),false);
 assert.equal(validate({...message,payload:{...message.payload,query:{kind:'address',registration:'DEMO 01'}}}),false);
});
test('design recovery preserves committed issue through delivery failure and retry',()=>{
 let state=initialRecoveryState();state=applyRecoveryEvent(state,{type:'issue-rollback'});
 assert.equal(state.policyTransactions,0);assert.equal(state.outbox,0);
 for(const type of ['issue-commit','delivery-failure','worker-restart','issue-commit'])state=applyRecoveryEvent(state,{type});
 assert.equal(state.policyTransactions,1);assert.equal(state.policyJournals,1);assert.equal(state.outbox,1);assert.equal(state.delivery,'retry');
});
test('design payment reconciliation after timeout deduplicates callback and quarantines changed content',()=>{
 let state=initialRecoveryState();
 for(const event of [{type:'provider-payment',hash:'request-a'},{type:'worker-restart'},{type:'provider-payment',hash:'request-a'},{type:'payment-callback',hash:'result-a'},{type:'payment-callback',hash:'result-a'}])state=applyRecoveryEvent(state,event);
 assert.equal(state.paymentJournals,1);assert.equal(state.refundPaid,true);
 assert.throws(()=>applyRecoveryEvent(state,{type:'provider-payment',hash:'request-b'}));
 state=applyRecoveryEvent(state,{type:'payment-callback',hash:'result-b'});
 assert.equal(state.quarantined,true);assert.equal(state.paymentJournals,1);
});
