import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {parseQuoteJson,maxQuoteBytes,maxQuoteDepth,validateQuoteIdentity,validateQuoteQuestions} from '../scripts/quote-semantic-contract.mjs';

const mappings=JSON.parse(await readFile(new URL('../contracts/quote-field-mapping.json',import.meta.url),'utf8')).mappings;
const id=n=>`aaaaaaaa-0000-4000-8000-${String(n).padStart(12,'0')}`;
const base=()=>({schemaVersion:'1.0',productCode:'motor-trade-road-risks'});
const responses=(...answers)=>({questionSetVersion:'test-pinned-1',answers});
const questionCheck=proposal=>validateQuoteQuestions(proposal,mappings,'test-pinned-1');

test('quote JSON rejects duplicate keys including escaped equivalents and nested keys',()=>{
  for(const text of [
    '{"risk":{},"risk":{"drivers":[]}}',
    '{"risk":{"drivers":[{"id":"first","id":"second"}]}}',
    String.raw`{"risk":{},"\u0072isk":{}}`,
    '{"__proto__":{},"__proto__":{}}',
    '{"a/b~c":1,"a/b~c":2}',
  ])assert.throws(()=>parseQuoteJson(text),/duplicate-json-key/);
  const valid=String.raw` { "text":"escaped \"quote\" and \\ slash", "values":[true,false,null,-1.2e3], "nested":[{"id":1},{"id":2}] } `;
  assert.deepEqual(parseQuoteJson(valid),JSON.parse(valid));
  // Field names are case-sensitive: a subsequent strict schema check rejects
  // unknown casing. Duplicate detection must not invent a case conversion.
  assert.deepEqual(parseQuoteJson('{"id":1,"Id":2}'),{id:1,Id:2});
});

test('quote JSON enforces UTF-8 byte and nesting bounds and retains standard grammar rejection',()=>{
  const exact=JSON.stringify('x'.repeat(maxQuoteBytes-2));
  assert.equal(parseQuoteJson(exact).length,maxQuoteBytes-2);
  assert.throws(()=>parseQuoteJson(JSON.stringify('x'.repeat(maxQuoteBytes-1))),/quote-json-too-large/);
  assert.throws(()=>parseQuoteJson(JSON.stringify('€'.repeat(Math.ceil(maxQuoteBytes/3)))),/quote-json-too-large/);
  assert.doesNotThrow(()=>parseQuoteJson('['.repeat(maxQuoteDepth)+'0'+']'.repeat(maxQuoteDepth)));
  assert.throws(()=>parseQuoteJson('['.repeat(maxQuoteDepth+1)+'0'+']'.repeat(maxQuoteDepth+1)),/quote-json-too-deep/);
  for(const value of ['{"a":1,}','[1,]','{"a":NaN}','{"a":01}','true false','{"a":"bad\ntext"}'])
    assert.throws(()=>parseQuoteJson(value),SyntaxError);
});

test('quote item identity is global across nested collections and UUID casing',()=>{
  const proposal={...base(),risk:{drivers:[{id:id(1),occupations:[{id:id(2)}]}],vehicles:[{id:id(3)}]}};
  assert.deepEqual(validateQuoteIdentity(proposal),[]);
  proposal.risk.vehicles[0].id=id(2).toUpperCase();
  assert.deepEqual(validateQuoteIdentity(proposal),[{code:'duplicate-item-id',path:'/risk/vehicles/0/id'}]);
  proposal.risk.vehicles[0].id=id(3);proposal.cover={annualEuropeanCover:[{id:id(1)}]};
  assert.deepEqual(validateQuoteIdentity(proposal),[{code:'duplicate-item-id',path:'/cover/annualEuropeanCover/0/id'}]);
});

test('quote links must identify the correct current proposal item type',()=>{
  const proposal={...base(),risk:{drivers:[{id:id(1),losses:[{id:id(4),riskItemId:id(2)}]}],vehicles:[{id:id(2),ownerDriverId:id(1)}],premises:[{id:id(3)}],specifiedVehicleIds:[id(2)]},cover:{temporaryEuropeanCover:[{id:id(5),driverIds:[id(1)]}]}};
  const before=structuredClone(proposal);
  assert.deepEqual(validateQuoteIdentity(proposal),[]);assert.deepEqual(proposal,before);
  proposal.risk.vehicles[0].ownerDriverId=id(3);
  proposal.risk.specifiedVehicleIds=[id(1)];
  proposal.cover.temporaryEuropeanCover[0].driverIds=[id(99)];
  proposal.risk.drivers[0].losses[0].riskItemId=id(4);
  assert.deepEqual(validateQuoteIdentity(proposal).map(issue=>issue.path),[
    '/risk/specifiedVehicleIds/0','/risk/vehicles/0/ownerDriverId',
    '/cover/temporaryEuropeanCover/0/driverIds/0','/risk/drivers/0/losses/0/riskItemId',
  ]);
});

test('quote reference lists reject duplicate UUIDs even with different casing',()=>{
  const proposal={...base(),risk:{drivers:[{id:id(1)}],vehicles:[{id:id(2)}],specifiedVehicleIds:[id(2),id(2).toUpperCase()]},cover:{temporaryEuropeanCover:[{id:id(3),driverIds:[id(1),id(1).toUpperCase()]}]}};
  assert.deepEqual(validateQuoteIdentity(proposal),[
    {code:'duplicate-item-reference',path:'/risk/specifiedVehicleIds/1'},
    {code:'duplicate-item-reference',path:'/cover/temporaryEuropeanCover/0/driverIds/1'},
  ]);
});

test('questions reject unknown IDs, mismatched kinds, duplicates and wrong scope without exposing answers',()=>{
  const declared={questionId:'MTS-12-Q01',kind:'boolean',value:false};
  const proposal={...base(),risk:{declarations:responses(declared)}};
  assert.deepEqual(questionCheck(proposal),[]);
  proposal.risk.declarations.answers.push({...declared});
  assert.deepEqual(questionCheck(proposal),[{code:'duplicate-question-id',path:'/risk/declarations/answers/1/questionId'}]);
  proposal.risk.declarations=responses({...declared,kind:'text',value:'Sensitive example'});
  assert.deepEqual(questionCheck(proposal),[{code:'question-kind-mismatch',path:'/risk/declarations/answers/0/kind'}]);
  proposal.risk.declarations=responses({...declared,questionId:'not-approved'});
  assert.deepEqual(questionCheck(proposal),[{code:'unknown-question-id',path:'/risk/declarations/answers/0/questionId'}]);
  proposal.risk.declarations=responses();proposal.risk.responses=responses(declared);
  assert.deepEqual(questionCheck(proposal),[{code:'unknown-question-id',path:'/risk/responses/answers/0/questionId'}]);
});

test('question identity is local to each repeatable item while every response uses the pinned version',()=>{
  const answer={questionId:'MTS-06-Q21',kind:'boolean',value:false};
  const proposal={...base(),risk:{drivers:[{id:id(1),responses:responses(answer)},{id:id(2),responses:responses({...answer})}]}};
  assert.deepEqual(questionCheck(proposal),[]);
  proposal.risk.drivers[1].responses.questionSetVersion='caller-version';
  assert.deepEqual(questionCheck(proposal),[{code:'question-version-mismatch',path:'/risk/drivers/1/responses/questionSetVersion'}]);
  delete proposal.risk.drivers[1].responses.questionSetVersion;
  assert.equal(questionCheck(proposal)[0].code,'question-version-mismatch');
  assert.throws(()=>validateQuoteQuestions(proposal,mappings,''),/Pinned question version required/);
});
