import test from 'node:test';
import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { sourceBusinessQuestions } from '../lib/quote-source-questions.ts';
const read = async name => JSON.parse(await readFile(new URL(`../../../contracts/${name}`, import.meta.url), 'utf8'));
const mappings = (await read('quote-question-catalogue.json')).mappings;
const refs = await read('reference-data/motor-trade-capture.json');
const ownership = (await read('quote-control-ownership.json')).controls;
test('business form projection covers every initial source-stage question without pulling in later sections', () => {
  const questions = sourceBusinessQuestions(mappings, refs.bindings, refs.collections);
  assert.equal(questions.length, 15); assert.equal(new Set(questions.map(x => x.id)).size, 15);
  for (const control of ownership.filter(x => x.sourceStages.some(s => /^Motor Trade (Road Risks|Combined):step-[23]$/.test(s)))) {
    for (const binding of control.sourceFieldBindings.filter(x => x.questionId)) {
      const question = questions.find(x => x.id === binding.questionId); assert.ok(question, control.label);
      assert.equal(question.label, control.label);
      assert.equal(question.stage, control.sourceStages.some(x => x.endsWith(':step-2')) ? 1 : 2);
    }
  }
  assert.equal(questions.some(x => x.id === 'prototype.quote.46414cc10100'), false);
  assert.equal(questions.some(x => x.id === 'prototype.quote.d7a75768e505'), false);
  for (const question of questions.filter(x => x.kind === 'reference')) {
    assert.deepEqual(question.choices, refs.collections[question.collection].map(({value,text}) => ({value,text})));
    assert.ok(question.choices.length);
  }
});
test('business projection refuses missing reference bindings and unsupported control types', () => {
  assert.throws(() => sourceBusinessQuestions(mappings, [], refs.collections));
  const changed = structuredClone(mappings); changed.find(x => x.questionId === 'MTS-02-Q01').answerKind = 'date';
  assert.throws(() => sourceBusinessQuestions(changed, refs.bindings, refs.collections));
});
