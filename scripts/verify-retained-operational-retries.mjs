import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {chromium} from 'playwright';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const directory='.local/operational-pack-demo-v1',fixture=JSON.parse(await readFile(directory+'/agency-fixture.json','utf8'));
const work=JSON.parse(await readFile(directory+'/work.json','utf8')),mid=JSON.parse(await readFile('.local/phase9-16-mid-demo.json','utf8'));
const tasks=JSON.parse(await readFile('.local/phase9-16-retry-task-fixtures.json','utf8'));
const browser=await chromium.launch({headless:true}),page=await browser.newPage({viewport:{width:1440,height:1000}});page.setDefaultTimeout(30000);
const errors=[],cases=[];page.on('pageerror',e=>errors.push(e.message));
try{
 await page.goto(origin+'/login');await page.getByLabel('Email address',{exact:true}).fill('senior-underwriter@cover.example');await page.getByLabel('Password',{exact:true}).fill((await readFile('.local/demo-password.txt','utf8')).trim());
 await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 async function get(path){const r=await page.request.get(origin+path);assert.equal(r.status(),200,path);return r.json();}
 async function until(read,check){for(let n=0;n<120;n++){const value=await read();if(check(value))return value;await page.waitForTimeout(500);}throw Error('Original operation did not complete.');}
 async function screenshot(name,anchor){await anchor.scrollIntoViewIfNeeded();await page.screenshot({path:directory+'/'+name+'-desktop.png'});await page.setViewportSize({width:390,height:844});await anchor.scrollIntoViewIfNeeded();assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);await page.screenshot({path:directory+'/'+name+'-mobile.png'});await page.setViewportSize({width:1440,height:1000});}
 for(const taskId of [tasks.packTaskId,tasks.midTaskId]){
  await get('/api/v1/tasks/'+taskId);await page.goto(origin+'/tasks/'+taskId);await page.getByRole('heading',{level:1}).waitFor();
 }
 cases.push('Both original job exceptions open real scoped workflow tasks');
 const deliveries=await get(`/api/v1/records/${fixture.subjectId}/document-deliveries`),delivery=deliveries.items.find(x=>x.jobId===work.id);assert.ok(delivery);
 const route='/api/v1/document-deliveries/'+delivery.id;
 if(delivery.state==='failed'){
  const attempts=await get(route+'/attempts');assert.equal(attempts.items.length,6);assert.equal(delivery.retryAllowed,true);
  await writeFile(directory+'/pack-before-retry.json',JSON.stringify({delivery,attempts},null,2));
  await page.goto(origin+'/policies/'+fixture.policyId+'?tab=Documents');
  const history=page.getByRole('region',{name:'Delivery history',exact:true});await history.getByRole('button',{name:'View delivery and attempts',exact:true}).click();
  const details=page.getByRole('region',{name:'Delivery details',exact:true});await details.getByLabel('Reason',{exact:true}).fill('Retry the original fictional policy pack after checking its single exception task.');
  await details.getByRole('button',{name:'Retry delivery',exact:true}).click();
  await page.getByRole('dialog').getByRole('button',{name:'Retry delivery',exact:true}).click();await page.getByRole('dialog').waitFor({state:'hidden'});
 }
 const delivered=await until(()=>get(route),x=>x.state==='delivered');
 const packAttempts=await get(route+'/attempts');assert.equal(packAttempts.items.length,7);assert.equal(delivered.jobId,work.id);
 assert.deepEqual([...delivered.documentVersionIds].sort(),fixture.documents.map(x=>x.id).sort());
 for(const file of fixture.documents){const r=await page.request.get(origin+`/api/v1/document-versions/${file.id}/content`);assert.equal(r.status(),200);const bytes=await r.body();assert.equal(createHash('sha256').update(bytes).digest('hex'),file.sha256);await writeFile(directory+'/'+file.kind+'.pdf',bytes);}
 cases.push('Pack browser retry reuses the original job, exact three PDFs and seventh real attempt');
 await page.goto(origin+'/policies/'+fixture.policyId+'?tab=Documents');await page.getByRole('region',{name:'Delivery history',exact:true}).getByRole('button',{name:'View delivery and attempts',exact:true}).click();
 await page.getByRole('region',{name:'Delivery details',exact:true}).getByText(/Status: delivered/).waitFor();await page.getByRole('region',{name:'Delivery details',exact:true}).scrollIntoViewIfNeeded();await screenshot('pack-retry',page.getByRole('region',{name:'Delivery details',exact:true}));
 const midPath=`/api/v1/versions/${mid.versionId}/mid-submissions`,readMid=async()=>{const list=await get(midPath);const row=list.items.find(x=>x.id===mid.submissionId);assert.ok(row);return row;};
 const before=await readMid();assert.equal(before.exceptionTaskId,tasks.midTaskId);
 if(before.state==='failed'){
  assert.equal(before.attempts.length,6);await writeFile(directory+'/mid-before-retry.json',JSON.stringify(before,null,2));
  await page.goto(origin+'/policies/'+tasks.midPolicyId);await page.getByRole('tab',{name:'Vehicles',exact:true}).click();
  await page.getByLabel('Reason to retry',{exact:true}).fill('Retry the same fictional MID data after reviewing its original exception task.');
  await page.getByRole('button',{name:'Retry data submission',exact:true}).click();
  await page.getByRole('status').filter({hasText:'Submission queued for retry.'}).waitFor();
 }
 const accepted=await until(readMid,x=>x.state==='accepted');assert.equal(accepted.attempts.length,7);assert.equal(accepted.id,mid.submissionId);assert.equal(accepted.policyVersionId,mid.versionId);assert.equal(accepted.exceptionTaskId,tasks.midTaskId);
 await page.goto(origin+'/policies/'+tasks.midPolicyId);await page.getByRole('tab',{name:'Vehicles',exact:true}).click();await page.getByRole('heading',{name:'Initial issue · Accepted',exact:true}).waitFor();await page.getByRole('heading',{name:'MID submissions',exact:true}).scrollIntoViewIfNeeded();await screenshot('mid-retry',page.getByRole('heading',{name:'MID submissions',exact:true}));
 cases.push('MID browser retry retains submission/version/task and accepts the seventh real attempt');assert.deepEqual(errors,[]);
 await writeFile(directory+'/retry-browser.json',JSON.stringify({passed:true,checkedAt:new Date().toISOString(),cases,errors,workId:work.id,deliveryId:delivered.id,documentVersionIds:delivered.documentVersionIds,midWorkId:mid.workId,midSubmissionId:mid.submissionId,tasks,packAttempts:packAttempts.items.length,midAttempts:accepted.attempts.length},null,2));
 console.log('Retained pack and MID browser retries passed with exact original operations and files.');
}catch(error){await page.screenshot({path:directory+'/retry-failure.png'}).catch(()=>{});throw error;}finally{await browser.close();}

