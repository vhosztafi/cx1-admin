import test from 'node:test';
import assert from 'node:assert/strict';
import {commercialPropertySummary, commercialPolicyCoverage, formatCommercialMoney} from '../lib/commercial-policy.ts';

test('commercial property totals and demo MEL use exact selected-version amounts', () => {
 const locations=[{id:'a',buildings:'0.10',contents:'0.20',stock:'0.01',maximumEstimatedLoss:'999.00'}, {id:'b',buildings:'1.00',contents:'0.00',stock:'0.00'}];
 assert.deepEqual(commercialPropertySummary(locations),{total:'1.31',demoMel:'1.00',locationTotals:['0.31','1.00']});
 assert.deepEqual(commercialPropertySummary([]),{total:undefined,demoMel:undefined,locationTotals:[]});
 assert.equal(commercialPropertySummary([{id:'c',buildings:'100.00'}]).total,undefined);
 assert.equal(commercialPropertySummary([{id:'x',buildings:'9999999999999.99',contents:'9999999999999.99',stock:'9999999999999.99'}]).total,'29999999999999.97');
 assert.equal(formatCommercialMoney('2999999999999999.99'),'£2,999,999,999,999,999.99');
 assert.equal(formatCommercialMoney('-0.01'),'−£0.01');assert.equal(formatCommercialMoney('1.001'),'Unavailable');
});
test('commercial coverage preserves cancellation and scheduled states',()=>{
 assert.equal(commercialPolicyCoverage('cancelled'),'Cancelled'); assert.equal(commercialPolicyCoverage('scheduled'),'Inception scheduled');
 assert.equal(commercialPolicyCoverage('active'),'In force'); assert.equal(commercialPolicyCoverage('expired'),'Term ended');
});
