import test from 'node:test';
import assert from 'node:assert/strict';
import {contactPayload} from '../lib/contacts.ts';

test('contact consent change clears former permissions while preserving explicit identity components',()=>{
  const form=new FormData();
  for(const [key,value] of Object.entries({fullName:' Fictional Dr A. Morgan ',role:'Director',primary:'yes',email:' ',telephone:'',consent:'withheld',consentEmail:'on',consentTelephone:'on',recordedAt:'2026-01-01T12:00:00',source:' Phone discussion '}))form.set(key,value);
  const body=contactPayload(form,{personId:'existing-person',firstName:'Alex',surname:'Morgan'});
  assert.equal(body.fullName,'Fictional Dr A. Morgan');assert.equal(body.firstName,'Alex');assert.equal(body.surname,'Morgan');assert.equal(body.personId,'existing-person');
  assert.equal(body.isPrimary,true);assert.equal(Object.hasOwn(body,'email'),false);assert.equal(Object.hasOwn(body,'telephone'),false);
  assert.deepEqual(body.marketingConsent,{state:'withheld',email:false,telephone:false,recordedAt:'2026-01-01T12:00:00.000Z',source:'Phone discussion'});
});
test('new declared full name is never heuristically split and given consent preserves only selected channels',()=>{
  const form=new FormData();for(const [key,value] of Object.entries({fullName:'Fictional A. de la Cruz',role:'Other',primary:'no',consent:'given',consentTelephone:'on',recordedAt:'2026-01-01T12:00:00',source:'Fictional evidence'}))form.set(key,value);
  const body=contactPayload(form);assert.equal(Object.hasOwn(body,'firstName'),false);assert.equal(Object.hasOwn(body,'personId'),false);
  assert.equal(body.marketingConsent.email,false);assert.equal(body.marketingConsent.telephone,true);
});
