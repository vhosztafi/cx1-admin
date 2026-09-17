import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
const read=async file=>JSON.parse(await readFile(new URL(`../contracts/${file}`,import.meta.url),'utf8'));
const ajv=new Ajv2020({strict:true,allErrors:true});addFormats(ajv);
const validate=ajv.compile(await read('schemas/issued-policy.schema.json'));
for(const product of ['motor-trade-road-risks','motor-trade-combined'])test(`${product} actual issue fixture preserves declarations with closed rated money and lineage`,async()=>{
 const value=await read(`examples/issued-${product}.json`);assert.ok(validate(value),JSON.stringify(validate.errors));
 assert.equal(value.snapshotFormat,'issued-quote-1');assert.equal(value.risk.drivers[0].licence.testDate,undefined);
 assert.ok(value.risk.drivers[0].licence.issuedOn);assert.ok(value.premium.ratingResultId);assert.ok(value.provenance.quoteRevisionId);
 const missing=structuredClone(value);delete missing.premium;assert.equal(validate(missing),false);
 const open=structuredClone(value);open.premium.paid='100.00';assert.equal(validate(open),false);
});
