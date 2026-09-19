import assert from 'node:assert/strict';
import {mkdir,writeFile,readFile} from 'node:fs/promises';
import {spawn} from 'node:child_process';
import {chromium} from 'playwright';

if(!process.argv.includes('--worker')) {
 const run=`.local/phase7-15-browser/${new Date().toISOString().replace(/[:.]/g,'-')}-${process.pid}`;
 await mkdir(run,{recursive:true});
 const child=spawn('dotnet',['test','backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj','--no-restore',...(process.argv.includes('--no-build')?['--no-build']:[]),
  '--filter','FullyQualifiedName~RealSqlPolicyHistoryBrowser','--logger','trx;LogFileName=sql.trx','--results-directory',run],{stdio:['ignore','pipe','pipe'],windowsHide:true});
 let log='';child.stdout.on('data',value=>log+=value);child.stderr.on('data',value=>log+=value);
 const code=await new Promise((resolve,reject)=>{child.on('error',reject);child.on('close',resolve);});
 await writeFile(run+'/driver.log',log);console.log(log.slice(-6000));assert.equal(code,0,'Policy history browser scenarios failed.');
 const trx=await readFile(run+'/sql.trx','utf8');assert.match(trx,/<Counters\b[^>]*total="2"[^>]*executed="2"[^>]*passed="2"[^>]*failed="0"/);
} else {
 const f=JSON.parse(process.env.COVER_HISTORY_BROWSER_FIXTURE);
 assert.ok(['localhost','127.0.0.1','[::1]'].includes(new URL(f.apiOrigin).hostname));assert.equal(new URL(f.webOrigin).hostname,'127.0.0.1');
 const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1480,height:980}});page.setDefaultTimeout(45000);
 await page.clock.setFixedTime(new Date(f.clockNow));const errors=[];page.on('pageerror',error=>errors.push(error.message));
 await page.route('**/api/v1/**',async route=>{const url=new URL(route.request().url());const response=await route.fetch({url:f.apiOrigin+url.pathname+url.search});await route.fulfill({response});});
 const button=name=>page.getByRole('button',{name,exact:true});
 const root=`/api/v1/policies/${f.policyId}`;
 async function read(path){const response=await page.request.get(f.apiOrigin+path);assert.equal(response.status(),200,await response.text());assert.equal(response.headers()['cache-control'],'no-store');return response.json();}
 try {
  await page.goto(f.webOrigin+'/login');await page.getByLabel('Email address',{exact:true}).fill('underwriter@cover.example');await page.getByLabel('Password',{exact:true}).fill(process.env.COVER_HISTORY_BROWSER_PASSWORD);await button('Sign in').click();await page.waitForURL(f.webOrigin+'/');
  await page.goto(f.webOrigin+`/policies/${f.policyId}`);
  await page.getByRole('heading',{name:'Transactions and versions',exact:true}).waitFor();
  await page.getByRole('heading',{name:'Servicing assignments',exact:true}).waitFor();
  await page.getByRole('heading',{name:'Premium at selected version',exact:true}).waitFor();
  const openingPolicy=await read(root);
  await page.locator(`a[href="/agencies/${openingPolicy.agencyId}"]`).first().click();
  await page.waitForURL(f.webOrigin+`/agencies/${openingPolicy.agencyId}`);
  await page.getByRole('heading').first().waitFor();
  await page.goto(f.webOrigin+`/policies/${f.policyId}`);
  await page.getByRole('tab',{name:'Cover',exact:true}).click();
  await page.getByRole('heading',{name:'Policy wording details',exact:true}).waitFor();
  await page.getByText('Law and jurisdiction',{exact:true}).waitFor();
  await page.getByRole('tab',{name:'Transactions',exact:true}).click();
  await page.getByRole('checkbox',{name:'Include draft transactions',exact:true}).check();
  await page.getByText('No active draft proposals for this term.',{exact:true}).waitFor();
  assert.equal((await read(root+'/history')).versions.length,2);
  await button('View document requests').last().click();
  await page.getByRole('table',{name:'Policy document requests',exact:true}).waitFor();
  assert.equal(await page.getByRole('table',{name:'Policy document requests',exact:true}).getByRole('row').count(),4);
  assert.equal(new URL(page.url()).searchParams.get('versionId'),f.originalVersionId);
  await page.getByRole('tab',{name:'Transactions',exact:true}).click();
  await page.getByLabel('Compare from',{exact:true}).selectOption(f.originalVersionId);await page.getByLabel('Compare to',{exact:true}).selectOption(f.changedVersionId);
  await button('Compare selected versions').click();await page.getByRole('region',{name:'Policy version comparison'}).locator('details').first().waitFor();
  const comparison=await read(root+`/compare?beforeVersionId=${f.originalVersionId}&afterVersionId=${f.changedVersionId}`);assert.ok(comparison.changes.length>0);assert.ok(comparison.changes.every(x=>!x.path.startsWith('/provenance')));
  const basePolicy=await read(root);
  for(const kind of ['Drivers','Vehicles']) {
   await page.getByRole('tab',{name:kind,exact:true}).click();
   await page.getByRole('button',{name:new RegExp(kind==='Drivers'?'^Open driver record:':'^Open vehicle record:')}).first().click();
   const history=page.getByRole('region',{name:'Risk item history'});await history.locator('details').first().waitFor();assert.equal(await history.locator('details').count(),2);
   const item=basePolicy.snapshot.risk[kind.toLowerCase()][0];
   const values=kind==='Drivers'?[item.fullName,item.firstName,item.surname,item.licence?.number]:[item.registration,item.make,item.model];
   for(const value of values.filter(x=>typeof x==='string'&&x.length))await history.locator('details[open]').getByText(value,{exact:true}).first().waitFor();
   if(kind==='Vehicles')await history.locator('details[open]').getByText('VIN',{exact:true}).waitFor();
   await history.locator('details[open]').getByRole('link',{name:'Open originating transaction',exact:true}).waitFor();
  }
  await page.getByLabel('Effective date and time (UTC)',{exact:true}).fill(f.changedEffectiveAt.replace(/(?:Z|\+00:00)$/,'').slice(0,19).replace(/:00$/, ''));
  await page.getByLabel('Known-at date and time (UTC)',{exact:true}).fill(f.clockNow.replace(/(?:Z|\+00:00)$/,'').slice(0,19).replace(/:00$/, ''));
  await button('View selected dates').click();await page.getByRole('tab',{name:'Transactions',exact:true}).click();await page.getByRole('heading',{name:'Cancellation transaction',exact:true}).waitFor();
  await page.getByLabel('Reason for clone or reconstruction',{exact:true}).fill('Reconstruct selected cancelled cover for demonstration');
  await button('Request reconstruction').click();await page.getByText('Reconstruction request saved. Document rendering is pending.',{exact:true}).waitFor();
  await page.reload();await page.getByRole('table',{name:'Saved policy reconstruction requests'}).waitFor();
  const requests=await read(root+'/reconstructions');assert.equal(requests.length,1);assert.equal(requests[0].versionId,f.changedVersionId);assert.equal(requests[0].state,'pending');
  await page.getByLabel('Effective date and time (UTC)',{exact:true}).fill('2026-11-01T00:00');
  await page.getByLabel('Known-at date and time (UTC)',{exact:true}).fill('2020-01-01T00:00');await button('View selected dates').click();
  await page.getByRole('heading',{name:'No cover recorded at these dates',exact:true}).waitFor();
  await page.getByLabel('Reconstruction term',{exact:true}).selectOption({index:1});
  await page.getByLabel('Reason for no-cover reconstruction',{exact:true}).fill('Record that no policy was known at the historical cutoff');
  await button('Retain no-cover reconstruction').click();await page.getByText('No-cover reconstruction saved. Rendering is pending.',{exact:true}).waitFor();
  const noCover=await read(root+'/reconstructions');assert.equal(noCover.length,2);assert.ok(noCover.some(x=>x.versionId===null&&x.contentHash===null&&x.coverageState==='not-covered'));
  await page.reload();await page.getByRole('heading',{name:'No cover recorded at these dates',exact:true}).waitFor();
  await button('View current policy').click();await page.getByRole('heading',{name:'Clone and reconstruct',exact:true}).waitFor();
  await page.getByLabel('Reason for clone or reconstruction',{exact:true}).fill('Clone existing cover to a fresh incomplete quotation');
  await button('Clone to New Quote').click();await page.getByRole('checkbox',{name:'I confirm the current agency terms and selected policy version.',exact:true}).check();
  let lost=false;
  await page.route(`**${root}/clone`,async route=>{const response=await route.fetch({url:f.apiOrigin+root+'/clone'});if(!lost&&response.status()===201){lost=true;await route.abort('failed');}else await route.fulfill({response});});
  await button('Create incomplete quote').click();await button('Retry saved request').waitFor();await button('Retry saved request').click();await page.getByRole('link',{name:'Open new quote',exact:true}).waitFor();assert.equal(lost,true);
  const quotePath=await page.getByRole('link',{name:'Open new quote',exact:true}).getAttribute('href');
  const quote=await read('/api/v1'+quotePath);assert.equal(quote.state,'draft');assert.equal(Object.hasOwn(quote.proposal,'termIntent'),false);assert.equal(Object.hasOwn(quote.proposal,'premium'),false);
  await page.screenshot({path:f.output+'/history-desktop.png',fullPage:true});
  await page.setViewportSize({width:390,height:844});await page.screenshot({path:f.output+'/history-mobile.png',fullPage:true});
  await page.getByRole('region',{name:'Saved policy reconstruction requests',exact:true}).screenshot({path:f.output+'/requests-mobile.png'});
  await page.getByRole('link',{name:'Open new quote',exact:true}).click();await page.waitForURL(f.webOrigin+quotePath);await page.reload();await page.getByRole('heading').first().waitFor();
  assert.deepEqual(errors,[]);await writeFile(f.output+'/result.json',JSON.stringify({product:f.product,policyId:f.policyId,quoteId:quote.id,requestId:requests[0].id,comparedChanges:comparison.changes.length,lostResponseRecovered:lost},null,2));
 } catch(error){await page.screenshot({path:f.output+'/failure.png',fullPage:true}).catch(()=>{});throw error;}
 finally{await page.unrouteAll({behavior:'wait'});await browser.close();}
}
