import assert from 'node:assert/strict';
import {mkdir,readFile,writeFile} from 'node:fs/promises';
import {spawn} from 'node:child_process';

const directory='.local/phase7-14-browser-command';
await mkdir(directory,{recursive:true});
const child=spawn('dotnet',['test','backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj','--no-restore',
 ...(process.argv.includes('--no-build')?['--no-build']:[]),'--filter','FullyQualifiedName~RealSqlCancellationIssueBrowser',
 '--logger','trx;LogFileName=sql.trx','--results-directory',directory],{stdio:['ignore','pipe','pipe'],windowsHide:true});
let log='';child.stdout.on('data',value=>log+=value);child.stderr.on('data',value=>log+=value);
const code=await new Promise((resolve,reject)=>{child.on('error',reject);child.on('close',resolve);});
await writeFile(directory+'/run.log',log);console.log(log.slice(-5000));assert.equal(code,0,'Cancellation issue browser scenarios failed.');
const trx=await readFile(directory+'/sql.trx','utf8');
assert.match(trx,/<Counters\b[^>]*total="2"[^>]*executed="2"[^>]*passed="2"[^>]*failed="0"/,'Both real browser cases must pass.');
