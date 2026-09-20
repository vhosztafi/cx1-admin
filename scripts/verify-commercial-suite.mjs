import assert from 'node:assert/strict';
import {spawn} from 'node:child_process';
import {createHash,randomUUID} from 'node:crypto';
import {mkdir,readFile,writeFile,copyFile,stat} from 'node:fs/promises';
import {resolve} from 'node:path';

// Each stage creates its own SQL database, API and browser policy base. The
// retained business demo is not a prerequisite or a source of canned success.
assert.equal(process.argv.length,2,'Full commercial acceptance does not accept stage filters.');
assert.ok(process.env.COVER_SQL_TEST_CONNECTION,'Real SQL is required.');
assert.ok(process.env.COVER_COMMERCIAL_UNIT_TRX,'Supply the current full unit TRX for strict combined accounting.');
assert.ok(process.env.COVER_ACCEPTANCE_NOT_BEFORE_UTC,'Supply the acceptance run start cutoff.');
const dist=process.env.COVER_NEXT_DIST_DIR??'.next';
await stat(`apps/backoffice/${dist}/BUILD_ID`);
const startedAt=new Date().toISOString(),directory=`.local/commercial-suite/${startedAt.replaceAll(/[:.]/g,'-')}-${randomUUID()}`;
await mkdir(directory,{recursive:true});await mkdir(directory+'/trx');
const stages=[
 ['capture-business-loss-and-cover','04',['--stage','full'],'RealSqlCommercialCaptureFullBrowser'],
 ['referral-proof-and-query','06',['--stage','underwriting'],'RealSqlCommercialReferralBrowser'],
 ['policy-views-editors-and-adjustment','12',['--stage','issue','--servicing','--servicing-issue'],'RealSqlCommercialServicingIssueBrowser'],
 ['renewal-experience-terms-and-issue','13',['--stage','issue','--renewal'],'RealSqlCommercialRenewalBrowser'],
 ['cancellation-credit-and-release','14',['--stage','issue','--cancellation'],'RealSqlCommercialCancellationBrowser'],
].map(([name,plan,args,test])=>({name,plan,args,test,status:'not-run'}));
const sources=['verify-commercial-capture-browser','verify-commercial-underwriting-browser','verify-commercial-issue-browser','verify-commercial-policy-browser',
 'verify-commercial-servicing-editor-browser','verify-commercial-servicing-issue-browser','verify-commercial-renewal-browser','verify-commercial-cancellation-browser','validate-underwriting-response'];
const sourceHashes={};for(const name of sources){const path=`scripts/${name}.mjs`;sourceHashes[path]=createHash('sha256').update(await readFile(path)).digest('hex');}
const unit=await readFile(process.env.COVER_COMMERCIAL_UNIT_TRX,'utf8');
const unitCount=Number(unit.match(/<Counters\b[^>]*\btotal="(\d+)"/)?.[1]);assert.ok(unitCount>=1091,'Expected full current unit coverage.');
await copyFile(process.env.COVER_COMMERCIAL_UNIT_TRX,directory+'/trx/unit.trx');
const assembly=process.env.COVER_COMMERCIAL_TEST_ASSEMBLY;
const assemblyHash=assembly?createHash('sha256').update(await readFile(assembly)).digest('hex'):undefined;
const report={startedAt,passed:false,dist,assembly,assemblyHash,sourceHashes,stages};
const save=()=>writeFile(directory+'/report.json',JSON.stringify(report,null,2));await save();
async function run(command,args,logPath){
 let log='';const result=await new Promise(resolve=>{
  const child=spawn(command,args,{stdio:['ignore','pipe','pipe'],windowsHide:true,env:process.env});
  child.stdout.on('data',x=>log+=x);child.stderr.on('data',x=>log+=x);
  child.once('error',error=>resolve({exitCode:null,error:error.message}));child.once('close',(exitCode,signal)=>resolve({exitCode,signal}));
 });
 await writeFile(logPath,log);return{...result,logPath,logSha256:createHash('sha256').update(log).digest('hex')};
}
try{
 for(const stage of stages){
  stage.status='running';stage.startedAt=new Date().toISOString();await save();console.log('Commercial acceptance: '+stage.name);
  Object.assign(stage,await run(process.execPath,['scripts/verify-commercial-capture-browser.mjs','--no-build',...stage.args],`${directory}/${stage.name}.log`));
  assert.equal(stage.exitCode,0,`Inspect ${stage.logPath}`);
  const pointer=`.local/phase8-${stage.plan}-browser-current.txt`;assert.ok((await stat(pointer)).mtimeMs>=Date.parse(stage.startedAt));
  const output=(await readFile(pointer,'utf8')).trim();assert.match(output,new RegExp(`^\\.local/phase8-${stage.plan}-browser-[a-f0-9-]+$`));
  const trx=await readFile(output+'/sql.trx','utf8');assert.ok(trx.includes(stage.test));
  await copyFile(output+'/sql.trx',`${directory}/trx/${stage.plan}.trx`);
  stage.resultsDirectory=output;stage.status='passed';stage.finishedAt=new Date().toISOString();await save();
 }
 for(const [path,hash] of Object.entries(sourceHashes))assert.equal(createHash('sha256').update(await readFile(path)).digest('hex'),hash,'Browser source changed during acceptance.');
 if(assembly)assert.equal(createHash('sha256').update(await readFile(assembly)).digest('hex'),assemblyHash,'Test assembly changed during acceptance.');
 report.strictGate=await run('pwsh',['-NoProfile','-File','scripts/assert-test-results.ps1','-ResultsDirectory',resolve(directory+'/trx'),
  '-MinimumTests',String(unitCount+stages.length),'-MinimumSqlTests',String(stages.length),'-NotBeforeUtc',process.env.COVER_ACCEPTANCE_NOT_BEFORE_UTC],directory+'/strict-gate.log');
 assert.equal(report.strictGate.exitCode,0,'Commercial TRX accounting failed.');report.passed=true;
}catch(error){report.error=String(error.message);const running=stages.find(x=>x.status==='running');if(running){running.status='failed';running.finishedAt=new Date().toISOString();}process.exitCode=1;}
report.finishedAt=new Date().toISOString();await save();
console.log(`Commercial acceptance ${report.passed?'passed':'incomplete'}: ${directory}/report.json`);
