import assert from 'node:assert/strict';
import { readFile, writeFile, rename, mkdir } from 'node:fs/promises';
import { dirname } from 'node:path';
import { randomUUID } from 'node:crypto';

// Local demo orchestration only. API command receipts remain the authoritative
// database record. Retain this file to resume interrupted multi-command seeds.
export async function openDemoJournal(file, origin, allowedHosts = ['localhost', '127.0.0.1']) {
 assert.ok(allowedHosts.includes(new URL(origin).hostname));
 let state;
 try { state = JSON.parse(await readFile(file, 'utf8')); }
 catch (error) { if (error.code !== 'ENOENT') throw error; state = { version: 1, origin, createdAt: new Date().toISOString(), commands: {} }; }
 assert.equal(state.version, 1); assert.equal(state.origin, origin);
 assert.ok(state.commands && typeof state.commands === 'object' && !Array.isArray(state.commands));
 await mkdir(dirname(file), { recursive: true });
 async function save() {
  const temporary = `${file}.${randomUUID()}.tmp`;
  await writeFile(temporary, JSON.stringify(state, null, 2), { flag: 'wx' });
  await rename(temporary, file);
 }
 await save();
 let busy = false;
 return {
  createdAt: state.createdAt,
  async command(name, prepare, send) {
   assert.match(name, /^[a-z0-9][a-z0-9:/.-]{0,299}$/i);
   assert.equal(busy, false, 'Seed commands must run sequentially.'); busy = true;
   try {
    let entry = Object.hasOwn(state.commands, name) ? state.commands[name] : null;
    if (!entry) {
     entry = { request: await prepare(), key: randomUUID() };
     state.commands[name] = entry; await save();
    }
    if (Object.hasOwn(entry, 'result')) return structuredClone(entry.result);
    // A response lost before the journal commit is retried using the original
    // payload, If-Match and idempotency key, not a newly read version.
    const result = await send(structuredClone(entry.request), entry.key);
    entry.result = structuredClone(result); entry.completedAt = new Date().toISOString();
    await save(); return structuredClone(result);
   } finally { busy = false; }
  },
 };
}
