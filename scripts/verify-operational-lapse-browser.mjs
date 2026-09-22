import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {chromium} from 'playwright';
const sources=['apps/backoffice/app/(workspace)/policies/[id]/page.tsx','apps/backoffice/components/policies/policy-record.tsx','apps/backoffice/components/policies/commercial-policy-record.tsx','scripts/verify-operational-lapse-browser.mjs','backend/tests/BackOffice.IntegrationTests/OperationalDemoRenewalTests.cs','backend/tests/BackOffice.IntegrationTests/OperationalDemoBrowserTests.cs',
 'backend/src/BackOffice.Infrastructure/Operations/LegacyOperationalBridge.Renewals.cs','backend/src/BackOffice.Infrastructure/Operations/ThreadService.Legacy.cs',
 'backend/src/BackOffice.Infrastructure/Operations/OperationalDemoSeed.cs','backend/src/BackOffice.Infrastructure/Policies/RenewalLifecycleService.cs',
 'backend/src/BackOffice.Infrastructure/Persistence/Migrations/LegacyRenewalCorrespondence.Guards.cs','apps/backoffice/components/policies/renewallifecycle.tsx','apps/backoffice/components/operations/thread.tsx'];
const hash=createHash('sha256');for(const path of sources){hash.update(path);hash.update((await readFile(path,'utf8')).replaceAll('\r\n','\n').trimEnd());}const sourceHash=hash.digest('hex');
if(!process.argv.includes('--worker')){
 for(const scenario of ['lapse-manual','lapse-automatic']){
  const latest=JSON.parse(await readFile(`.local/phase9-16-browser/${scenario}.json`,'utf8'));
  const report=JSON.parse(await readFile(latest.output+'/browser-report.json','utf8')),sql=JSON.parse(await readFile(latest.output+'/sql-readback.json','utf8'));
  assert.equal(latest.passed,true);assert.equal(sql.passed,true);assert.equal(report.sourceHash,sourceHash);assert.ok(report.cases.length>=5);assert.deepEqual(report.errors,[]);
  console.log(`${scenario}: ${report.cases.length} correspondence browser checks and SQL readback.`);
 }
}else{
 const f=JSON.parse(process.env.COVER_OPERATIONAL_DEMO_FIXTURE);assert.equal(new URL(f.apiOrigin).hostname,'127.0.0.1');assert.notEqual(new URL(f.apiOrigin).port,'5000');
 const browser=await chromium.launch({headless:true}),page=await browser.newPage({viewport:{width:1440,height:1000}});page.setDefaultTimeout(25000);
 const cases=[],errors=[];page.on('pageerror',e=>errors.push(e.message));
 await page.route('**/api/v1/**',async route=>{const url=new URL(route.request().url());const response=await route.fetch({url:f.apiOrigin+url.pathname+url.search});await route.fulfill({response});});
 const lifecycle=()=>page.evaluate(async id=>{const response=await fetch(`/api/v1/terms/${id}/renewal-lifecycle`);if(!response.ok)throw Error('Renewal unavailable');return response.json();},f.termId);
 try{
  await page.goto(f.webOrigin+'/login');await page.getByLabel('Email address',{exact:true}).fill('underwriter@cover.example');await page.getByLabel('Password',{exact:true}).fill(process.env.COVER_OPERATIONAL_DEMO_PASSWORD);
  await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(f.webOrigin+'/');
  await page.goto(f.webOrigin+`/policies/${f.policyId}`);const before=await lifecycle();assert.equal(before.state,'lapsed');assert.equal(before.correspondenceMessageId,f.messageId);
  await page.getByText(/Legacy demo notification record:/).waitFor();await page.getByRole('link',{name:'Open retained lapse correspondence',exact:true}).click();
  await page.getByRole('heading',{name:'Messages',exact:true}).waitFor();cases.push('lapse history distinguishes its legacy receipt and links actual policy correspondence');
  await page.getByRole('button',{name:/Recorded renewal lapse ·/}).click();const region=page.getByRole('region',{name:'Conversation messages'});
  await region.getByText('Staff retained and annotated the historical lapse notice.',{exact:true}).waitFor();cases.push('normal correspondence view preserves staff annotations across initialization');
  await region.getByText('Visible to internal users only',{exact:true}).waitFor();assert.equal(await region.getByRole('button',{name:'Send to agency',exact:true}).count(),0);
  await region.getByText('0 selected recipients · 0 exact file versions',{exact:true}).waitFor();cases.push('legacy notice association cannot silently resend or choose recipients');
  await region.scrollIntoViewIfNeeded();await page.screenshot({path:f.output+'/lapse-desktop.png'});
  await page.setViewportSize({width:390,height:844});await region.scrollIntoViewIfNeeded();assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);
  await page.screenshot({path:f.output+'/lapse-mobile.png'});cases.push('retained lapse correspondence fits mobile layout');
  await page.reload();await page.getByRole('button',{name:/Recorded renewal lapse ·/}).click();await page.getByText('Staff retained and annotated the historical lapse notice.',{exact:true}).waitFor();
  const after=await lifecycle();assert.equal(after.correspondenceMessageId,f.messageId);assert.deepEqual(after.notificationAttempts,before.notificationAttempts);assert.deepEqual(after.timeline,before.timeline);
  cases.push('reload preserves message identity, original attempts and expiry timeline');assert.deepEqual(errors,[]);
  await writeFile(f.output+'/browser-report.json',JSON.stringify({sourceHash,cases,errors},null,2));
 }catch(error){await page.screenshot({path:f.output+'/failure.png'}).catch(()=>{});await writeFile(f.output+'/browser-failure.json',JSON.stringify({sourceHash,cases,errors,error:String(error).split('\n')[0],locatorLog:error.name==='TimeoutError'?error.message:undefined},null,2));process.exitCode=1;}
 finally{await browser.close();}
}
