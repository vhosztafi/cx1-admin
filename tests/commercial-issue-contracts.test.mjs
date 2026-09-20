import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
const read=async p=>JSON.parse(await readFile(p,'utf8'));
async function fixture(){
 const p=await read('contracts/examples/commercial-combined-ready.json');
 const old=await read('contracts/examples/issued-motor-trade-road-risks.json');
 delete p.format;delete p.termIntent;
 Object.assign(p,{snapshotFormat:'issued-commercial-1',productVersionId:old.productVersionId,term:old.term,premium:old.premium,provenance:old.provenance});
 Object.assign(p.insured,{clientId:old.insured.clientId,clientAgencyRelationshipId:old.insured.clientAgencyRelationshipId});
 p.cover.sections=p.risk.locations.map(x=>({id:x.id,code:'property',limit:'1000000.00',targetIds:[x.id]}));p.cover.endorsements=[];p.cover.warranties=[];
 const ajv=new Ajv2020({allErrors:true,strict:true,multipleOfPrecision:8});addFormats(ajv);
 return {p,validate:ajv.compile(await read('contracts/schemas/commercial-combined-issued.schema.json'))};
}
test('issued CC retains location targets and selected BI extension money',async()=>{
 const {p,validate}=await fixture();p.cover.sections.push({id:p.provenance.quoteRevisionId,code:'named-suppliers',limit:'100000.00',targetIds:[]});
 assert.ok(validate(p),JSON.stringify(validate.errors));
 p.cover.sections[0].targetIds=[];assert.equal(validate(p),false);
});
test('issued CC glass retains its captured basis without an invented monetary limit',async()=>{
 const {p,validate}=await fixture();p.cover.sections.push({id:p.provenance.quoteRevisionId,code:'glass',basis:'Included for the declared premises',targetIds:p.risk.locations.map(x=>x.id)});
 assert.ok(validate(p),JSON.stringify(validate.errors));
 p.cover.sections.at(-1).limit='0.00';assert.equal(validate(p),false);
});
test('issued CC rejects motor sections and incomplete monetary sections',async()=>{
 const {p,validate}=await fixture();assert.ok(validate(p),JSON.stringify(validate.errors));
 p.cover.sections[0].code='road-risks';assert.equal(validate(p),false);
 p.cover.sections[0].code='property';delete p.cover.sections[0].limit;assert.equal(validate(p),false);
});
