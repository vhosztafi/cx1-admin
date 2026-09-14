import {chromium} from 'playwright';
import assert from 'node:assert/strict';
import {readFile,mkdir} from 'node:fs/promises';
import {execFile} from 'node:child_process';
import {promisify} from 'node:util';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
if(!['localhost','127.0.0.1'].includes(new URL(origin).hostname))throw Error('Local demo only.');
const run=promisify(execFile);
async function fixture(){const {stdout}=await run('dotnet',['backend/src/BackOffice.Api/bin/Debug/net10.0/BackOffice.Api.dll','--seed-agency-notification-demo'],{env:{...process.env,ASPNETCORE_ENVIRONMENT:'Development',DOTNET_ENVIRONMENT:'Development'},windowsHide:true,maxBuffer:8*1024*1024});return JSON.parse(stdout.trim().split(/\r?\n/).at(-1)).agencyId;}
const id=await fixture();const staleId=await fixture();
const password=(await readFile('.local/demo-password.txt','utf8')).trim();const output='.local/browser-evidence';await mkdir(output,{recursive:true});
const browser=await chromium.launch({channel:'chrome',headless:true});const context=await browser.newContext({viewport:{width:1560,height:1000}});const page=await context.newPage();page.setDefaultTimeout(20000);const errors=[];page.on('pageerror',error=>errors.push(error.message));
try{
 await page.goto(origin+'/login');await page.getByLabel('Email address',{exact:true}).fill('agency-admin@cover.example');await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');
 await page.goto(`${origin}/agents/${id}?tab=Activity`);
 await page.getByRole('table',{name:'Agency demo notifications'}).waitFor();const table=page.getByRole('table',{name:'Agency demo notifications'});
 await table.getByText('Demo delivered',{exact:true}).waitFor();await table.getByText('Rejected; retry unavailable',{exact:true}).waitFor();assert.equal(await page.getByRole('button',{name:'Retry delivery',exact:true}).count(),1);
 await page.getByRole('button',{name:'Retry delivery',exact:true}).click();assert.equal(await page.getByLabel('Reason for retry').evaluate(element=>element===document.activeElement),true);await page.getByLabel('Reason for retry',{exact:true}).fill('Fictional browser retry after demo provider recovery');
 await page.getByRole('link',{name:'Overview',exact:true}).click();await page.getByText('Finish or cancel this notification retry before leaving.',{exact:true}).waitFor();assert.equal(await page.getByLabel('Reason for retry').inputValue(),'Fictional browser retry after demo provider recovery');
 const keys=[];let lost=true;
 await page.route('**/api/v1/agencies/*/notifications/*/retry',async route=>{keys.push(route.request().headers()['idempotency-key']);if(lost){lost=false;const response=await route.fetch();assert.equal(response.status(),202);await route.abort('failed');}else await route.continue();});
 await page.getByRole('button',{name:'Queue retry',exact:true}).click();await page.getByRole('button',{name:'Retry same request',exact:true}).waitFor();assert.equal(await page.getByRole('button',{name:'Cancel retry',exact:true}).isDisabled(),true);assert.equal(await page.getByLabel('Reason for retry').isDisabled(),true);
 await page.getByRole('button',{name:'Retry same request',exact:true}).click();await page.getByText('Retry queued for demo delivery. Delivery has not yet been confirmed.',{exact:true}).waitFor();assert.equal(keys.length,2);assert.equal(keys[0],keys[1]);await table.getByText('Queued',{exact:true}).waitFor();await page.unroute('**/api/v1/agencies/*/notifications/*/retry');
 await page.screenshot({path:`${output}/agency-notifications-desktop.png`,fullPage:true});await page.setViewportSize({width:390,height:844});await page.screenshot({path:`${output}/agency-notifications-mobile.png`,fullPage:true});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=window.innerWidth),true);
 await page.setViewportSize({width:1560,height:1000});await page.goto(`${origin}/agents/${staleId}?tab=Activity`);await page.getByRole('button',{name:'Retry delivery',exact:true}).click();await page.getByLabel('Reason for retry').fill('Retain this fictional reason after a concurrent retry');
 const concurrent=await page.evaluate(async agency=>{const path=`/api/v1/agencies/${agency}/notifications`;const list=await(await fetch(path)).json();const item=list.items.find(x=>x.retryAllowed);const csrf=await(await fetch('/api/v1/auth/csrf')).json();const response=await fetch(`${path}/${item.id}/retry`,{method:'POST',headers:{'Content-Type':'application/json','X-CSRF-Token':csrf.requestToken,'If-Match':item.etag,'Idempotency-Key':crypto.randomUUID()},body:JSON.stringify({reason:'Fictional concurrent retry'})});return response.status;},staleId);assert.equal(concurrent,202);
 await page.getByRole('button',{name:'Queue retry',exact:true}).click();await page.getByRole('button',{name:'Reload notification status',exact:true}).waitFor();assert.equal(await page.getByLabel('Reason for retry').inputValue(),'Retain this fictional reason after a concurrent retry');await page.getByRole('button',{name:'Reload notification status',exact:true}).click();await page.getByText('This notification is no longer eligible for retry.',{exact:true}).waitFor();assert.equal(await page.getByRole('button',{name:'Queue retry',exact:true}).isDisabled(),true);await page.getByRole('button',{name:'Cancel retry',exact:true}).click();
 assert.deepEqual(errors,[]);console.log('Agency notifications browser passed: persisted outcomes, lost-response replay, navigation guard, retained stale reason, desktop/mobile.');
}catch(error){await page.screenshot({path:`${output}/agency-notifications-failure.png`,fullPage:true});throw error;}
finally{await context.close();await browser.close();}
