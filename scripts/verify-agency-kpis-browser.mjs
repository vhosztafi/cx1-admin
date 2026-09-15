import { chromium } from 'playwright';
import assert from 'node:assert/strict';
import { readFile, mkdir } from 'node:fs/promises';
const origin=process.env.COVER_WEB_ORIGIN??'http://127.0.0.1:3100';
if(!['localhost','127.0.0.1'].includes(new URL(origin).hostname))throw Error('Local demo only.');
const {agencyId}=JSON.parse(await readFile('.local/browser-evidence/agency-lifecycle-result.json','utf8'));
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const browser=await chromium.launch({channel:'chrome',headless:true});const contexts=[];const errors=[];
async function login(role){const context=await browser.newContext({viewport:{width:1560,height:1000}});contexts.push(context);const page=await context.newPage();page.setDefaultTimeout(20000);page.on('pageerror',e=>errors.push(e.message));await page.goto(origin+'/login');await page.getByLabel('Email address',{exact:true}).fill(role+'@cover.example');await page.getByLabel('Password',{exact:true}).fill(password);await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(origin+'/');return page;}
async function read(page,path){const r=await page.request.get(origin+path);assert.equal(r.status(),200);return{body:await r.json(),etag:r.headers().etag};}
async function post(page,path,data,etag){const csrf=(await read(page,'/api/v1/auth/csrf')).body.requestToken;const r=await page.request.post(origin+path,{data,headers:{'X-CSRF-Token':csrf,'Idempotency-Key':crypto.randomUUID(),'If-Match':etag}});assert.ok(r.ok(),String(r.status()));return await r.json();}
const base=`/api/v1/agencies/${agencyId}`;let page;
try{
 page=await login('agency-admin');const reviewer=await login('agency-reviewer');
 for(const request of (await read(page,base+'/permission-requests')).body.items.filter(x=>x.state==='pending'&&x.reason.startsWith('Fictional KPI browser ')))await post(reviewer,base+`/permission-requests/${request.id}/decision`,{outcome:'reject',reason:'Fictional KPI browser recovery'},request.etag);
 const before=(await read(page,'/api/v1/agencies/kpis')).body;
 const agency=await read(page,base);const reference=agency.body.reference;
 const directory=(await read(page,'/api/v1/agencies?q='+encodeURIComponent(reference))).body.items;assert.equal(directory.length,1);const rowBefore=directory[0].openActionCount;
 const request=await post(page,base+'/permission-requests',{permission:'bordereau-download',reason:'Fictional KPI browser '+crypto.randomUUID()},agency.etag);
 const current=(await read(page,'/api/v1/agencies/kpis')).body;assert.equal(current.openActions,before.openActions+1);assert.equal(current.openActions,current.pendingStateRequests+current.pendingTermsRequests+current.pendingPermissionRequests);
 await page.goto(origin+'/agents?q='+encodeURIComponent(reference));await page.getByRole('table',{name:'Agency directory',exact:true}).waitFor();
 const card=page.locator('.agency-kpis .panel').filter({hasText:'Open actions'});await card.locator('strong').filter({hasText:new RegExp('^'+current.openActions+'$')}).waitFor();
 const row=page.getByRole('table',{name:'Agency directory',exact:true}).getByRole('row').filter({hasText:reference});assert.ok((await row.locator('td').nth(7).innerText()).startsWith(String(rowBefore+1)));
 await page.getByText(/recorded follow-up obligations due by/).waitFor();
 await mkdir('.local/browser-evidence',{recursive:true});await page.screenshot({path:'.local/browser-evidence/agency-kpis-desktop.png',fullPage:true});
 await page.setViewportSize({width:390,height:844});assert.ok(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth));await page.screenshot({path:'.local/browser-evidence/agency-kpis-mobile.png',fullPage:true});
 const pending=(await read(page,base+'/permission-requests')).body.items.find(x=>x.id===request.id);
 await post(reviewer,base+`/permission-requests/${request.id}/decision`,{outcome:'reject',reason:'Fictional KPI browser completed'},pending.etag);
 await page.reload();await card.locator('strong').filter({hasText:new RegExp('^'+before.openActions+'$')}).waitFor();
 assert.ok((await row.locator('td').nth(7).innerText()).startsWith(String(rowBefore)));
 await page.route('**/api/v1/agencies/kpis',r=>r.fulfill({status:503,contentType:'application/problem+json',body:JSON.stringify({status:503,code:'service-unavailable'})}),{times:1});await page.reload();
 await page.getByRole('button',{name:'Try again',exact:true}).click();await card.locator('strong').filter({hasText:new RegExp('^'+before.openActions+'$')}).waitFor();
 assert.deepEqual(errors,[]);console.log('Agency KPI browser passed: actual pending request increments global and filtered row counts, independent rejection decrements them, persisted reload, due-obligation distinction, error retry and desktop/mobile containment.');
}catch(error){if(page)await page.screenshot({path:'.local/browser-evidence/agency-kpis-failure.png',fullPage:true});throw error;}finally{for(const context of contexts)await context.close();await browser.close();}
