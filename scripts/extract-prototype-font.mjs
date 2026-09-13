import { readFile, mkdir, writeFile } from 'node:fs/promises';
const source = await readFile(new URL('../docs/prototype/Cover MGA Back Office-4.html', import.meta.url), 'utf8');
const manifest = JSON.parse(source.match(/<script type="__bundler\/manifest">([\s\S]*?)<\/script>/)[1]);
const font = manifest['8f932163-6d4c-41fd-aa3d-c17c9db26c5f'];
if (!font || font.mime !== 'font/woff2' || font.compressed) throw new Error('Expected uncompressed prototype font.');
const directory = new URL('../apps/backoffice/public/fonts/', import.meta.url);
await mkdir(directory, { recursive: true });
await writeFile(new URL('ibm-plex-sans-latin.woff2', directory), Buffer.from(font.data, 'base64'));
console.log('Extracted the prototype IBM Plex Sans Latin font.');
