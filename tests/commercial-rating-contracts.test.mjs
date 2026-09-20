import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import Ajv2020 from 'ajv/dist/2020.js';
import addFormats from 'ajv-formats';
import {underwritingResponseValidator} from '../scripts/validate-underwriting-response.mjs';
const read = async name=>JSON.parse(await readFile(new URL('../contracts/'+name,import.meta.url),'utf8'));
const schema = await read('schemas/commercial-underwriting.schema.json');
const definitions = await read('examples/commercial-underwriting-demo.json');
const ajv = new Ajv2020({strict:true,allErrors:true});addFormats(ajv);const valid = ajv.compile(schema);
test('published commercial rating, binder and authority have distinct closed configurations',()=>{
 for(const value of Object.values(definitions))assert.equal(valid(value),true,JSON.stringify(valid.errors));
 const forged=structuredClone(definitions.rating);forged.driverCount=0;assert.equal(valid(forged),false);
});
test('missing categories, source dispositions and forged selection limits cannot publish',()=>{
 for(const mutate of [x=>delete x.wageRates.warehouse,x=>delete x.sourceDispositions['prototype.quote.4aa3e214f046'],x=>x.selectionLimits.goodsInTransit['1']='5000.00',x=>x.glassIncluded['2']=true]){
  const value=structuredClone(definitions.rating);mutate(value);assert.equal(valid(value),false);
 }
});
test('every source cover selection has an explicit pricing disposition',async()=>{
 const source=await read('quote-question-catalogue.json');
 const questions=source.deferredQuestions.filter(q=>q.targetContainer==='cover.responses'&&['reference','boolean'].includes(q.kind));
 assert.deepEqual(Object.keys(definitions.rating.sourceDispositions).sort(),questions.map(q=>q.questionId).sort());
 assert.equal(definitions.rating.selectionLimits.goodsInTransit['4'],'25000.00');
 assert.equal(definitions.rating.selectionLimits.money['4'],'5000.00');
});
test('actual API union validates decimal height without binary-float false negatives',async()=>{
 const proposal=await read('examples/commercial-combined-ready.json');
 const validate=await underwritingResponseValidator('QuoteCaptureProposal');
 proposal.risk.liability.maximumHeightMetres=2.05;
 assert.equal(validate(proposal),true,JSON.stringify(validate.errors));
 proposal.risk.liability.maximumHeightMetres=2.051;
 assert.equal(validate(proposal),false);
});
