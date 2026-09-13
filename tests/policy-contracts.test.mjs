import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import {days,financials,minorUnits,roundRatio,versionAt,issueErrors,cancellationReturn,commandDecision,allocationDecision} from '../scripts/design-rules.mjs';
const read=async name=>JSON.parse(await readFile(new URL(`../contracts/${name}`,import.meta.url),'utf8'));
const ajv=new Ajv2020({allErrors:true,strict:true});addFormats(ajv);
const validate=ajv.compile(await read('schemas/policy.schema.json'));
const draftValidate=ajv.compile(await read('schemas/policy-draft.schema.json'));
const road=await read('examples/motor-trade-road-risks.json');
for(const product of ['motor-trade-road-risks','motor-trade-combined','commercial-combined'])test(`${product} fixture satisfies issued schema`,async()=>{
 assert.ok(validate(await read(`examples/${product}.json`)),JSON.stringify(validate.errors));
});
test('draft accepts missing answers but issued contract does not',()=>{
 const draft={schemaVersion:'1.0',productCode:road.productCode,productVersionId:road.productVersionId,risk:{business:{}}};
 assert.ok(draftValidate(draft),JSON.stringify(draftValidate.errors));assert.equal(validate(draft),false);
});
test('product discriminator rejects CC with Motor Trade risk',()=>{const p=structuredClone(road);p.productCode='commercial-combined';assert.equal(validate(p),false);});
test('unknown sensitive fields cannot enter issued snapshots',()=>{const p=structuredClone(road);p.risk.supportFlags=[{category:'Health'}];assert.equal(validate(p),false);});
test('money precision and calendar errors are rejected',()=>{
 const p=structuredClone(road);p.premium.tax='144.001';assert.equal(validate(p),false);
 p.premium.tax='144.00';p.risk.drivers[0].dateOfBirth='1985-02-31';assert.equal(validate(p),false);
 assert.throws(()=>minorUnits('1.001'));assert.throws(()=>days('2026-02-31','2026-03-15'));
});
test('issue and cancellation monetary examples reconcile',()=>{
 const full=financials({annualPremium:'1200.00',effectiveDate:'2026-01-01',termStart:'2026-01-01',termEnd:'2027-01-01',fee:'35.00'});
 assert.deepEqual(full,{premium:'1200.00',tax:'144.00',fee:'35.00',brokerCommission:'120.00',grossPayable:'1379.00',netBrokerDue:'1259.00',insurerDue:'1224.00'});
 const cancel=financials({annualPremium:'-1200.00',effectiveDate:'2026-09-15',termStart:'2026-01-01',termEnd:'2027-01-01'});
 assert.equal(cancel.premium,'-355.07');assert.equal(cancel.tax,'-42.61');assert.equal(cancel.brokerCommission,'-35.51');assert.equal(cancel.netBrokerDue,'-362.17');
 assert.equal(minorUnits(full.netBrokerDue),minorUnits(full.insurerDue)+minorUnits(full.fee));
});
test('MTA and leap year bases are explicit, and negative half rounds away from zero',()=>{
 assert.equal(days('2024-01-01','2025-01-01'),366);assert.equal(days('2026-09-15','2027-01-01'),108);
 const mta=financials({annualPremium:'600.00',effectiveDate:'2026-09-15',termStart:'2026-01-01',termEnd:'2027-01-01',fee:'15.00'});
 assert.equal(mta.premium,'177.53');assert.equal(mta.tax,'21.30');assert.equal(mta.netBrokerDue,'196.08');
 assert.equal(roundRatio(-1n,2n),-1n);
});
test('effective history excludes drafts/future cover and respects processing knowledge',()=>{
 const versions=[{sequence:1,status:'issued',effectiveAt:'2026-01-01T00:00:00Z',recordedAt:'2025-12-20T10:00:00Z'},{sequence:2,status:'issued',effectiveAt:'2026-07-01T00:00:00+01:00',recordedAt:'2026-07-10T10:00:00Z'},{sequence:3,status:'draft',effectiveAt:'2026-07-02T00:00:00+01:00',recordedAt:'2026-07-11T10:00:00Z'},{sequence:4,status:'issued',effectiveAt:'2026-10-01T00:00:00+01:00',recordedAt:'2026-08-01T10:00:00Z'}];
 assert.equal(versionAt(versions,'2026-07-05T12:00:00Z','2026-07-05T12:00:00Z').sequence,1);
 assert.equal(versionAt(versions,'2026-07-05T12:00:00Z','2026-09-01T12:00:00Z').sequence,2);
 assert.equal(versionAt(versions,'2026-10-02T12:00:00Z','2026-09-01T12:00:00Z').sequence,4);
});
test('material revision invalidates rating and acceptance independently of approval',()=>{
 const input={revision:3,rating:{id:'r1',revision:2,expired:false},acceptance:{revision:2,ratingId:'r1'},referrals:[{status:'approved',conditionsSatisfied:true}],actorLimit:'5000.00',premium:'1200.00'};
 assert.deepEqual(issueErrors(input),['rating-not-current','terms-not-accepted']);
 input.rating.revision=3;input.acceptance.revision=3;input.referrals[0].conditionsSatisfied=false;input.actorLimit='0.00';
 assert.deepEqual(issueErrors(input),['referrals-unresolved','authority-exceeded']);
});
test('cancellation after an adjustment reverses unearned parts of each posted component once',()=>{
 const movements=[{startsOn:'2026-01-01',endsOn:'2027-01-01',premium:'1200.00',tax:'144.00',brokerCommission:'120.00'},
 {startsOn:'2026-09-15',endsOn:'2027-01-01',premium:'177.53',tax:'21.30',brokerCommission:'17.75'}];
 assert.deepEqual(cancellationReturn(movements,'2026-10-01'),{premium:'-453.70',tax:'-54.44',brokerCommission:'-45.37',netBrokerDue:'-462.77'});
 assert.equal(cancellationReturn(movements,'2027-01-01').netBrokerDue,'0.00');
 assert.equal(cancellationReturn([movements[0]],'2026-01-01').netBrokerDue,'-1224.00');
});
test('command replay precedes stale-write check, while changed intent conflicts',()=>{
 const request={requestHash:'hash-a',ifMatch:'"v1"',currentEtag:'"v2"'};
 assert.equal(commandDecision(request),'precondition-failed');
 assert.equal(commandDecision({...request,ifMatch:undefined}),'precondition-required');
 assert.equal(commandDecision({...request,ifMatch:'"v2"'}),'apply');
 assert.equal(commandDecision({...request,saved:{requestHash:'hash-a'}}),'replay');
 assert.equal(commandDecision({...request,saved:{requestHash:'hash-b'}}),'idempotency-conflict');
});
test('allocation decision checks both residuals and agency scope',()=>{
 const request={available:'340.00',outstanding:'500.00',amount:'400.00',receiptAgency:'a',invoiceAgency:'a'};
 assert.equal(allocationDecision(request),'invalid-allocation');
 assert.equal(allocationDecision({...request,amount:'340.00'}),'apply');
 assert.equal(allocationDecision({...request,amount:'340.00',outstanding:'300.00'}),'invalid-allocation');
 assert.equal(allocationDecision({...request,amount:'10.00',invoiceAgency:'b'}),'agency-mismatch');
});
test('local calendar earning is invariant across London 23 and 25 hour days',()=>{
 assert.equal((Date.parse('2026-03-30T00:00:00+01:00')-Date.parse('2026-03-29T00:00:00Z'))/3600000,23);
 assert.equal((Date.parse('2026-10-26T00:00:00Z')-Date.parse('2026-10-25T00:00:00+01:00'))/3600000,25);
 assert.equal(days('2026-03-29','2026-03-30'),1);
 assert.equal(days('2026-10-25','2026-10-26'),1);
});
test('dated slices of a single MTA do not introduce later cover before its effective date',()=>{
 const recordedAt='2026-09-13T12:00:00Z';
 const versions=[{sequence:7,transactionId:'mta-1',sliceOrdinal:1,status:'issued',effectiveAt:'2026-09-15T00:00:00+01:00',recordedAt,stockLimit:'75000.00'},
 {sequence:8,transactionId:'mta-1',sliceOrdinal:2,status:'issued',effectiveAt:'2026-10-01T00:00:00+01:00',recordedAt,stockLimit:'125000.00'}];
 assert.equal(versionAt(versions,'2026-09-20T12:00:00Z','2026-09-20T12:00:00Z').stockLimit,'75000.00');
 assert.equal(versionAt(versions,'2026-10-02T12:00:00Z','2026-10-02T12:00:00Z').stockLimit,'125000.00');
 assert.equal(versionAt(versions,'2026-10-02T12:00:00Z','2026-09-12T12:00:00Z'),null);
});
