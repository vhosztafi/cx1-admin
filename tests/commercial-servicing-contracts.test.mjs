import test from 'node:test';
import assert from 'node:assert/strict';
import {readFileSync} from 'node:fs';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
const schema=JSON.parse(readFileSync(new URL('../contracts/schemas/servicing.schema.json',import.meta.url),'utf8'));
const ajv=new Ajv2020({allErrors:true,strict:true,multipleOfPrecision:8});addFormats(ajv);ajv.addSchema(schema);
const change=ajv.compile({$ref:schema.$id+'#/$defs/ServicingChange'});
const id='aaaaaaaa-0000-4000-8000-000000000001';
test('commercial servicing kinds use closed product-specific payloads and stable target envelopes',()=>{
 for(const kind of ['location','property','business','bi','liability','wage','loss','cover','insured','declarations']){
  const value={changeId:id,riskItemId:id,kind:'commercial-'+kind,operation:'update',payload:{},payloadMode:'replace'};
  assert.ok(change(value),JSON.stringify(change.errors));assert.equal(change({...value,payload:{premium:'1.00'}}),false);
  assert.equal(change({...value,payload:{id}}),false);
 }
 assert.equal(change({changeId:id,riskItemId:id,kind:'commercial-insured',operation:'update',payload:{clientId:id}}),false);
});
test('commercial location property and cover date payloads cannot smuggle motor targets',()=>{
 const base={changeId:id,riskItemId:id,operation:'update'};
 assert.ok(change({...base,kind:'commercial-property',payload:{buildings:'0.00',stock:'100.01'}}));
 assert.equal(change({...base,kind:'commercial-property',payload:{address:{postcode:'S9 2QT'}}}),false);
 assert.equal(change({...base,kind:'commercial-location',payload:{registration:'AB12 CDE'}}),false);
 const effectiveIntent={localDate:'2026-10-01',localTime:'00:00',timeZone:'Europe/London'};
 assert.ok(change({...base,kind:'commercial-cover',payload:{},effectiveIntent}));
 assert.equal(change({...base,kind:'commercial-location',payload:{},effectiveIntent}),false);
});
