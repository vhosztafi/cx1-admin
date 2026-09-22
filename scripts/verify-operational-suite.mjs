import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {mkdir,readFile,writeFile} from 'node:fs/promises';

// Evidence collectors only: no SQL/browser suites are silently relaunched.
// Each collector validates its current source fingerprints and SQL readback.
assert.equal(process.argv.length,2,'Operational acceptance does not accept stage filters.');
const startedAt=new Date().toISOString(),directory='.local/operational-suite/'+startedAt.replaceAll(/[:.]/g,'-');
await mkdir(directory,{recursive:true});
const report={startedAt,passed:false,stages:[
 ['tasks-and-workflows','verify-task-browser.mjs'],['documents','verify-document-browser.mjs'],
 ['communication-and-delivery','verify-communication-browser.mjs'],['historical-incidents','verify-incident-browser.mjs'],
 ['claims-administrator','verify-claims-browser.mjs'],['mid','verify-mid-browser.mjs'],
 ['cancellation-operations','verify-cancellation-browser.mjs'],['matching-correspondence','verify-operational-match-browser.mjs'],
 ['lapse-correspondence','verify-operational-lapse-browser.mjs'],
 ['driver-referral-tasks','verify-driver-task-browser.mjs'],
].map(([name,script])=>({name,script:'scripts/'+script,status:'not-run'}))};
const save=()=>writeFile(directory+'/report.json',JSON.stringify(report,null,2));await save();
for(const stage of report.stages){
 stage.status='running';await save();
 const result=await new Promise(resolve=>{
  let output='';const child=spawn(process.execPath,[stage.script],{windowsHide:true,stdio:['ignore','pipe','pipe']});
  child.stdout.on('data',chunk=>output+=chunk);child.stderr.on('data',chunk=>output+=chunk);
  child.once('error',error=>resolve({exitCode:null,output:output+String(error)}));child.once('exit',exitCode=>resolve({exitCode,output}));
 });
 await writeFile(directory+'/'+stage.name+'.log',result.output);stage.exitCode=result.exitCode;stage.status=result.exitCode===0?'passed':'failed';await save();
 console.log(stage.name+': '+stage.status);
}
const retained={name:'retained-demo',status:'running'};report.stages.push(retained);
try{
 const incident=JSON.parse(await readFile('.local/operational-incidents-demo-v1/report.json','utf8'));
 const sql=JSON.parse(await readFile('.local/operational-incidents-demo-v1/sql-readback.json','utf8'));
 assert.equal(incident.passed,true);assert.equal(incident.reports.length,2);assert.deepEqual(incident.errors,[]);assert.equal(sql.passed,true);
 const retries=JSON.parse(await readFile('.local/operational-pack-demo-v1/retry-acceptance.json','utf8'));
 assert.equal(retries.passed,true);assert.equal(retries.sameOperations,true);assert.equal(retries.packExceptionTasks,1);assert.equal(retries.midExceptionTasks,1);
 retained.status='passed';
}catch(error){retained.status='failed';retained.error=String(error);}
report.finishedAt=new Date().toISOString();report.passed=report.stages.every(x=>x.status==='passed');await save();
if(!report.passed)process.exitCode=1;
console.log(`Operational evidence ${report.passed?'passed':'incomplete'}: ${directory}/report.json`);
