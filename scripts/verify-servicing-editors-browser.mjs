import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { spawn } from 'node:child_process';
import { mkdir,readFile,stat,writeFile } from 'node:fs/promises';
const cases=[['driver','servicing-driver'],['vehicle','servicing-vehicle'],['premises','servicing-premises'],['business','servicing-business'],['policyholder','servicing-policyholder'],['cover','servicing-cover'],['date-review','servicing-date-review'],['editor-audit','servicing-editor-audit']];
const output='.local/browser-evidence/servicing-editors';await mkdir(output,{recursive:true});
const expected=JSON.parse(await readFile('.local/browser-evidence/underwriting-issue/report.json','utf8')).journeys.map(x=>x.policyId).sort();assert.equal(new Set(expected).size,2);
const report=process.argv.includes('--resume')?JSON.parse(await readFile(`${output}/progress.json`,'utf8')):{startedAt:new Date().toISOString(),runs:[]};
for(const [name,directory] of cases){
 const previous=report.runs.find(run=>run.name===name);
 if(previous){
  assert.ok((await stat(`scripts/verify-servicing-${name}-browser.mjs`)).mtimeMs<=Date.parse(previous.startedAt),`${name} script changed since its completed run`);
  const bytes=await readFile(previous.path);assert.equal(createHash('sha256').update(bytes).digest('hex'),previous.artifactHash);
  const prior=JSON.parse(bytes);assert.deepEqual(prior.journeys.map(x=>x.policyId).sort(),expected);
  console.log(`Retaining completed servicing ${name} from this gate`);continue;
 }
 const started=Date.now();console.log(`Checking servicing ${name}`);
 const code=await new Promise((resolve,reject)=>{const child=spawn(process.execPath,[`scripts/verify-servicing-${name}-browser.mjs`],{stdio:'inherit',windowsHide:true});child.on('error',reject);child.on('exit',resolve);});
 assert.equal(code,0,`${name} browser checks failed`);
 const path=`.local/browser-evidence/${directory}/report.json`;assert.ok((await stat(path)).mtimeMs>=started,`${name} report is stale`);
 const result=JSON.parse(await readFile(path,'utf8'));assert.equal(result.journeys.length,2,`${name} must verify both products`);assert.deepEqual(result.journeys.map(x=>x.policyId).sort(),expected,`${name} verified different policies`);
 const retainedPath=`${output}/${name}.json`;await writeFile(retainedPath,JSON.stringify(result,null,2));
 report.runs.push({name,startedAt:new Date(started).toISOString(),completedAt:new Date().toISOString(),path:retainedPath,artifactHash:createHash('sha256').update(await readFile(retainedPath)).digest('hex'),policyIds:result.journeys.map(x=>x.policyId)});
 await writeFile(`${output}/progress.json`,JSON.stringify(report,null,2));
}
report.completedAt=new Date().toISOString();await writeFile(`${output}/report.json`,JSON.stringify(report,null,2));console.log('All 16 typed servicing editor journeys passed.');
