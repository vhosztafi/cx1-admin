import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
const read=async path=>JSON.parse(await readFile(new URL(`../${path}`,import.meta.url),'utf8'));
const prefix='.planning/phases/07-policy-lifecycle-and-history/';
const inventory=await read(`${prefix}07-SOURCE-INVENTORY.json`);
const fields=await read(`${prefix}07-SOURCE-FIELDS.json`);
const api=await read('contracts/openapi.json');
test('every retained servicing control and conditional field has an explicit implementation owner',()=>{
 assert.equal(inventory.controls.length,137);
 assert.equal(new Set(inventory.controls.map(x=>x.id)).size,137);
 assert.equal(fields.entries.length,393);
 assert.equal(new Set(fields.entries.map(x=>x.id)).size,393);
 assert.equal(fields.conditionalBranches.length,10);
 for(const entry of [...inventory.controls,...fields.entries,...fields.conditionalBranches]) {
  assert.match(entry.owner,/^(07-(0[1-9]|1[0-6])|Phase9 .+|Phase10 .+)$/u,entry.id);
 }
 for(const behavior of ['Issue adjustment','Driver','Vehicle'])
  assert.ok(JSON.stringify(fields).toLowerCase().includes(behavior.toLowerCase()),behavior);
});
test('field audit remains tied to exact prototype source bytes',async()=>{
 const source=await readFile(new URL(`../${fields.source}`,import.meta.url));
 assert.equal(createHash('sha256').update(source).digest('hex'),fields.sourceSha256);
 const lines=source.toString('utf8').split(/\r?\n/);
 for(const entry of fields.entries)assert.ok(lines[entry.line-1]?.includes(entry.source.trim()),entry.id);
});
test('servicing writes preserve current scope, no-store, CSRF, conditional writes and operation keys',()=>{
 const operations=Object.values(api.paths).flatMap(x=>Object.values(x)).filter(x=>['phase-7-pending','phase-7-03-implemented','phase-7-10-implemented'].includes(x['x-runtime-status']));
 assert.deepEqual(operations.map(x=>x.operationId).sort(),['listPolicyDrafts','createPolicyDraft','getPolicyDraft','acquireDraftLease','renewDraftLease',
  'releaseDraftLease','savePolicyDraft','abandonPolicyDraft','issuePolicyDraft','getCancellationPreview','persistCancellationPreview','approveCancellation','recordRenewalExperience'].sort());
 for(const operation of operations) {
  assert.match(operation.description,/before (?:receipt )?replay/);
  for(const response of Object.values(operation.responses))assert.equal(response.headers['Cache-Control'].schema.const,'no-store');
  if(['getCancellationPreview','getPolicyDraft','listPolicyDrafts'].includes(operation.operationId))continue;
  for(const name of ['If-Match','Idempotency-Key'])assert.ok(operation.parameters.some(p=>p.name===name&&p.required),`${operation.operationId} ${name}`);
  assert.ok(operation.security.some(x=>'Csrf' in x));
 }
});
test('servicing adds no duplicate templated routes and retains reviewed operation identities',async()=>{
 const ids=Object.values(api.paths).flatMap(x=>Object.values(x)).map(x=>x.operationId);
 assert.equal(new Set(ids).size,ids.length);
 const paths=Object.keys(api.paths).map(x=>x.replace(/\{[^}]+\}/g,'{}'));
 assert.equal(new Set(paths).size,paths.length);
 for(const name of ['attachDraftEvidence','withdrawDraftEvidence','getCancellationPreview','savePolicyDraft','recordDraftAcceptance','issuePolicyDraft'])assert.ok(ids.includes(name),name);
});
