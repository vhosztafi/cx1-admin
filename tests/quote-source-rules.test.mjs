import test from 'node:test';
import assert from 'node:assert/strict';
import {validateQuoteSourceRules} from '../scripts/quote-source-rules.mjs';
const id=n=>`aaaaaaaa-0000-4000-8000-${String(n).padStart(12,'0')}`;
const check=proposal=>validateQuoteSourceRules(proposal,'2026-09-15');
const code=value=>({collection:'test',value,label:'Example',version:'test-1'});

test('vehicle, plate and annual European registrations reject normalized duplicates in their own lists',()=>{
 const proposal={risk:{vehicles:[{id:id(1),registration:'DEMO 01'},{id:id(2),registration:'DEMO01'}],tradePlates:[{id:id(3),number:'AB 123'},{id:id(4),number:'AB123'}]},cover:{annualEuropeanCover:[{id:id(5),registration:'DEMO 02'},{id:id(6),registration:'demo02'}]}};
 assert.deepEqual(check(proposal).map(issue=>issue.path),['/risk/vehicles/1/registration','/risk/tradePlates/1/number','/cover/annualEuropeanCover/1/registration']);
});

test('trade plate limits distinguish number covered from recorded rows and retained inactive data',()=>{
 const covered={questionId:'MTS-07-Q01',kind:'boolean',value:true};
 const count={questionId:'MTS-07-Q02',kind:'count',value:2};
 const proposal={risk:{responses:{answers:[covered,count]},tradePlates:[{id:id(1),number:'A1'},{id:id(2),number:'A2'}]}};
 assert.deepEqual(check(proposal),[]);
 count.value=1;assert.equal(check(proposal)[0].code,'trade-plate-count-below-rows');
 count.value=1000;assert.equal(check(proposal)[0].code,'trade-plate-count-required');
 count.value=2;covered.value=false;assert.equal(check(proposal)[0].code,'inactive-trade-plate-data');
 assert.equal(check({risk:{tradePlates:Array.from({length:151},(_,i)=>({id:id(i),number:`A${i}`}))}})[0].code,'trade-plate-row-limit');
});

test('business start and activity totals retain typed identity and incomplete share issues',()=>{
 const activity={id:id(1),code:code(1),turnoverBasisPoints:10000};
 const proposal={termIntent:{localStartDate:'2026-09-01'},risk:{business:{startedOn:'2026-09-02',activities:[activity]}}};
 assert.equal(check(proposal)[0].code,'business-start-after-policy');
 proposal.risk.business.startedOn='2020-01-01';assert.deepEqual(check(proposal),[]);
 activity.turnoverBasisPoints=9000;assert.equal(check(proposal)[0].code,'activity-total-must-equal-100-percent');
 delete activity.turnoverBasisPoints;assert.equal(check(proposal)[0].code,'activity-share-required');
 activity.turnoverBasisPoints=5000;proposal.risk.business.activities.push({...activity,id:id(2)});
 assert.equal(check(proposal)[0].code,'duplicate-business-activity');
 proposal.risk.business.activities[1].code=code('1');assert.deepEqual(check(proposal),[]);
});

test('licence issue and UK residency chronology do not overwrite captured dates',()=>{
 const residency={questionId:'MTS-06-Q22',kind:'date',value:'2007-12-31'};
 const driver={id:id(1),dateOfBirth:'2008-01-01',licence:{issuedOn:'2024-12-31'},responses:{answers:[residency]}};
 const proposal={risk:{drivers:[driver]}};const before=structuredClone(proposal);
 assert.deepEqual(check(proposal).map(issue=>issue.code),['licence-before-seventeenth-birthday','residency-before-birth']);assert.deepEqual(proposal,before);
 driver.licence.issuedOn='2007-01-01';assert.equal(check(proposal)[0].code,'licence-before-seventeenth-birthday');
 driver.licence.issuedOn='2025-01-01';residency.value='2026-09-16';assert.equal(check(proposal)[0].code,'residency-in-future');
 residency.value='2026-09-15';assert.deepEqual(check(proposal),[]);
});

test('future purchase restriction follows source ordinary versus specified vehicle branches',()=>{
 const vehicle={id:id(1),purchasedOn:'2026-09-16'};
 assert.equal(check({risk:{vehicles:[vehicle]}})[0].code,'purchase-in-future');
 assert.deepEqual(check({risk:{vehicles:[vehicle],specifiedVehicleIds:[id(1).toUpperCase()]}}),[]);
 vehicle.purchasedOn='2026-09-15';assert.deepEqual(check({risk:{vehicles:[vehicle]}}),[]);
});

test('modification identity is unique per vehicle and validation requires a trusted as-of date',()=>{
 const modifications=[{id:id(2),code:code(1)},{id:id(3),code:code(1)}];
 const proposal={risk:{vehicles:[{id:id(1),modifications}]}};
 assert.equal(check(proposal)[0].code,'duplicate-vehicle-modification');
 modifications[1].code=code('1');assert.deepEqual(check(proposal),[]);
 assert.throws(()=>validateQuoteSourceRules(proposal,'2026-02-30'),/trusted-as-of-date-required/);
});
