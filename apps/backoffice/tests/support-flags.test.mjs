import test from 'node:test';
import assert from 'node:assert/strict';
import {flagPayload,canServiceSupport} from '../lib/support-flags.ts';
test('declined consent cannot produce a sensitive support mutation payload',()=>{
  const form=new FormData();form.set('consentBasis','declined');form.set('internalInstruction','Fictional private draft');form.set('reason','Fictional private reason');
  assert.throws(()=>flagPayload(form,['relationship']),error=>{assert.doesNotMatch(error.message,/private/);return /No support details/.test(error.message);});
});
test('support payload preserves explicit grants and omits absent agency wording for internal-only flags',()=>{
  const form=new FormData();for(const [key,value] of Object.entries({typeCode:'accessible-format',internalCategory:'capability',internalInstruction:' Written follow-up ',consentBasis:'written-consent',reviewOn:'2027-01-01',reason:' Fictional request ',agencyInstruction:''}))form.set(key,value);
  const body=flagPayload(form,[]);assert.deepEqual(body.visibleRelationshipIds,[]);assert.equal(Object.hasOwn(body,'agencyInstruction'),false);assert.equal(body.internalInstruction,'Written follow-up');
  assert.deepEqual(flagPayload(form,['b','a','b']).visibleRelationshipIds,['a','b']);
  assert.equal(canServiceSupport(['agency-admin']),false);assert.equal(canServiceSupport(['system-admin']),false);assert.equal(canServiceSupport(['servicing']),true);
});
