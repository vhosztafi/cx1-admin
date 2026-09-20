import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import {emptyCommercialProposal,commercialField,changeCommercialField,commercialRows,addCommercialRow,updateCommercialRow,removeCommercialRow,setCommercialResponse,commercialResponse} from '../lib/commercial-capture.ts';
import {createQuoteCommand,saveQuoteCommand} from '../lib/quotes.ts';

const schema=JSON.parse(await readFile(new URL('../../../contracts/schemas/commercial-combined.schema.json',import.meta.url),'utf8'));
const ajv=new Ajv({strict:true,allErrors:true});addFormats(ajv);const valid=ajv.compile(schema);
const first='10000000-0000-4000-8000-000000000001',second='10000000-0000-4000-8000-000000000002';

test('commercial creation and stable row editing preserve the closed schema and original proposal',()=>{
 const empty=emptyCommercialProposal();assert.ok(valid(empty),JSON.stringify(valid.errors));
 let p=addCommercialRow(empty,'losses',first);p=updateCommercialRow(p,'losses',first,'amount','10.01');
 p=addCommercialRow(p,'losses',second);p=updateCommercialRow(p,'losses',second,'description','Fictional water damage');
 assert.equal(empty.risk,undefined);assert.equal(commercialRows(p,'losses')[0].amount,'10.01');
 const removed=removeCommercialRow(p,'losses',first);assert.equal(commercialRows(removed,'losses')[0].id,second);
 assert.equal(commercialRows(p,'losses').length,2);assert.ok(valid(removed),JSON.stringify(valid.errors));
});
test('stale item IDs, duplicate IDs and foreign loss-location links cannot redirect an edit',()=>{
 const p=addCommercialRow(emptyCommercialProposal(),'losses',first);
 assert.throws(()=>addCommercialRow(p,'locations',first));
 assert.throws(()=>updateCommercialRow(p,'losses',second,'amount','1.00'));
 assert.throws(()=>updateCommercialRow(p,'losses',first,'id',second));
 assert.throws(()=>updateCommercialRow(p,'losses',first,'riskItemId',second));
 assert.throws(()=>addCommercialRow(p,'losses','00000000-0000-0000-0000-000000000000'));
});
test('removing a referenced location needs an explicit loss-link change',()=>{
 let p=addCommercialRow(emptyCommercialProposal(),'locations',first);p=addCommercialRow(p,'losses',second);
 p=updateCommercialRow(p,'losses',second,'riskItemId',first);assert.throws(()=>removeCommercialRow(p,'locations',first));
 p=updateCommercialRow(p,'losses',second,'riskItemId',undefined);p=removeCommercialRow(p,'locations',first);
 assert.equal(commercialRows(p,'locations').length,0);assert.equal(commercialRows(p,'losses')[0].riskItemId,undefined);
});
test('unknown, explicit No and explicit zero remain different saved values',()=>{
 const question={id:'prototype.quote.0b0b63fca324',kind:'boolean',container:'risk.declarations',label:'Loss history',stage:3,options:[]};
 const initial=emptyCommercialProposal();let p=setCommercialResponse(initial,question,undefined,false);
 assert.equal(commercialResponse(p,question),false);assert.ok(valid(p),JSON.stringify(valid.errors));
 p=setCommercialResponse(p,question,undefined,undefined);assert.equal(commercialResponse(p,question),undefined);
 p=changeCommercialField(p,'risk.business.turnover','0.00');assert.equal(commercialField(p,'risk.business.turnover'),'0.00');
 assert.throws(()=>changeCommercialField(p,'productCode','motor-trade-combined'));
 assert.throws(()=>changeCommercialField(p,'risk.__proto__.polluted',true));
});
test('commercial requests retain the exact format, body, key and stale ETag for retries',()=>{
 const p=emptyCommercialProposal();const create=createQuoteCommand(first,second,p,'commercial-create-key');
 assert.equal(JSON.parse(create.body).proposal.format,'commercial-combined-capture-1');
 const save=saveQuoteCommand(first,'"AAAAAAAAAAE="',p,'Fictional capture','commercial-save-key');
 p.risk={materialFacts:'later unsaved change'};assert.equal(JSON.parse(save.body).proposal.risk,undefined);
 assert.equal(save.etag,'"AAAAAAAAAAE="');assert.equal(save.key,'commercial-save-key');assert.ok(Object.isFrozen(save));
});
