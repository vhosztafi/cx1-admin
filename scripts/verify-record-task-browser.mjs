import assert from 'node:assert/strict';
import {mkdir,readFile,writeFile,open,unlink} from 'node:fs/promises';
import {resolve,sep} from 'node:path';
import {chromium} from 'playwright';
import {openDemoJournal} from './demo-command-journal.mjs';

const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const output=process.argv[2];assert.ok(output&&resolve(output).startsWith(resolve('.local')+sep));
await mkdir(output,{recursive:true});await mkdir('.local/operational-record-tasks-v1',{recursive:true});
const fixtures=JSON.parse(await readFile('.local/operational-incidents-demo-v1/fixtures.json','utf8'));
assert.equal(fixtures.length,2);
const lockPath='.local/operational-record-tasks-v1/running.lock';
const lock=await open(lockPath,'wx');let browser;
const report={passed:false,checkedAt:new Date().toISOString(),checks:[],errors:[]};
try{
 await lock.writeFile(String(process.pid));
 const journal=await openDemoJournal('.local/operational-record-tasks-v1/commands.json',origin);
 browser=await chromium.launch({headless:true});
 const page=await browser.newPage({viewport:{width:1440,height:1000}});page.setDefaultTimeout(15000);
 page.on('pageerror',error=>report.errors.push(error.message));
 await page.goto(origin+'/login');await page.getByLabel('Email address',{exact:true}).fill('senior-underwriter@cover.example');
 await page.getByLabel('Password',{exact:true}).fill((await readFile('.local/demo-password.txt','utf8')).trim());
 await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 async function get(path){const r=await page.request.get(origin+path);assert.equal(r.status(),200);return r.json();}
 async function post(name,path,data){return journal.command(name,async()=>({path,data}),async(request,key)=>{
  const csrf=await get('/api/v1/auth/csrf');const r=await page.request.post(origin+request.path,{headers:{'X-CSRF-Token':csrf.requestToken,'Idempotency-Key':key},data:request.data});
  assert.ok(r.ok(),request.path+': '+r.status());return r.json();
 });}
 for(const fixture of fixtures){
  const policy=await get('/api/v1/policies/'+fixture.policyId);
  for(const [kind,id,path] of [['policy',fixture.policyId,'policies'],['quote',policy.sourceQuoteId,'quotes']]){
   const subject=await post(kind+':'+id+':subject','/api/v1/operational-subjects',{kind,parentId:id});
   const task=await post(kind+':'+id+':task','/api/v1/tasks',{subjectRecordId:subject.id,typeCode:'servicing',title:'Fictional demo: review saved '+kind+' operations',priority:'normal',assignment:{kind:'unassigned'}});
   const current=await get('/api/v1/tasks/'+task.id);
   await page.goto(origin+'/'+path+'/'+id);await page.getByRole('tab',{name:'Tasks',exact:true}).click();
   const row=page.getByRole('row').filter({has:page.getByRole('link',{name:current.reference,exact:true})});
   await row.waitFor();assert.ok((await row.innerText()).includes(current.title));
   await page.reload();await page.getByRole('tab',{name:'Tasks',exact:true}).click();await row.waitFor();
   await page.setViewportSize({width:390,height:844});await row.scrollIntoViewIfNeeded();
   assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);
   await page.screenshot({path:output+'/'+policy.snapshot.productCode+'-'+kind+'.png'});
   await row.getByRole('link',{name:current.reference,exact:true}).click();await page.waitForURL(origin+'/tasks/'+task.id);
   assert.equal((await get('/api/v1/tasks/'+task.id)).subjectRecordId,subject.id);
   report.checks.push({productCode:policy.snapshot.productCode,kind,recordId:id,subjectId:subject.id,taskId:task.id,reference:current.reference});
   await page.setViewportSize({width:1440,height:1000});
  }
 }
 assert.equal(report.checks.length,4);assert.deepEqual(report.errors,[]);report.passed=true;
}catch(error){report.failure=String(error);throw error;}
finally{await writeFile(output+'/report.json',JSON.stringify(report,null,2));await browser?.close();await lock.close();await unlink(lockPath);}
