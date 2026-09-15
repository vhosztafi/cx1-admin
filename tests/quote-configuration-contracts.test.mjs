import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {questionConfigurationSchema,referenceConfigurationSchema,validateQuoteConfiguration} from '../scripts/quote-configuration-contracts.mjs';

const read=async name=>JSON.parse(await readFile(new URL(`../contracts/${name}`,import.meta.url),'utf8'));
const questions=await read('quote-question-catalogue.json'),references=await read('reference-data/motor-trade-capture.json');
const check=(mutate,pattern)=>{const q=structuredClone(questions),r=structuredClone(references);mutate(q,r);assert.match(validateQuoteConfiguration(q,r).join('\n'),pattern);};

test('published configuration schemas match generators and accept all pinned source and prototype families',async()=>{
 assert.deepEqual(await read('schemas/quote-question-configuration.schema.json'),questionConfigurationSchema);
 assert.deepEqual(await read('schemas/quote-reference-configuration.schema.json'),referenceConfigurationSchema);
 const before=structuredClone([questions,references]);
 assert.deepEqual(validateQuoteConfiguration(questions,references),[]);
 assert.deepEqual([questions,references],before);
});

test('configuration rejects unknown properties, invalid answer kinds, fractional identifiers and mistyped eligibility',()=>{
 check(q=>{q.mappings[0].premium='100.00';},/additionalProperties/);
 check(q=>{q.mappings.find(row=>row.answerKind).answerKind='arbitrary-json';},/enum/);
 check((q,r)=>{Object.values(r.collections)[0][0].value=1.5;},/type/);
 check((q,r)=>{Object.values(r.collections)[0][0].value='1';},/type/);
 check((q,r)=>{Object.values(r.collections)[0][0].customerLOI='false';},/type/);
 check(q=>{q.mappings[0].canonicalPath='risk..secret';},/pattern/);
});

test('configuration rejects version mismatch, duplicate option IDs, missing collections and conflicting bindings',()=>{
 check((q,r)=>{r.version='mt-capture-0000000000000000';},/version mismatch/);
 check((q,r)=>{const rows=Object.values(r.collections)[0];rows.push({...rows[0],text:'Different label'});},/Duplicate option identity/);
 check((q,r)=>{r.bindings[0].collections=['missing'];},/Unknown collection/);
 check((q,r)=>{r.bindings.push(structuredClone(r.bindings[0]));},/Duplicate reference binding/);
 check((q,r)=>{r.bindings.push({...r.bindings[0],owner:'another-source',collections:[Object.keys(r.collections)[0]]});},/Conflicting reference binding/);
 check((q,r)=>{const i=r.bindings.findIndex(b=>b.questionId==='MTS-06-Q59');r.bindings.splice(i,1);},/Missing question reference binding/);
});

test('conditional questions require a boolean parent in the same product and answer container and cannot cycle',()=>{
 check(q=>{q.mappings.find(row=>row.requiredWhen).requiredWhen.questionId='unknown';},/Invalid conditional parent/);
 check(q=>{const child=q.mappings.find(row=>row.requiredWhen);child.canonicalPath='risk.other.responses.answers[]';},/Invalid conditional parent/);
 check(q=>{const child=q.mappings.find(row=>row.requiredWhen);child.products=['motor-trade-road-risks'];const parent=q.mappings.find(row=>row.questionId===child.requiredWhen.questionId);parent.products=['motor-trade-combined'];},/Invalid conditional parent/);
 check(q=>{const child=q.mappings.find(row=>row.requiredWhen),parent=q.mappings.find(row=>row.questionId===child.requiredWhen.questionId);parent.requiredWhen={questionId:parent.questionId,equals:true};},/Cyclic question condition/);
 check(q=>{const row=q.mappings.find(row=>row.answerKind==='boolean');q.mappings.push({...row,answerKind:'text'});},/Conflicting question definition/);
});

test('dynamic age bands and direct prototype options cannot silently drift from their bound collections',()=>{
 check((q,r)=>{r.youngDriverConfiguration[1].ageFrom=18;},/Invalid age band/);
 check((q,r)=>{r.youngDriverConfiguration[0].cCs[0].text='Changed';},/Age band collection mismatch/);
 check((q,r)=>{r.youngDriverConfiguration[0].defaultCc='Unknown';},/Unknown age band default/);
 check(q=>{q.directReferenceFields[0].sourceOptions[0]='Changed';},/Direct reference options mismatch/);
 check((q,r)=>{r.bindings=r.bindings.filter(b=>b.owner!==q.directReferenceFields[0].controlId);},/Unbound direct reference/);
});
