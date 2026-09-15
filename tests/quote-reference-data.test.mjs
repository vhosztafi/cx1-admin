import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {validateQuoteReferences} from '../scripts/quote-semantic-contract.mjs';

const catalogue=JSON.parse(await readFile(new URL('../contracts/reference-data/motor-trade-source.json',import.meta.url),'utf8'));
const mappings=JSON.parse(await readFile(new URL('../contracts/quote-field-mapping.json',import.meta.url),'utf8')).mappings;
const selection=(collection,row=catalogue.collections[collection][0])=>({collection,value:row.value,label:row.text,version:catalogue.version});
const answer=(questionId,value)=>({questionId,kind:'reference',value});

test('pinned reference catalogue preserves all source rows, metadata and dynamic child families',async()=>{
  const bytes=await readFile(new URL(`../${catalogue.sourcePath}`,import.meta.url));
  const source=JSON.parse(bytes);
  assert.equal(createHash('sha256').update(bytes).digest('hex'),catalogue.sourceSha256);
  assert.equal(catalogue.version,`mt-source-${catalogue.sourceSha256.slice(0,16)}`);
  for(const [name,rows] of Object.entries(source)) {
    if(name==='youngDriverConfiguration'){assert.deepEqual(catalogue.youngDriverConfiguration,rows);continue;}
    assert.deepEqual(catalogue.collections[name],rows,name);
  }
  for(const parent of source.indemnityOwnVehicles)
    assert.deepEqual(catalogue.collections[`indemnityOwnVehicles/number:${parent.value}/excesses`],parent.excesses);
  source.youngDriverConfiguration.forEach((band,index)=>{
    for(const name of ['indemnities','cCs'])assert.deepEqual(catalogue.collections[`youngDriverConfiguration/${index}/${name}`],band[name]);
  });
  assert.equal(Object.keys(catalogue.collections).length,76);
  for(const rows of Object.values(catalogue.collections))assert.equal(new Set(rows.map(row=>JSON.stringify(row.value))).size,rows.length);
});

test('every mapped reference field has a bound collection and dynamic families remain context dependent',()=>{
  const references=mappings.filter(row=>row.contractKind==='Reference'||['reference','references'].includes(row.answerKind));
  assert.equal(references.length,57);assert.equal(catalogue.bindings.length,57);
  for(const row of references) {
    const binding=catalogue.bindings.find(binding=>binding.owner===row.owner);
    assert.equal(binding.canonicalPath,row.canonicalPath);
    assert.equal(binding.questionId,row.questionId);
    assert.ok(binding.collections.length>0);
    for(const name of binding.collections)assert.ok(catalogue.collections[name]);
    if(row.optionCollection)assert.deepEqual(binding.collections,[row.optionCollection]);
  }
  assert.deepEqual(catalogue.bindings.filter(row=>row.selectionRule!=='fixed').map(row=>row.owner).sort(),['MTS-05-Q04','MTS-06-Q59','MTS-06-Q60']);
});

test('reference identity rejects type coercion, forged labels, versions and another valid collection',()=>{
  const proposal={insured:{title:selection('proposerTitles')}};
  assert.deepEqual(validateQuoteReferences(proposal,catalogue),[]);
  const original=structuredClone(proposal.insured.title);
  for(const [key,value,code] of [
    ['value',String(original.value),'unknown-reference-value'],
    ['label','Caller-supplied label','reference-label-mismatch'],
    ['version','unapproved-version','reference-version-mismatch'],
    ['collection','driverTitles','reference-collection-mismatch'],
  ]) {
    proposal.insured.title={...original,[key]:value};
    assert.deepEqual(validateQuoteReferences(proposal,catalogue),[{code,path:`/insured/title/${key}`}]);
  }
});

test('supplemental reference selections validate parent scope and duplicate multi-select values',()=>{
  const reference=selection('marketingConsents');
  const proposal={insured:{responses:{answers:[{questionId:'MTS-01-Q02',kind:'references',value:[reference]}]}}};
  assert.deepEqual(validateQuoteReferences(proposal,catalogue),[]);
  proposal.insured.responses.answers[0].value.push({...reference});
  assert.deepEqual(validateQuoteReferences(proposal,catalogue),[{code:'duplicate-reference-selection',path:'/insured/responses/answers/0/value/1'}]);
  const wrongScope={risk:{responses:{answers:[answer('MTS-01-Q03',selection('marketingMethods'))]}}};
  assert.deepEqual(validateQuoteReferences(wrongScope,catalogue),[{code:'unbound-reference',path:'/risk/responses/answers/0/value'}]);
});

test('dynamic selections fail closed without trusted context and cannot choose another parent collection',()=>{
  const binding=catalogue.bindings.find(row=>row.owner==='MTS-05-Q04');
  const [first,second]=binding.collections;
  const proposal={cover:{responses:{answers:[answer(binding.owner,selection(first))]}}};
  assert.equal(validateQuoteReferences(proposal,catalogue)[0].code,'reference-context-required');
  assert.deepEqual(validateQuoteReferences(proposal,catalogue,{'/cover/responses/answers/0/value':[first]}),[]);
  assert.equal(validateQuoteReferences(proposal,catalogue,{'/cover/responses/answers/0/value':[second]})[0].code,'reference-collection-mismatch');
  assert.equal(validateQuoteReferences(proposal,catalogue,{'/cover/responses/answers/0/value':['proposerTitles']})[0].code,'reference-context-required');
  for(const owner of ['MTS-06-Q59','MTS-06-Q60']) {
    const family=catalogue.bindings.find(row=>row.owner===owner).collections[0];
    const driver={risk:{drivers:[{responses:{answers:[answer(owner,selection(family))]}}]}};
    assert.equal(validateQuoteReferences(driver,catalogue)[0].code,'reference-context-required');
    assert.deepEqual(validateQuoteReferences(driver,catalogue,{'/risk/drivers/0/responses/answers/0/value':[family]}),[]);
  }
});

test('each driver requires its own trusted dynamic context even when question IDs match',()=>{
  const owner='MTS-06-Q60';
  const [young,older]=catalogue.bindings.find(row=>row.owner===owner).collections;
  const proposal={risk:{drivers:[
    {responses:{answers:[answer(owner,selection(young))]}},
    {responses:{answers:[answer(owner,selection(older))]}},
  ]}};
  const context={'/risk/drivers/0/responses/answers/0/value':[young]};
  assert.deepEqual(validateQuoteReferences(proposal,catalogue,context),[{code:'reference-context-required',path:'/risk/drivers/1/responses/answers/0/value'}]);
  context['/risk/drivers/1/responses/answers/0/value']=[older];
  assert.deepEqual(validateQuoteReferences(proposal,catalogue,context),[]);
});
