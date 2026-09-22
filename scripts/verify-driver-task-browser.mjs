import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {chromium} from 'playwright';
const sources=['scripts/verify-driver-task-browser.mjs','backend/tests/BackOffice.IntegrationTests/OperationalDriverTaskTests.cs','backend/tests/BackOffice.IntegrationTests/OperationalDriverTaskBrowser.cs','backend/src/BackOffice.Api/TaskDiscoveryEndpoints.cs','apps/backoffice/components/policies/policyriskhistory.tsx','apps/backoffice/components/policies/servicing-driver-editor.tsx','apps/backoffice/components/operations/driver-tasks.tsx'];
const hash=createHash('sha256');for(const path of sources){hash.update(path);hash.update((await readFile(path,'utf8')).replaceAll('\r\n','\n').trimEnd());}const sourceHash=hash.digest('hex');
if(!process.argv.includes('--worker')){
 const latest=JSON.parse(await readFile('.local/phase9-16-driver-browser/current.json','utf8'));
 const browser=JSON.parse(await readFile(latest.output+'/browser-report.json','utf8')),sql=JSON.parse(await readFile(latest.output+'/sql-readback.json','utf8'));
 assert.equal(browser.sourceHash,sourceHash);assert.equal(browser.passed,true);assert.equal(sql.passed,true);assert.equal(browser.taskId,sql.taskId);
 console.log('Driver referral task browser and exact SQL readback passed.');
}else{
 const f=JSON.parse(process.env.COVER_DRIVER_TASK_FIXTURE);assert.equal(new URL(f.apiOrigin).hostname,'127.0.0.1');assert.notEqual(new URL(f.apiOrigin).port,'5000');assert.equal(new URL(f.webOrigin).hostname,'127.0.0.1');
 const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1440,height:1000}});page.setDefaultTimeout(30000);
 const errors=[];page.on('pageerror',error=>errors.push(error.message));
 await page.route('**/api/v1/**',async route=>{const url=new URL(route.request().url());await route.fulfill({response:await route.fetch({url:f.apiOrigin+url.pathname+url.search})});});
 try{
  await page.goto(f.webOrigin+'/login');await page.getByLabel('Email address',{exact:true}).fill(f.email);await page.getByLabel('Password',{exact:true}).fill(process.env.COVER_DRIVER_TASK_PASSWORD);
  await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(f.webOrigin+'/');
  await page.goto(f.webOrigin+'/policies/'+f.policyId);await page.getByRole('tab',{name:'Drivers',exact:true}).click();
  await page.locator(`[data-risk-item-id="${f.driverId}"]`).click();
  const region=page.getByRole('region',{name:'Driver referral tasks',exact:true});
  const link=region.getByRole('link',{name:/Open referral task/});await link.waitFor();assert.equal(await link.count(),1);assert.equal(await link.getAttribute('href'),'/tasks/'+f.taskId);
  await region.scrollIntoViewIfNeeded();await page.screenshot({path:f.output+'/driver-task-desktop.png'});
  await page.setViewportSize({width:390,height:844});await link.focus();await region.scrollIntoViewIfNeeded();assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);
  await page.screenshot({path:f.output+'/driver-task-mobile.png'});await page.keyboard.press('Enter');await page.waitForURL(f.webOrigin+'/tasks/'+f.taskId);
  const savedResponse=await page.request.get(f.apiOrigin+'/api/v1/tasks/'+f.taskId);assert.equal(savedResponse.status(),200);
  const saved=await savedResponse.json();assert.equal(saved.id,f.taskId);
  await page.getByRole('heading',{name:saved.title,exact:true}).waitFor();await page.getByText(' / '+saved.reference,{exact:false}).first().waitFor();assert.deepEqual(errors,[]);
  await page.goto(f.webOrigin+'/drafts/'+f.draftId);
  const proposed=page.getByRole('region',{name:'Named driver changes',exact:true});await proposed.waitFor();
  const proposedLink=proposed.getByRole('link',{name:/Open referral task/});await proposedLink.waitFor();assert.equal(await proposedLink.getAttribute('href'),'/tasks/'+f.taskId);
  await proposedLink.focus();await proposedLink.scrollIntoViewIfNeeded();await page.screenshot({path:f.output+'/proposed-driver-task.png'});
  await page.keyboard.press('Enter');await page.waitForURL(f.webOrigin+'/tasks/'+f.taskId);await page.getByRole('heading',{name:saved.title,exact:true}).waitFor();assert.deepEqual(errors,[]);
  await writeFile(f.output+'/browser-report.json',JSON.stringify({passed:true,sourceHash,taskId:f.taskId,policyId:f.policyId,driverId:f.driverId,draftId:f.draftId,errors},null,2));
 }catch(error){await writeFile(f.output+'/failure.json',JSON.stringify({name:error.name,message:error.message.split('Call log:')[0]}));await page.screenshot({path:f.output+'/failure.png',fullPage:true}).catch(()=>{});throw error;}finally{await page.unrouteAll({behavior:'ignoreErrors'});await browser.close();}
}
