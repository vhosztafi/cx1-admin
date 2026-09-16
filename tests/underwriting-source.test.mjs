import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import {conditionSchema} from '../scripts/underwriting-contract-model.mjs';
import {financials} from '../scripts/design-rules.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../${path}`,import.meta.url),'utf8'));
const phase='.planning/phases/06-underwriting-and-first-policy-issue/';
const mapping=await read(`${phase}06-INPUT-MAP.json`);
const references=await read('contracts/reference-data/motor-trade-capture.json');
const schema=await read('contracts/schemas/quote-ready.schema.json');
const fixtures=await read('contracts/examples/underwriting-demo.json');

function resolve(path){
  let node=schema;
  for(const token of path.slice(1).split('/')){
    while(node?.$ref){node=node.$ref.slice(2).split('/').reduce((v,k)=>v[k],schema);}
    node=node?.properties?.[token];
  }
  return node;
}
test('underwriting input paths use real capture schema and never invent a loss or driver-basis field',()=>{
  for(const input of mapping.inputs)assert.equal(Boolean(resolve(input.proposalPath)),input.schemaPathExists,input.code);
  assert.equal(resolve('/risk/losses'),undefined);
  assert.equal(resolve('/risk/driverBasis'),undefined);
  assert.ok(mapping.inputs.find(x=>x.code==='any-driver-count').interpretation.includes('MTS-06-Q02'));
});
test('numeric reference identities and dependent option bindings retain the pinned source catalog',async()=>{
  const raw=await readFile(new URL('../contracts/reference-data/motor-trade-capture.json',import.meta.url));
  assert.equal(createHash('sha256').update(raw).digest('hex'),mapping.sourceFiles['contracts/reference-data/motor-trade-capture.json']);
  assert.equal(mapping.referenceVersion,references.version);
  assert.deepEqual(mapping.referenceBindings,references.bindings);
  for(const [name,rows] of Object.entries(mapping.trustedCollections))for(const row of rows){
    const actual=references.collections[name].find(v=>v.value===row.value);
    assert.ok(actual,`${name}/${row.value}`);
    if('numericValue' in row)assert.equal(row.numericValue,actual.numericValue);
  }
  for(const config of fixtures.configurations.filter(x=>x.kind==='rating')){
    assert.equal(config.referenceVersion,references.version);
    assert.ok(config.valetingTradeValues.every(value=>references.collections.mtOccupations.some(x=>x.value===value&&/valet/i.test(x.text))));
  }
});
test('source table-only underwriting rules and requested section gaps have named execution ownership',async()=>{
  const source=await readFile(new URL('../docs/design/source/prototype-template.txt',import.meta.url),'utf8');
  for(const text of ['UW-09','UW-22','Business trading fewer than five years','W-07','Tools & equipment'])assert.ok(source.includes(text),text);
  assert.deepEqual(mapping.captureAdditionsRequired.map(x=>x.code),['stock-custody','premises','tools-equipment']);
  for(const section of mapping.captureAdditionsRequired)assert.ok(resolve(section.proposalPath),section.proposalPath);
  for(const rule of mapping.sourceRowRules)assert.match(rule.owner,/^06-\d{2}/);
});
test('every retained source question and Phase6 operation dependency still resolves exactly',async()=>{
  const catalogs=new Map();
  for(const row of mapping.sourceQuestions){
    if(!catalogs.has(row.sourceFile))catalogs.set(row.sourceFile,(await read(row.sourceFile)).questions);
    const {sourceFile,...expected}=row;
    assert.deepEqual(catalogs.get(sourceFile).find(q=>q.questionId===row.questionId),expected,row.questionId);
  }
  const audit=await read(`${phase}06-SOURCE-AUDIT.json`),api=await read('contracts/openapi.json');
  const ids=new Set(Object.values(api.paths).flatMap(p=>Object.values(p)).map(o=>o.operationId));
  assert.equal(audit.controls.length,211);
  for(const control of audit.controls){
    if(control.placementOwnerPhase===6)assert.match(control.task,/^06-\d{2}-01$/);
    for(const owner of control.operationOwners)if(owner.phase===6){assert.ok(ids.has(owner.operationId),owner.operationId);assert.ok(owner.task);}
  }
});
test('condition catalog rejects arbitrary wording, effects, empty targets and foreign-shaped commands',()=>{
  const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);const validate=ajv.compile(conditionSchema);
  const id='10000000-0000-4000-8000-000000000001';
  for(const value of [
    {code:'provide-driver-proof',driverId:id,requirementCode:'driving-licence'},
    {code:'provide-premises-security',premisesId:id},
    {code:'provide-signed-statement',termsVersionId:id,termsHash:'a'.repeat(64)},
    {code:'provide-trading-history'},
    {code:'overnight-security',premisesId:id,wordingVersion:'1'},
    {code:'named-drivers-only',driverIds:[id],wordingVersion:'1'},
    {code:'revise-stock-limit',maximumAmount:'125000.00'},
    {code:'revise-vehicle-limit',vehicleId:id,maximumAmount:'50000.00'},
  ]){
    assert.ok(validate(value),JSON.stringify(validate.errors));
    assert.equal(validate({...value,premium:'0.01'}),false);
    assert.equal(validate({...value,description:'Arbitrary authority override'}),false);
  }
  assert.equal(validate({code:'named-drivers-only',driverIds:[],wordingVersion:'1'}),false);
  assert.equal(validate({code:'provide-signed-statement',termsVersionId:id,termsHash:'not-a-hash'}),false);
});
test('worked examples independently reconcile exact pennies and existing short-period arithmetic',()=>{
  const pennies=v=>BigInt(v.replace('.',''));
  for(const example of fixtures.pricingExamples)
    assert.equal(pennies(example.termPremium)+pennies(example.tax)+pennies(example.fee),pennies(example.gross),example.name);
  for(const example of fixtures.postingExamples){
    assert.equal(pennies(example.debtorDue),pennies(example.insurerDue)+pennies(example.feeIncome)+pennies(example.brokerPayable),example.name);
    assert.equal(pennies(example.fee),pennies(example.feeIncome)+pennies(example.feeShare),example.name);
    assert.equal(pennies(example.premium)+pennies(example.tax)-pennies(example.commission),pennies(example.insurerDue),example.name);
  }
  const term=financials({annualPremium:'1200.00',effectiveDate:'2026-01-01',termStart:'2026-01-01',termEnd:'2026-06-30'});
  assert.equal(term.premium,'591.78');assert.equal(term.tax,'71.01');
  const rule=fixtures.configurations.find(x=>x.kind==='rating'&&x.productCode==='motor-trade-road-risks');
  const portion=(amount,bps)=>(amount*BigInt(bps)+5000n)/10000n;
  for(const example of fixtures.pricingExamples.filter(x=>x.factorInputs)){
    const input=example.factorInputs,base=pennies(input.subtotal)+(input.tools?pennies(rule.toolsPremium):0n);
    const total=base+(input.valeting?portion(base,rule.valetingLoadingBps):0n)+(input.youngDriver?portion(base,rule.youngDriverLoadingBps):0n)-(input.ncd?portion(base,rule.noClaimsDiscountBps):0n);
    const final=total<pennies(rule.minimumPremium)?pennies(rule.minimumPremium):total;
    assert.equal(final,pennies(example.annualPremium),example.name);
    assert.equal(portion(final,rule.taxRateBps),pennies(example.tax),example.name);
  }
});
