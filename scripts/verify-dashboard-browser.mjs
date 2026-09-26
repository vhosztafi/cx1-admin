import {chromium} from 'playwright';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import assert from 'node:assert/strict';
const fixture=JSON.parse(await readFile('.local/phase11-fixture/fixture.json','utf8'));assert.match(fixture.database,/^CoverMGA_Test_Phase11_[a-f0-9]{32}$/);
const origin='http://localhost:3193',password=(await readFile('.local/phase11-fixture/password.txt','utf8')).trim();
const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1440,height:1000}});page.setDefaultTimeout(60000);
const evidence={passed:false,checks:[],saved:{}};
async function get(path){const r=await page.request.get(origin+'/api/v1'+path);assert.equal(r.status(),200,path);return r.json();}
async function post(path,data){const token=(await get('/auth/csrf')).requestToken;const r=await page.request.post(origin+'/api/v1'+path,{headers:{'X-CSRF-Token':token,'Idempotency-Key':crypto.randomUUID()},data});assert.ok(r.ok(),await r.text());return r.status()===204?null:r.json();}
try{
 await page.goto(origin+'/login');await page.getByLabel('Email address',{exact:true}).fill('underwriter@cover.example');await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 const actor=await get('/account');const agencies=await get('/search?kind=agency');assert.ok(agencies.items.length);
 const subject=await post('/operational-subjects',{kind:'agency',parentId:agencies.items[0].id});
 let task=(await get('/notifications')).find(x=>x.text==='Fictional Phase 12 dashboard review');
 if(!task){task=await post('/tasks',{subjectRecordId:subject.id,typeCode:'underwriting',title:'Fictional Phase 12 dashboard review',priority:'normal',assignment:{kind:'user',ownerId:actor.id},dueOn:'2026-09-01'});}
 evidence.saved.task=task.id;await page.reload();const dashboard=await get('/dashboard');const queue=dashboard.queues.find(x=>x.code==='tasks');assert.ok(queue.count>0);assert.ok(queue.overdue>0);assert.ok(queue.items.some(x=>x.id===task.id));
 await page.getByRole('table',{name:'Open tasks',exact:true}).getByRole('link').filter({hasText:queue.items.find(x=>x.id===task.id).reference}).click();await page.waitForURL('**/tasks/'+task.id);evidence.checks.push('saved task count, overdue measure and detail link');
 await page.goto(origin+'/alerts');const row=page.getByRole('table',{name:'My alerts',exact:true}).getByRole('row').filter({hasText:'Fictional Phase 12 dashboard review'});await row.waitFor();
 if(await row.getByRole('button',{name:'Mark read',exact:true}).count()){const saved=page.waitForResponse(r=>r.url().endsWith('/notifications/'+task.id+'/read'));await row.getByRole('button',{name:'Mark read',exact:true}).click();assert.equal((await saved).status(),204);}
 await page.reload();await page.getByRole('row').filter({hasText:'Fictional Phase 12 dashboard review'}).getByText('Read',{exact:true}).waitFor();assert.equal((await get('/notifications')).find(x=>x.id===task.id).read,true);evidence.checks.push('own alert acknowledgement survives reload');
 await page.goto(origin+'/');await page.getByRole('table',{name:'Open tasks',exact:true}).waitFor();await mkdir('output/playwright',{recursive:true});await page.screenshot({path:'output/playwright/phase12-dashboard.png',fullPage:true});evidence.passed=true;
}finally{await mkdir('.local/phase12-tests',{recursive:true});await writeFile('.local/phase12-tests/dashboard-browser.json',JSON.stringify(evidence,null,2));await browser.close();}
console.log(JSON.stringify(evidence));
