import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';

const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);
const draft=ajv.compile(await read('schemas/quote-draft.schema.json'));
const ready=ajv.compile(await read('schemas/quote-ready.schema.json'));
const base=()=>({schemaVersion:'1.0',productCode:'motor-trade-road-risks'});
const id='00000000-0000-4000-8000-000000000099';
const selection={collection:'occupations',value:1,label:'Fictional occupation',version:'demo-1'};

test('quote capture rejects policy authority, ownership and unsupported products even in drafts',()=>{
  assert.equal(draft(base()),true);
  for(const key of ['premium','settlement','provenance','productVersionId','agencyId','state','term','ratingResultId'])
    assert.equal(draft({...base(),[key]:key.endsWith('Id')?id:{}}),false,key);
  for(const key of ['clientId','clientAgencyRelationshipId'])
    assert.equal(draft({...base(),insured:{[key]:id}}),false,key);
  assert.equal(draft({...base(),productCode:'commercial-combined'}),false);
  assert.equal(draft({...base(),risk:{supportFlags:[]}}),false);
  assert.equal(draft({...base(),risk:{previousInsurance:{evidenceDocumentIds:[id]}}}),false);
});

test('partial quote children require stable IDs and typed answer/reference selections',()=>{
  assert.equal(draft({...base(),risk:{drivers:[{id}]}}),true);
  assert.equal(draft({...base(),risk:{drivers:[{fullName:'Fictional driver'}]}}),false);
  assert.equal(draft({...base(),risk:{drivers:[{id,occupations:[{}]}]}}),false);
  assert.equal(draft({...base(),risk:{drivers:[{id,occupations:[{id,businessUseRequired:false}]}]}}),true);
  assert.equal(draft({...base(),risk:{business:{responses:{answers:[{questionId:'declared',kind:'boolean'}]}}}}),false);
  assert.equal(draft({...base(),risk:{business:{responses:{answers:[{questionId:'declared',kind:'boolean',value:false}]}}}}),true);
  assert.equal(draft({...base(),risk:{drivers:[{id,relationship:{value:1}}]}}),false);
  assert.equal(draft({...base(),risk:{drivers:[{id,relationship:selection}]}}),true);
});

test('driver repeated histories preserve typed dates, money and distinct sentence components',()=>{
  const driver={id,occupations:[{id,occupation:selection,businessUseRequired:false}],criminalConvictions:[{id,occurredOn:'2020-01-01',code:selection,sentenceYears:2,sentenceMonths:6}],countyCourtJudgments:[{id,occurredOn:'2021-02-01',status:selection,amount:'123.45',circumstances:'Fictional dispute'}]};
  const proposal={...base(),risk:{drivers:[driver]}};
  assert.equal(draft(proposal),true,JSON.stringify(draft.errors));
  driver.criminalConvictions[0].sentenceMonths=12;assert.equal(draft(proposal),false);
  driver.criminalConvictions[0].sentenceMonths=6;driver.countyCourtJudgments[0].amount='123.456';assert.equal(draft(proposal),false);
  driver.countyCourtJudgments[0].amount='123.45';driver.countyCourtJudgments[0].occurredOn='2021-02-30';assert.equal(draft(proposal),false);
  driver.countyCourtJudgments[0].occurredOn='2021-02-01';driver.countyCourtJudgments[0].approved=true;assert.equal(draft(proposal),false);
});

test('both Motor Trade fixture shapes convert without copying policy evidence or authority',async()=>{
  for(const product of ['motor-trade-road-risks','motor-trade-combined']) {
    const policy=await read(`examples/${product}.json`);
    const {clientId,clientAgencyRelationshipId,...insured}=policy.insured;
    const risk=structuredClone(policy.risk);delete risk.previousInsurance.evidenceDocumentIds;
    const proposal={schemaVersion:'1.0',productCode:product,insured,risk,cover:policy.cover,termIntent:{kind:'annual',localStartDate:'2026-01-01',localStartTime:'00:00',timeZone:'Europe/London'}};
    assert.equal(ready(proposal),true,JSON.stringify(ready.errors));
    assert.equal(draft(proposal),true,JSON.stringify(draft.errors));
    proposal.termIntent.kind='short-period';assert.equal(ready(proposal),false);
    Object.assign(proposal.termIntent,{localEndDate:'2026-04-01',localEndTime:'00:00'});assert.equal(ready(proposal),true);
    proposal.termIntent.localStartTime='24:00';assert.equal(draft(proposal),false);
    proposal.termIntent.localStartTime='00:00';proposal.termIntent.utcOffsetMinutes=120;assert.equal(draft(proposal),false);
  }
});
