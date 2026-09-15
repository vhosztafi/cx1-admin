import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {ageOn,selectQuoteDynamicOptions} from '../scripts/quote-dynamic-options.mjs';
import {validateQuoteReferences} from '../scripts/quote-semantic-contract.mjs';
const catalogue=JSON.parse(await readFile(new URL('../contracts/reference-data/motor-trade-capture.json',import.meta.url),'utf8'));
const ref=(collection,value)=>{const row=catalogue.collections[collection].find(row=>row.value===value);assert.ok(row,`${collection}:${value}`);return {collection,value,label:row.text,version:catalogue.version};};
const answer=(questionId,value)=>({questionId,kind:'reference',value});
const base=()=>({termIntent:{localStartDate:'2026-01-01'},cover:{responses:{answers:[answer('MTS-05-Q01',ref('coverLevels',1)),answer('MTS-05-Q02',ref('indemnityOwnVehicles',2))]}},risk:{business:{activities:[{code:ref('mtOccupations',5)}]},drivers:[]}});

test('driver age uses complete calendar anniversaries including leap-day clamping',()=>{
  assert.equal(ageOn('2008-01-02','2026-01-01'),17);
  assert.equal(ageOn('2008-01-01','2026-01-01'),18);
  assert.equal(ageOn('2008-02-29','2026-02-28'),18);
  for(const dob of ['2026-02-01','2008-02-30','missing'])assert.equal(ageOn(dob,'2026-01-01'),undefined);
});

test('excess context is derived from the exact trusted own-indemnity selection',()=>{
  const proposal=base();
  proposal.cover.responses.answers.push(answer('MTS-05-Q04',ref('indemnityOwnVehicles/number:2/excesses',4)));
  const result=selectQuoteDynamicOptions(proposal,catalogue);
  assert.deepEqual(result.issues,[]);
  assert.deepEqual(result.selectedCollections,{'/cover/responses/answers/2/value':['indemnityOwnVehicles/number:2/excesses']});
  assert.deepEqual(validateQuoteReferences(proposal,catalogue,result.selectedCollections),[]);
  proposal.cover.responses.answers[1].value.label='Forged';
  assert.equal(selectQuoteDynamicOptions(proposal,catalogue).issues[0].code,'missing-dynamic-dependency');
});

test('fire/theft limits and third-party-only applicability cannot be bypassed by a catalogue-valid excess',()=>{
  const proposal=base();const high=catalogue.collections.indemnityOwnVehicles.find(row=>row.numericValue>15000);
  proposal.cover.responses.answers[0].value=ref('coverLevels',2);
  proposal.cover.responses.answers[1].value=ref('indemnityOwnVehicles',high.value);
  const collection=`indemnityOwnVehicles/number:${high.value}/excesses`;
  proposal.cover.responses.answers.push(answer('MTS-05-Q04',ref(collection,catalogue.collections[collection][0].value)));
  assert.equal(selectQuoteDynamicOptions(proposal,catalogue).issues[0].code,'missing-dynamic-dependency');
  proposal.cover.responses.answers[0].value=ref('coverLevels',3);
  assert.equal(selectQuoteDynamicOptions(proposal,catalogue).issues[0].code,'inactive-dynamic-answer');
});

test('young-driver CC bands derive separately for each driver and expire at age25',()=>{
  const proposal=base();
  for(const [birth,band] of [['2008-01-01',0],['2006-01-01',1],['2004-01-01',2],['2002-01-01',3]]) {
    const collection=`youngDriverConfiguration/${band}/cCs`;
    proposal.risk.drivers.push({dateOfBirth:birth,responses:{answers:[answer('MTS-06-Q60',ref(collection,catalogue.collections[collection][0].value))]}});
  }
  const before=structuredClone(proposal);const result=selectQuoteDynamicOptions(proposal,catalogue);
  assert.deepEqual(result.issues,[]);assert.equal(Object.keys(result.selectedCollections).length,4);
  assert.deepEqual(validateQuoteReferences(proposal,catalogue,result.selectedCollections),[]);assert.deepEqual(proposal,before);
  proposal.risk.drivers[0].dateOfBirth='2001-01-01';
  assert.equal(selectQuoteDynamicOptions(proposal,catalogue).issues[0].code,'inactive-dynamic-answer');
});

test('young-driver indemnity is bounded by trusted policy limits and requires complete activity context',()=>{
  const proposal=base();const collection='youngDriverConfiguration/0/indemnities';
  proposal.risk.drivers=[{dateOfBirth:'2008-01-01',responses:{answers:[answer('MTS-06-Q59',ref(collection,1))]}}];
  assert.equal(selectQuoteDynamicOptions(proposal,catalogue).issues[0].code,'indemnity-exceeds-policy-limit');
  const adequate=catalogue.collections.indemnityOwnVehicles.find(row=>row.numericValue===10000);
  proposal.cover.responses.answers[1].value=ref('indemnityOwnVehicles',adequate.value);
  assert.deepEqual(selectQuoteDynamicOptions(proposal,catalogue).issues,[]);
  proposal.risk.business.activities=[];
  assert.equal(selectQuoteDynamicOptions(proposal,catalogue).issues[0].code,'missing-dynamic-dependency');
  proposal.risk.business.activities=[{code:ref('mtOccupations',1)}];
  assert.equal(selectQuoteDynamicOptions(proposal,catalogue).issues[0].code,'missing-dynamic-dependency');
});

test('customer-vehicle limits affect driver indemnity only for an eligible declared activity',()=>{
  const proposal=base();const collection='youngDriverConfiguration/0/indemnities';
  const customer=catalogue.collections.indemnityCustomerVehicles.find(row=>row.numericValue===15000);
  proposal.cover.responses.answers.push(answer('MTS-05-Q03',ref('indemnityCustomerVehicles',customer.value)));
  proposal.risk.drivers=[{dateOfBirth:'2008-01-01',responses:{answers:[answer('MTS-06-Q59',ref(collection,2))]}}];
  assert.equal(selectQuoteDynamicOptions(proposal,catalogue).issues[0].code,'indemnity-exceeds-policy-limit');
  proposal.risk.business.activities[0].code=ref('mtOccupations',1);
  assert.deepEqual(selectQuoteDynamicOptions(proposal,catalogue).issues,[]);
  proposal.cover.responses.answers[2].value.value=String(customer.value);
  assert.equal(selectQuoteDynamicOptions(proposal,catalogue).issues[0].code,'missing-dynamic-dependency');
});
