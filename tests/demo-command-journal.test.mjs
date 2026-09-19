import { test } from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readdir, unlink, rmdir } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve, sep } from 'node:path';
import { openDemoJournal } from '../scripts/demo-command-journal.mjs';

async function fixture(run) {
 const directory = await mkdtemp(join(tmpdir(), 'cover-demo-journal-'));
 try { await run(join(directory, 'commands.json')); }
 finally {
  assert.ok(resolve(directory).startsWith(resolve(tmpdir()) + sep));
  for (const file of await readdir(directory)) await unlink(join(directory, file));
  await rmdir(directory);
 }
}
test('lost command response resumes original payload, version and key after process restart', async () => fixture(async file => {
 const origin = 'http://127.0.0.1:3100'; let journal = await openDemoJournal(file, origin), original;
 await assert.rejects(journal.command('issue', async () => ({ path: '/issue', version: 'first', body: { amount: '12.00' } }), async (request, key) => {
  original = { request, key }; throw Error('response lost after server commit');
 }), /response lost/);
 journal = await openDemoJournal(file, origin);
 const result = await journal.command('issue', async () => assert.fail('Must not replace frozen request'), async (request, key) => {
  assert.deepEqual({ request, key }, original); return { policyId: 'persisted-policy' };
 });
 assert.deepEqual(result, { policyId: 'persisted-policy' });
 journal = await openDemoJournal(file, origin);
 assert.deepEqual(await journal.command('issue', async () => assert.fail(), async () => assert.fail('Completed seed must not dispatch again')), result);
}));
test('demo journal refuses an origin change and concurrent commands', async () => fixture(async file => {
 const journal = await openDemoJournal(file, 'http://127.0.0.1:3100');
 await assert.rejects(openDemoJournal(file, 'http://127.0.0.1:3200'));
 let release, entered;
 const started = new Promise(resolve => { entered = resolve; });
 const held = journal.command('first', async () => ({}), async () => { entered(); await new Promise(resolve => { release = resolve; }); return {}; });
 await started;
 await assert.rejects(journal.command('second', async () => ({}), async () => ({})), /sequentially/);
 release(); await held;
}));
