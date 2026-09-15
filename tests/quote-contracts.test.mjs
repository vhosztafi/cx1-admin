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

test('European cover retains separate stable annual and temporary rows without UI-only flags',()=>{
  const annual={id,registration:'DEMO 01',usage:selection};
  const temporary={id,registration:'DEMO 02',startsOn:'2026-10-01',endsOn:'2026-10-14',area:selection,driverIds:[id],cover:selection,usage:selection};
  const proposal={...base(),cover:{annualEuropeanCover:[annual],temporaryEuropeanCover:[temporary]}};
  assert.equal(draft(proposal),true,JSON.stringify(draft.errors));
  temporary.driverIds.push(id);assert.equal(draft(proposal),false);
  temporary.driverIds.pop();temporary.startsOn='2026-02-30';assert.equal(draft(proposal),false);
  temporary.startsOn='2026-10-01';temporary.isDraft=false;assert.equal(draft(proposal),false);
  delete temporary.isDraft;temporary.europeanCoverDriverNames=['Caller supplied name'];assert.equal(draft(proposal),false);
  delete temporary.europeanCoverDriverNames;delete annual.id;assert.equal(draft(proposal),false);
});

test('complete European cover row shapes require their source fields',async()=>{
  const schema=await read('schemas/quote-ready.schema.json');
  const validate=ajv.compile({$schema:schema.$schema,$defs:schema.$defs,$ref:'#/$defs/TemporaryEuropeanCover'});
  const row={id,registration:'DEMO 02',startsOn:'2026-10-01',endsOn:'2026-10-14',area:selection,driverIds:[],cover:selection,usage:selection};
  assert.equal(validate(row),true,JSON.stringify(validate.errors));
  for(const key of ['id','registration','startsOn','endsOn','area','driverIds','cover','usage']) {
    const missing=structuredClone(row);delete missing[key];assert.equal(validate(missing),false,key);
  }
  // Empty selection is structurally valid; named/any-driver applicability and
  // same-proposal membership are mandatory runtime checks, not schema claims.
});

test('every source European trip child field maps to a concrete quote contract path',async()=>{
  const source=JSON.parse(await readFile(new URL('../docs/design/funnel-field-mapping.json',import.meta.url),'utf8'));
  const mapping=await read('quote-field-mapping.json');
  const schema=await read('schemas/quote-ready.schema.json');
  const sourceRows=source.mappings.filter(row=>row.rawPath.startsWith('europeanCoverAnnualExtras[]')||row.rawPath.startsWith('europeanCoverTemporaryExtras[]'));
  const rows=mapping.mappings.filter(row=>sourceRows.some(original=>original.owner===row.owner));
  assert.equal(sourceRows.length,9);assert.equal(rows.length,sourceRows.length);
  assert.equal(new Set(rows.map(row=>row.owner)).size,rows.length);
  for(const original of sourceRows)assert.equal(rows.find(row=>row.owner===original.owner)?.rawPath,original.rawPath);
  for(const row of rows) {
    assert.ok(row.canonicalPath,row.owner);
    let node=schema;
    for(const part of row.canonicalPath.split('.')) {
      while(node.$ref)node=schema.$defs[node.$ref.split('/').at(-1)];
      const list=part.endsWith('[]');node=node.properties?.[part.replace(/\[\]$/,'')];
      assert.ok(node,`${row.owner}: ${row.canonicalPath}`);
      if(list){assert.equal(node.type,'array');node=node.items;}
    }
    if(row.contractKind==='string')assert.equal(node.type,'string');
    else assert.equal(node.$ref,`#/$defs/${row.contractKind}`);
  }
});

test('all55driver source fields map to structural fields or identified typed answers',async()=>{
  const source=JSON.parse(await readFile(new URL('../docs/design/funnel-field-mapping.json',import.meta.url),'utf8'));
  const mapping=await read('quote-field-mapping.json');
  const schema=await read('schemas/quote-ready.schema.json');
  const original=source.mappings.filter(row=>row.owner.startsWith('MTS-06-Q')&&Number(row.owner.split('Q')[1])>=8);
  assert.equal(original.length,55);
  const rows=mapping.mappings.filter(row=>original.some(x=>x.owner===row.owner));
  assert.equal(rows.length,55);assert.equal(new Set(rows.map(row=>row.owner)).size,55);
  for(const input of original) {
    const row=rows.find(x=>x.owner===input.owner);assert.equal(row.rawPath,input.rawPath);
    assert.equal(row.optionCollection,input.optionCollection);
    let node=schema;
    for(const part of row.canonicalPath.split('.')) {
      while(node.$ref)node=schema.$defs[node.$ref.split('/').at(-1)];
      node=node.properties?.[part.replace(/\[\]$/,'')];assert.ok(node,`${row.owner}: ${row.canonicalPath}`);
      if(part.endsWith('[]')){assert.equal(node.type,'array');node=node.items;}
    }
    if(['string','boolean','integer'].includes(row.contractKind))assert.equal(node.type,row.contractKind);
    else assert.equal(node.$ref,`#/$defs/${row.contractKind}`);
    if(row.contractKind==='Answer') {
      assert.equal(row.questionId,row.owner);
      assert.equal(schema.$defs.Answer.oneOf.some(branch=>branch.properties.kind.const===row.answerKind),true,row.owner);
    }
  }
});

test('licence issue date, declared claim status and address components are retained independently',()=>{
  const proposal={...base(),risk:{drivers:[{id,licence:{issuedOn:'2004-01-01',testDate:'2003-12-01'},address:{houseNumber:'12B',street:'Example Road',city:'Example City',town:'Example Town',line1:'12B Example Road'},convictions:[{id,disqualified:false,banMonths:0}],losses:[{id,status:'open',declaredStatus:selection}]}]}};
  assert.equal(draft(proposal),true,JSON.stringify(draft.errors));
  assert.notEqual(proposal.risk.drivers[0].licence.issuedOn,proposal.risk.drivers[0].licence.testDate);
  proposal.risk.drivers[0].losses[0].declaredStatus={value:1};assert.equal(draft(proposal),false);
  proposal.risk.drivers[0].losses[0].declaredStatus=selection;
  proposal.risk.drivers[0].convictions[0].disqualified='No';assert.equal(draft(proposal),false);
});
