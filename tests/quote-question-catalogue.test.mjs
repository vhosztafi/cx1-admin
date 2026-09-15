import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {quoteMappingsForProduct,validateQuoteQuestions,validateQuoteReferences} from '../scripts/quote-semantic-contract.mjs';
const read=async path=>JSON.parse(await readFile(new URL(`../${path}`,import.meta.url),'utf8'));
const catalogue=await read('contracts/quote-question-catalogue.json');
const references=await read('contracts/reference-data/motor-trade-capture.json');

test('all 164 prototype question definitions have capture or explicit Commercial Combined ownership',async()=>{
  const sources=await Promise.all(['prototype-quote-questions','prototype-detail-questions','prototype-quote-value-questions'].map(name=>read(`contracts/examples/${name}.json`)));
  const original=sources.flatMap(source=>source.questions);
  const active=catalogue.mappings.filter(row=>row.owner.startsWith('prototype.')&&row.sourceCatalogue!=='prototype-conditional-1');
  const all=[...active,...catalogue.deferredQuestions];
  assert.equal(original.length,164);assert.equal(active.length,55);assert.equal(catalogue.deferredQuestions.length,109);
  assert.equal(new Set(all.map(row=>row.questionId)).size,164);
  for(const question of original) {
    const row=all.find(row=>row.questionId===question.questionId);
    assert.equal(row.sourceControlId,question.sourceControlId);assert.equal(row.kind,question.kind);
    assert.deepEqual(row.sourceOptions,question.sourceOptions);assert.equal(row.targetContainer,question.targetContainer);
  }
  for(const row of catalogue.deferredQuestions)assert.deepEqual(row.products,['commercial-combined']);
});

test('all 311 source mappings resolve to actual capture schema paths',async()=>{
  const schema=await read('contracts/schemas/quote-ready.schema.json');
  assert.equal(catalogue.mappings.length,311);
  for(const row of catalogue.mappings) {
    let node=schema;
    for(const part of row.canonicalPath.split('.')) {
      while(node.$ref)node=schema.$defs[node.$ref.split('/').at(-1)];
      node=node.properties?.[part.replace(/\[\]$/,'')];assert.ok(node,`${row.owner}: ${row.canonicalPath}`);
      if(part.endsWith('[]'))node=node.items;
    }
    if(row.contractKind==='Answer') {
      assert.equal(node.$ref,'#/$defs/Answer');
      assert.ok(schema.$defs.Answer.oneOf.some(branch=>branch.properties.kind.const===row.answerKind));
    }
  }
});

test('Motor Trade question allowlists include detail questions and reject Commercial Combined or wrong product scope',()=>{
  const rr=quoteMappingsForProduct(catalogue,'motor-trade-road-risks');
  const mtc=quoteMappingsForProduct(catalogue,'motor-trade-combined');
  const response=answers=>({questionSetVersion:catalogue.version,answers});
  const residency={questionId:'prototype.adddriver.residency-years',kind:'count',value:10};
  const proposal={risk:{drivers:[{responses:response([residency])}]}};
  assert.deepEqual(validateQuoteQuestions(proposal,rr,catalogue.version),[]);
  const premises={risk:{premises:[{responses:response([{questionId:'prototype.addprem.public-access',kind:'boolean',value:false}])}]}};
  assert.deepEqual(validateQuoteQuestions(premises,mtc,catalogue.version),[]);
  assert.equal(validateQuoteQuestions(premises,rr,catalogue.version)[0].code,'unknown-question-id');
  for(const deferred of catalogue.deferredQuestions)assert.ok(!rr.some(row=>row.questionId===deferred.questionId)&&!mtc.some(row=>row.questionId===deferred.questionId));
  assert.throws(()=>quoteMappingsForProduct(catalogue,'commercial-combined'),/unsupported-capture-product/);
});

test('all 12 prototype reference families preserve their exact options and validate alongside source references',()=>{
  const questions=catalogue.mappings.filter(row=>row.owner.startsWith('prototype.')&&row.answerKind==='reference');
  assert.equal(questions.length,12);assert.equal(references.version,catalogue.version);
  for(const question of questions) {
    const rows=references.collections[question.questionId];
    assert.deepEqual(rows.map(row=>row.text),question.sourceOptions);
    const binding=references.bindings.find(row=>row.owner===question.questionId);
    assert.deepEqual(binding.collections,[question.questionId]);
    assert.equal(binding.canonicalPath,question.canonicalPath);
  }
  const question=questions.find(row=>row.targetContainer==='risk.business.responses');
  const row=references.collections[question.questionId][0];
  const proposal={risk:{business:{responses:{questionSetVersion:catalogue.version,answers:[{questionId:question.questionId,kind:'reference',value:{collection:question.questionId,value:row.value,label:row.text,version:references.version}}]}}}};
  assert.deepEqual(validateQuoteQuestions(proposal,quoteMappingsForProduct(catalogue,'motor-trade-road-risks'),catalogue.version),[]);
  assert.deepEqual(validateQuoteReferences(proposal,references),[]);
  proposal.risk.business.responses.answers[0].value.label='Forged';
  assert.equal(validateQuoteReferences(proposal,references)[0].code,'reference-label-mismatch');
});
