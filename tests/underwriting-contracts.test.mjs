import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);
const config=ajv.compile(await read('schemas/underwriting-config.schema.json'));
const fixture=await read('examples/underwriting-demo.json');
const draft=ajv.compile(await read('schemas/quote-draft.schema.json'));
const id='10000000-0000-4000-8000-000000000001';
const proposed=section=>({schemaVersion:'1.0',productCode:'motor-trade-combined',cover:{requestedSections:[section]}});

test('both versioned fictional rating configurations and multidimensional authority fixtures satisfy the closed contract',()=>{
  for(const item of fixture.configurations)assert.ok(config(item),JSON.stringify(config.errors));
  assert.deepEqual(fixture.configurations.filter(x=>x.kind==='rating').map(x=>x.productCode),['motor-trade-road-risks','motor-trade-combined']);
});
test('rating definitions reject hidden/unknown inputs, missing source factors and malformed money or rates',()=>{
  const sample=fixture.configurations.find(x=>x.kind==='rating');
  for(const key of ['supportFlags','manualPremium','actorId'])assert.equal(config({...sample,[key]:true}),false,key);
  for(const key of ['quoteValidityDays','valetingLoadingBps','youngDriverLoadingBps','noClaimsDiscountBps','toolsPremium']){
    const copy=structuredClone(sample);delete copy[key];assert.equal(config(copy),false,key);
  }
  for(const value of ['-1.00','1','1.234','1e3','NaN','999999999999999999999.00'])assert.equal(config({...sample,fee:value}),false,value);
  assert.equal(config({...sample,taxRateBps:10001}),false);
  assert.equal(config({...sample,quoteValidityDays:0}),false);
  assert.equal(config({...sample,schemaVersion:'2'}),false);
});
test('authority cannot silently omit any required dimension or gain a generic override',()=>{
  const sample=fixture.configurations.find(x=>x.kind==='authority');
  for(const key of Object.keys(sample.limits)){
    const copy=structuredClone(sample);delete copy.limits[key];assert.equal(config(copy),false,key);
  }
  assert.equal(config({...sample,limits:{...sample.limits,overrideAll:true}}),false);
  assert.equal(config({...sample,limits:{...sample.limits,stockLimit:null}}),false);
});
test('requested cover sections preserve user intent without granting issued cover authority',()=>{
  for(const section of [
    {id,code:'stock-custody',selected:true,limit:'150000.00',excess:'750.00',anyOneVehicleLimit:'35000.00'},
    {id,code:'premises',selected:true,limit:'250000.00',excess:'500.00',premisesIds:[id]},
    {id,code:'tools-equipment',selected:true,limit:'5000.00',excess:'250.00'},
    {id,code:'stock-custody',selected:false},
  ])assert.ok(draft(proposed(section)),JSON.stringify(draft.errors));
  for(const field of ['sections','endorsements','warranties'])assert.equal(draft({...proposed({id,code:'tools-equipment',selected:false}),cover:{[field]:[]}}),false,field);
});
test('requested section discriminators, selected amounts and target bounds remain strict in incomplete drafts',()=>{
  for(const section of [
    {id,code:'stock-custody',selected:true,limit:'150000.00',excess:'750.00'},
    {id,code:'premises',selected:true,limit:'250000.00',excess:'500.00',premisesIds:[]},
    {id,code:'premises',selected:true,limit:'250000.00',excess:'500.00',premisesIds:[id,id]},
    {id,code:'tools-equipment',selected:false,limit:'5000.00'},
    {id,code:'tools-equipment',selected:true,limit:'0.00',excess:'250.00'},
    {id,code:'tools-equipment',selected:true,limit:'5000.00',excess:'-1.00'},
    {id,code:'other',selected:false},
    {id,code:'tools-equipment'},
  ])assert.equal(draft(proposed(section)),false,JSON.stringify(section));
});
test('Road Risks excludes Combined-only requests and duplicate section kinds are rejected',()=>{
  const base=proposed({id,code:'stock-custody',selected:false});
  assert.equal(draft({...base,productCode:'motor-trade-road-risks'}),false);
  const one={id,code:'tools-equipment',selected:false};
  assert.equal(draft({...base,cover:{requestedSections:[one,{...one,id:'10000000-0000-4000-8000-000000000002'}]}}),false);
});
test('legacy complete captures stay valid when new requested sections are absent',async()=>{
  const ready=ajv.compile(await read('schemas/quote-ready.schema.json'));
  for(const name of ['motor-trade-road-risks','motor-trade-combined']){
    const sample=await read(`examples/quote-capture-${name}.json`);
    assert.ok(ready(sample.proposal),JSON.stringify(ready.errors));
    assert.ok(ready(fixture.proposals[name]),JSON.stringify(ready.errors));
  }
});
