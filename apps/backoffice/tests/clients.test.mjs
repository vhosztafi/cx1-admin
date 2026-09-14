import test from 'node:test';
import assert from 'node:assert/strict';
import { identityPayload, clientFetch, ClientError, uncertainFailure, canWriteClients, canReadClients } from '../lib/clients.ts';

test('optional blank identity fields are omitted rather than submitted as invalid null values', () => {
  const form = new FormData();
  for (const [key,value] of Object.entries({legalName:"  Fictional O'Brien Ltd  ",entityType:'limited-company',companyNumber:' ',line1:'1 Example Street',line2:'',town:'Sheffield',county:'',postcode:'S1 1AA'})) form.set(key,value);
  const body = identityPayload(form);
  assert.equal(body.legalName,"Fictional O'Brien Ltd"); assert.equal(body.address.country,'GB');
  assert.equal(Object.hasOwn(body,'companyNumber'),false); assert.equal(Object.hasOwn(body.address,'line2'),false); assert.equal(Object.hasOwn(body.address,'county'),false);
});
test('client access controls distinguish read-only identity access from platform administration', () => {
  assert.equal(canReadClients(['agency-admin']),true); assert.equal(canWriteClients(['agency-admin']),false);
  assert.equal(canReadClients(['system-admin']),false); assert.equal(canWriteClients(['underwriter']),true);
});
test('uncertain responses retain commands while stale versions need explicit reload', async () => {
  assert.equal(uncertainFailure(new TypeError('network')),true); assert.equal(uncertainFailure(new ClientError(503)),true);
  assert.equal(uncertainFailure(new ClientError(412)),false); assert.match(new ClientError(412).message,/Reload/);
  const original=globalThis.fetch;
  globalThis.fetch=async () => new Response('{"detail":"private server detail"}',{status:503});
  try { await assert.rejects(()=>clientFetch('/api/v1/clients'),error=>{assert.doesNotMatch(error.message,/private/);return true;}); }
  finally { globalThis.fetch=original; }
});
