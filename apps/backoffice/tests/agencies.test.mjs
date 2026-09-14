import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { basisPoints, exactMoney, draftPayload, flattenDraft, canReadAgencies, canWriteAgencies, AgencyError, agencyFetch } from '../lib/agencies.ts';
import { agencyFields } from '../lib/agency-fields.ts';
test('agency inputs preserve omitted answers, complete money precision and source mode distinctions', () => {
  const payload=draftPayload({'legalName':' Fictional Agency ','commercialTerms.volumeCommitmentMode':'target-tiered','commercialTerms.volumeCommitment':'99999999999999999.99','commercialTerms.flatCommissionBasisPoints':'12.50','mainContact.name':'','territory':'NI'},agencyFields);
  assert.equal(payload.legalName,'Fictional Agency'); assert.equal(payload.mainContact,undefined); assert.equal(payload.commercialTerms.volumeCommitment,'99999999999999999.99');assert.equal(payload.commercialTerms.flatCommissionBasisPoints,1250);
  assert.equal(flattenDraft(payload)['commercialTerms.flatCommissionBasisPoints'],'12.50');assert.equal(payload.territory,'NI');
  assert.equal(exactMoney('25'),'25.00');assert.equal(basisPoints('0.01'),1);assert.equal(basisPoints('100'),10000);
  for(const input of ['-1','1.001','1e3','NaN','1,000','100000000000000000'])assert.throws(()=>exactMoney(input));
  for(const input of ['100.01','999','12.555'])assert.throws(()=>basisPoints(input));
});
test('wizard renders every fixed source option family through reviewed field paths',async()=>{
  const mappings=JSON.parse(await readFile(new URL('../../../contracts/examples/agency-option-mapping.json',import.meta.url),'utf8'));
  const targets=JSON.parse(await readFile(new URL('../../../contracts/examples/agency-option-targets.json',import.meta.url),'utf8'));
  for(const [label,mapping] of Object.entries(mappings)){const field=agencyFields.find(x=>x.path===targets[label]);assert.ok(field,label);assert.deepEqual(field.options,mapping,label);}
  assert.equal(new Set(agencyFields.map(x=>x.path)).size,agencyFields.length);
});
test('agency UI access and safe error feedback follow the implemented API permissions',async()=>{
  assert.equal(canReadAgencies(['underwriter']),true);assert.equal(canWriteAgencies(['underwriter']),false);assert.equal(canReadAgencies(['servicing']),false);assert.equal(canWriteAgencies(['broker-admin']),false);assert.equal(canWriteAgencies(['system-admin']),true);
  assert.match(new AgencyError(412).message,/retained/);
  const previous=globalThis.fetch;globalThis.fetch=async()=>new Response('{"secret":"private"}',{status:503});
  try{await assert.rejects(()=>agencyFetch('/api/v1/agencies'),error=>error.status===503&&!error.message.includes('private'));}finally{globalThis.fetch=previous;}
});
