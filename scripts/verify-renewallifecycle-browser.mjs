import assert from 'node:assert/strict';
import {mkdir,writeFile} from 'node:fs/promises';
import {spawn} from 'node:child_process';
import {chromium} from 'playwright';

if(!process.argv.includes('--worker')) {
 await mkdir('.local/phase7-12-lifecycle-browser',{recursive:true});
 const child=spawn('dotnet',['test','backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj','--no-restore',...(process.argv.includes('--no-build')?['--no-build']:[]),'--filter',
  'FullyQualifiedName~RealSqlRenewalLifecycleBrowserUsesActualUiAndDateWorker','--logger','trx;LogFileName=sql.trx','--results-directory','.local/phase7-12-lifecycle-browser'],{stdio:['ignore','pipe','pipe'],windowsHide:true});
 let log='';child.stdout.on('data',value=>log+=value);child.stderr.on('data',value=>log+=value);
 const code=await new Promise((resolve,reject)=>{child.on('error',reject);child.on('close',resolve);});
 await writeFile('.local/phase7-12-lifecycle-browser.log',log);console.log(log.slice(-4500));assert.equal(code,0,'Renewal lifecycle browser scenarios failed; inspect the retained log.');
} else {
 const f=JSON.parse(process.env.COVER_RENEWAL_BROWSER_FIXTURE);assert.ok(['localhost','127.0.0.1','[::1]'].includes(new URL(f.apiOrigin).hostname));assert.equal(new URL(f.webOrigin).hostname,'127.0.0.1');
 const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1480,height:980}});page.setDefaultTimeout(45000);
 await page.clock.setFixedTime(new Date(f.clockNow));
 const errors=[];page.on('pageerror',error=>errors.push(error.message));
 // The production web bundle has its original local API rewrite. This proxy
 // sends every browser request to the isolated real Kestrel API instead;
 // it does not mock responses. SSR uses the same API via its runtime origin.
 await page.route('**/api/v1/**',async route=>{const url=new URL(route.request().url());const response=await route.fetch({url:f.apiOrigin+url.pathname+url.search});await route.fulfill({response});});
 async function read(){const response=await page.request.get(f.apiOrigin+`/api/v1/terms/${f.termId}/renewal-lifecycle`);assert.equal(response.status(),200,await response.text());return response.json();}
 async function capture(name,schema,data){const directory='.local/phase7-12-lifecycle-browser-responses';await mkdir(directory,{recursive:true});await writeFile(`${directory}/${f.product}-${f.automatic?'automatic':'manual'}-${name}.json`,JSON.stringify({schema,data}));}
 try {
  await page.goto(f.webOrigin+'/login');await page.getByLabel('Email address',{exact:true}).fill('underwriter@cover.example');await page.getByLabel('Password',{exact:true}).fill(process.env.COVER_RENEWAL_BROWSER_PASSWORD);
  await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(f.webOrigin+'/');
  await page.goto(f.webOrigin+`/policies/${f.policyId}`);const panel=page.getByRole('region',{name:'Renewal lifecycle',exact:true});
  await panel.getByText(f.automatic?'Renewal overdue':'Invitation not yet due',{exact:true}).waitFor();
  const initial=await read();assert.equal(initial.lapseEventId,null);assert.equal(initial.canLapse,true);
  await capture('before','RenewalLifecycleView',initial);
  await writeFile(f.ready,JSON.stringify({readyAt:new Date().toISOString(),state:initial.state}));
  if(!f.automatic){
   await page.getByLabel('Draft type',{exact:true}).selectOption('renewal');
   await page.getByLabel('Reason for draft',{exact:true}).fill('Fictional renewal draft for the lapse browser demonstration');
   await page.getByRole('button',{name:'Create servicing draft',exact:true}).click();await page.waitForURL(/\/drafts\//);
   for(const [next,style] of [['segmented bar','Segmented bar'],['breadcrumb','Breadcrumb'],['numbered rail','Numbered rail'],['side rail','Side rail']]){
    await page.getByRole('button',{name:'Switch to '+next,exact:true}).click();assert.equal(await page.getByRole('navigation',{name:'Renewal stages',exact:true}).getAttribute('data-style'),style);
   }
   await page.getByRole('button',{name:'Acquire editing lease',exact:true}).click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
   await panel.getByText('Invitation not yet due',{exact:true}).waitFor();
   await panel.getByLabel('Lapse reason',{exact:true}).fill('Fictional customer declined renewal during the browser demonstration');
   await panel.getByLabel('I confirm this renewal will not proceed.',{exact:true}).check();
   let headers,body;
   await page.route('**'+`/api/v1/terms/${f.termId}/lapse`,async route=>{headers=route.request().headers();body=route.request().postData();const response=await route.fetch({url:f.apiOrigin+`/api/v1/terms/${f.termId}/lapse`});assert.equal(response.status(),201);await capture('receipt','RenewalLapseReceipt',await response.json());await route.abort('failed');},{times:1});
   await panel.getByRole('button',{name:'Record renewal lapse',exact:true}).click();await panel.getByRole('button',{name:'Retry same lapse',exact:true}).waitFor();
   const retry=page.waitForRequest(request=>request.url().endsWith(`/terms/${f.termId}/lapse`));await panel.getByRole('button',{name:'Retry same lapse',exact:true}).click();const repeated=await retry;
   for(const header of ['idempotency-key','if-match'])assert.equal(repeated.headers()[header],headers[header]);assert.equal(repeated.postData(),body);
  }
  await panel.getByText('Renewal lapsed',{exact:true}).waitFor({timeout:90000});await panel.getByText('Demo notification: Delivered. No email is sent.',{exact:true}).waitFor({timeout:90000});
  const retained=await read();assert.equal(retained.lapseMode,f.automatic?'automatic':'manual');assert.equal(retained.timeline.expiringEnd,f.termEndsAt);assert.equal(retained.notificationState,'succeeded');assert.equal(retained.notificationAttempts.length,1);assert.equal(retained.notificationAttempts[0].outcome,'succeeded');
  if(!f.automatic)await capture('after','RenewalLifecycleView',retained);
  await panel.getByText('Notification attempts (1)',{exact:true}).click();await panel.scrollIntoViewIfNeeded();await page.screenshot({path:f.output+'/desktop.png'});
  await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await panel.scrollIntoViewIfNeeded();await page.screenshot({path:f.output+'/mobile.png'});
  await page.reload();await panel.getByText('Renewal lapsed',{exact:true}).waitFor();assert.equal((await read()).lapseEventId,retained.lapseEventId);
  if(!f.automatic){await page.getByText('Lapsed',{exact:true}).waitFor();assert.equal(await page.getByRole('button',{name:'Acquire editing lease',exact:true}).isDisabled(),true);}
  const policyResponse=await page.request.get(f.apiOrigin+`/api/v1/policies/${f.policyId}`);assert.equal(policyResponse.status(),200);const policy=await policyResponse.json();assert.equal(policy.versionId,f.originalVersionId);assert.equal(policy.coverageState,f.automatic?'expired':'active');
  assert.deepEqual(errors,[]);await writeFile(f.output+'/report.json',JSON.stringify({completedAt:new Date().toISOString(),product:f.product,automatic:f.automatic,lifecycle:retained,
   checks:['real Next.js and Kestrel UI','isolated SQL database','actual manual command or clock-driven worker','one persistent demo notification','retained expiry and policy version','reload and 390px containment',...(!f.automatic?['lost response exact retry']:['pre-deadline checkpoint then exact configured deadline'])]},null,2));
 }catch(error){await page.screenshot({path:f.output+'/failure.png'}).catch(()=>{});await writeFile(f.output+'/failure.txt',String(error)+'\n'+await page.locator('body').innerText().catch(()=>''));throw error;}
 finally{await browser.close();}
}
