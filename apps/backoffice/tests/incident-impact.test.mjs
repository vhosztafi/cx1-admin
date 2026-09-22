import test from 'node:test';
import assert from 'node:assert/strict';
import {incidentPremiumContribution} from '../lib/incident-impact.ts';

test('reported incurred contribution uses exact money and explicit percent rounding',()=>{
 assert.equal(incidentPremiumContribution('6500.00','12000.00'),'54.17');
 assert.equal(incidentPremiumContribution('0.00','12000.00'),'0.00');
 assert.equal(incidentPremiumContribution('0.01','8.00'),'0.13');
 assert.equal(incidentPremiumContribution('9999999999999.99','0.01'),'99999999999999900.00');
});
test('unknown amounts and zero premium never invent a zero loss ratio',()=>{
 for(const values of [[null,'12000.00'],[undefined,'12000.00'],['6500.00',null],['6500.00','0.00']])assert.equal(incidentPremiumContribution(...values),null);
});
test('invalid provider amounts cannot yield a displayed contribution',()=>{
 for(const value of ['-1.00','1','1.001',' 1.00','1.00\n','10000000000000.00']){
  assert.equal(incidentPremiumContribution(value,'10.00'),null);
  assert.equal(incidentPremiumContribution('1.00',value),null);
 }
});
