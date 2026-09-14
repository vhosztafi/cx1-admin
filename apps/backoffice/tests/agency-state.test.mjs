import test from 'node:test';
import assert from 'node:assert/strict';
import {stateAction,canDecideState} from '../lib/agency-state.ts';
test('agency lifecycle offers only the transition for its current state',()=>{
  assert.equal(stateAction('draft').path,'activate');
  assert.equal(stateAction('active').path,'suspend');
  assert.equal(stateAction('suspended').path,'reactivate');
  for(const state of ['abandoned','pending','constructor','__proto__'])assert.equal(stateAction(state),undefined);
});
test('only an identified independent reviewer can decide a pending request',()=>{
  assert.equal(canDecideState({state:'pending',requestedBy:'a'},'b'),true);
  for(const actor of ['','a'])assert.equal(canDecideState({state:'pending',requestedBy:'a'},actor),false);
  for(const state of ['applied','rejected','stale'])assert.equal(canDecideState({state,requestedBy:'a'},'b'),false);
});
