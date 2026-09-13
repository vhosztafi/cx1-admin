import test from 'node:test';import assert from 'node:assert/strict';import {readFile} from 'node:fs/promises';
import {validateCoverage} from '../scripts/validate-contracts.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../${path}`,import.meta.url),'utf8'));
const inventory=await read('docs/design/control-inventory.json'),map=await read('docs/design/api-control-map.json'),spec=await read('contracts/openapi.json'),conditional=await read('contracts/examples/conditional-capture.json');
const source=await readFile(new URL('../docs/design/source/prototype-template.txt',import.meta.url),'utf8');
test('coverage gate rejects a missing mapping and a broken operation reference',()=>{
 assert.equal(validateCoverage(inventory,map,spec,conditional,source).controls,949);
 const missing=structuredClone(map);missing.controls.pop();assert.throws(()=>validateCoverage(inventory,missing,spec,conditional,source),/Missing/);
 const broken=structuredClone(map);broken.controls[0].operationIds=['notAnOperation'];assert.throws(()=>validateCoverage(inventory,broken,spec,conditional,source),/Broken/);
});
test('conditional gate catches drift in source and refuses guessed branch evidence',()=>{
 const changed=structuredClone(conditional);changed.controls[0].sourceKey='inventedSourceKey';
 assert.throws(()=>validateCoverage(inventory,map,spec,changed,source),/Conditional source/);
});
