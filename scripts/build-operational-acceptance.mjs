import assert from 'node:assert/strict';
import {readdir,readFile,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {spawn} from 'node:child_process';
const dist=process.env.COVER_OPERATIONAL_ACCEPTANCE_DIST??'.local/next-phase9-acceptance';
assert.match(dist,/^\.local\/next-[a-z0-9-]+$/,'Acceptance output must stay in an isolated local Next directory.');
async function snapshot(){
 const paths=[];
 async function walk(path){for(const entry of await readdir(path,{withFileTypes:true})){const child=path+'/'+entry.name;if(entry.isDirectory())await walk(child);else if(entry.isFile())paths.push(child);}}
 for(const root of ['apps/backoffice/app','apps/backoffice/components','apps/backoffice/lib','apps/backoffice/public','contracts/generated'])await walk(root);
 paths.push('apps/backoffice/next.config.ts','apps/backoffice/postcss.config.mjs','apps/backoffice/package.json','pnpm-lock.yaml');
 const files={};for(const path of paths.sort())files[path]=createHash('sha256').update(await readFile(path)).digest('hex');return files;
}
const before=await snapshot();
const code=await new Promise((resolve,reject)=>{const child=spawn(process.execPath,['apps/backoffice/node_modules/next/dist/bin/next','build','apps/backoffice','--webpack'],{stdio:'inherit',windowsHide:true,env:{...process.env,COVER_NEXT_DIST_DIR:dist,BACKOFFICE_API_ORIGIN:'http://127.0.0.1:5087'}});child.once('error',reject);child.once('exit',resolve);});
assert.equal(code,0,'Acceptance build failed.');const after=await snapshot();assert.deepEqual(after,before,'Source changed while building; rebuild before acceptance.');
await writeFile('apps/backoffice/'+dist+'/operational-source-manifest.json',JSON.stringify({format:'operational-build-1',builtAt:new Date().toISOString(),buildId:(await readFile('apps/backoffice/'+dist+'/BUILD_ID','utf8')).trim(),files:after},null,2));
console.log('Current operational acceptance build and exact source manifest recorded.');
