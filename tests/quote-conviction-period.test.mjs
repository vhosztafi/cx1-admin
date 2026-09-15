import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import {validateQuoteConvictionPeriods} from '../scripts/quote-conviction-period.mjs';
import {createQuoteValidationPipeline} from '../scripts/quote-validation-pipeline.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../contracts/${path}`,import.meta.url),'utf8'));
const schema=await read('schemas/quote-draft.schema.json'),ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);const shape=ajv.compile(schema);
const id='aaaaaaaa-0000-4000-8000-000000000099';
const fixture=row=>({schemaVersion:'1.0',productCode:'motor-trade-road-risks',risk:{drivers:[{id,convictions:[{id,...row}]}]}});

test('every declared disqualification band preserves its endpoints without inventing exact months',()=>{
 for(const [band,min,max] of [['under-3-months',1,2],['3-to-6-months',3,6],['6-to-12-months',6,12],['over-12-months',13,1200]]) {
  const p=fixture({declaredBanPeriod:band,disqualified:true});assert.equal(validateQuoteConvictionPeriods(p)[0].code,'exact-ban-duration-required');
  const row=p.risk.drivers[0].convictions[0];
  for(const months of [min,max]){row.banMonths=months;assert.equal(shape(p),true);const before=structuredClone(p);assert.deepEqual(validateQuoteConvictionPeriods(p),[]);assert.deepEqual(p,before);}
  for(const months of [min-1,max+1]){row.banMonths=months;assert.ok(validateQuoteConvictionPeriods(p).some(i=>i.code==='ban-duration-outside-declared-band'));}
 }
 const p=fixture({declaredBanPeriod:'none',disqualified:false});assert.deepEqual(validateQuoteConvictionPeriods(p),[]);p.risk.drivers[0].convictions[0].banMonths=0;assert.deepEqual(validateQuoteConvictionPeriods(p),[]);
});

test('prototype band and liability option mappings cover the source labels without losing split liability',async()=>{
 const manifest=await read('quote-prototype-bindings.json');
 assert.deepEqual(manifest.controls.find(c=>c.controlId==='CTL-d032c5f5ac0f').bindings[0].valueMapping,{Yes:'fault',No:'non-fault','Split liability':'split','Not yet determined':'unknown'});
 const ban=manifest.controls.find(c=>c.controlId==='CTL-f40626183cda').bindings[0];assert.equal(Object.keys(ban.valueMapping).length,5);assert.ok(ban.paths.includes('risk.drivers[].convictions[].declaredBanPeriod'));
 const p=fixture({});delete p.risk.drivers[0].convictions;p.risk.drivers[0].losses=[{id,fault:'split'}];assert.equal(shape(p),true,JSON.stringify(shape.errors));
 for(const fault of ['fault','non-fault','unknown']){p.risk.drivers[0].losses[0].fault=fault;assert.equal(shape(p),true);}
 p.risk.drivers[0].losses[0].fault='Split liability';assert.equal(shape(p),false);
});

test('composed validation rejects unrepresented bands and contradictory disqualification flags',async()=>{
 const {proposal:p,context}=await read('examples/quote-capture-motor-trade-combined.json'),validate=await createQuoteValidationPipeline();
 p.risk.drivers[0].convictions=[{id,declaredBanPeriod:'none',disqualified:true,banMonths:6}];
 let result=validate(JSON.stringify(p),context);assert.ok(result.issues.some(i=>i.stage==='conviction-period'&&i.code==='conflicting-disqualification-declaration'));assert.ok(result.issues.some(i=>i.code==='ban-duration-outside-declared-band'));
 delete p.risk.drivers[0].convictions[0].declaredBanPeriod;assert.ok(validate(JSON.stringify(p),context).issues.some(i=>i.code==='declared-ban-period-required'));
 p.risk.drivers[0].convictions[0].declaredBanPeriod='guess';assert.equal(validate(JSON.stringify(p),context).status,'invalid-draft');
});
