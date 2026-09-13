import { readFile, mkdir, writeFile } from 'node:fs/promises';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import path from 'node:path';

const root = fileURLToPath(new URL('../', import.meta.url));
const sourcePath = path.join(root, 'docs/prototype/Cover MGA Back Office-4.html');
const raw = await readFile(sourcePath);
const source = raw.toString('utf8');
const match = source.match(/<script\s+type="__bundler\/template">\s*([\s\S]*?)\s*<\/script>/);
if (!match) throw new Error('Expected bundled template was not found. Source format changed.');
const template = JSON.parse(match[1]);
if (typeof template !== 'string') throw new Error('Bundled template must be a JSON string.');
const lines = template.split(/\r?\n/);
let method = '(template)';
const evidence = [];
for (const [index, text] of lines.entries()) {
  const methodMatch = text.match(/^  ([A-Za-z_$][\w$]*)\([^)]*\)\s*\{/);
  if (methodMatch) method = methodMatch[1];
  if (methodMatch || /\b(title:|tabs:|subnav:|onClick:|onConfirm:|confirmLabel:|disabled:)|this\.act\(|m\.kind\s*===/.test(text)) {
    evidence.push({ line: index + 1, method, text: text.trim() });
  }
}
const out = path.join(root, 'docs/design/source');
await mkdir(out, { recursive: true });
await writeFile(path.join(out, 'prototype-template.txt'), template);
await writeFile(path.join(out, 'prototype-evidence.json'), JSON.stringify({
  source: 'docs/prototype/Cover MGA Back Office-4.html',
  sha256: createHash('sha256').update(raw).digest('hex'),
  note: 'Extracted candidates for manual review, not a complete or verified behavioural inventory. Line numbers refer to prototype-template.txt.',
  templateLines: lines.length,
  evidence,
}, null, 2) + '\n');
console.log(JSON.stringify({ templateLines: lines.length, evidenceCandidates: evidence.length, output: 'docs/design/source' }));
