import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {createHash} from 'node:crypto';
import {chromium} from 'playwright';
const sources=['scripts/verify-cancellation-browser.mjs','backend/src/BackOffice.Application/Operations/CancellationOperationsRules.cs','backend/src/BackOffice.Infrastructure/Platform/SqlJobLeases.cs','backend/src/BackOffice.Infrastructure/Persistence/Migrations/CancellationOperations.Guards.cs','backend/src/BackOffice.Infrastructure/Persistence/CancellationOperationalRecords.cs','backend/tests/BackOffice.IntegrationTests/OperationalCancellationBrowserTests.cs',
 'backend/src/BackOffice.Api/CancellationOperationsEndpoints.cs','backend/src/BackOffice.Api/CancellationOperationsDispatcher.cs',
 ...['CancellationOperationsService','CancellationOperationsWorker','CancellationOperationsWorker.Notice','CancellationOperationsAuthority','CancellationNoticeDelivery','DeliveryReadService','MessageDeliveryService','DeliveryAuthority','MessageDeliveryService.Recovery','TaskService.Cancellation','DocumentService.Metadata'].map(x=>`backend/src/BackOffice.Infrastructure/Operations/${x}.cs`),
 'apps/backoffice/components/operations/cancellation-consequences.tsx','apps/backoffice/components/operations/delivery-history.tsx',
 'apps/backoffice/components/policies/policy-record.tsx','apps/backoffice/components/policies/commercial-policy-record.tsx'];
const hash=createHash('sha256');for(const source of sources){hash.update(source);hash.update((await readFile(source,'utf8')).replaceAll('\r\n','\n').trimEnd());}const sourceHash=hash.digest('hex');
if(!process.argv.includes('--worker')){
 for(const product of ['motor-trade','commercial']){
  const latest=JSON.parse(await readFile(`.local/phase9-15-browser/${product}.json`,'utf8'));
  const report=JSON.parse(await readFile(latest.output+'/browser-report.json','utf8')),sql=JSON.parse(await readFile(latest.output+'/sql-readback.json','utf8'));
  assert.equal(latest.passed,true);assert.equal(sql.passed,true);assert.equal(report.sourceHash,sourceHash);assert.ok(report.cases.length>=7);assert.deepEqual(report.errors,[]);
  console.log(`${product}: ${report.cases.length} cancellation browser checks and SQL readback.`);
 }
}else{
 const f=JSON.parse(process.env.COVER_CANCELLATION_BROWSER_FIXTURE),browser=await chromium.launch({headless:true});
 const page=await browser.newPage({viewport:{width:1440,height:1000}}),cases=[],errors=[];page.on('pageerror',e=>errors.push(e.message));page.setDefaultTimeout(25000);
 const button=name=>page.getByRole('button',{name,exact:true});assert.equal(new URL(f.apiOrigin).hostname,'127.0.0.1');assert.notEqual(new URL(f.apiOrigin).port,'5000');
 await page.route('**/api/v1/**',async route=>{const url=new URL(route.request().url());const response=await route.fetch({url:f.apiOrigin+url.pathname+url.search});await route.fulfill({response});});
 const history=()=>page.evaluate(async path=>{const response=await fetch(path,{credentials:'same-origin',cache:'no-store'});if(!response.ok)throw new Error('Cancellation history unavailable');return response.json();},`/api/v1/versions/${f.versionId}/cancellation-consequences`);
 try{
  await page.goto(f.webOrigin+'/login');await page.getByLabel('Email address',{exact:true}).fill(f.email);await page.getByLabel('Password',{exact:true}).fill(process.env.COVER_CANCELLATION_BROWSER_PASSWORD);await button('Sign in').click();await page.waitForURL(f.webOrigin+'/');
  await page.goto(f.webOrigin+`/policies/${f.policyId}`);await page.getByRole('tab',{name:'Transactions',exact:true}).click();await page.getByRole('heading',{name:'Cancellation operations',exact:true}).waitFor();
  let saved;for(let n=0;n<160;n++){saved=(await history()).items;if(saved.some(x=>x.kind==='notice'&&x.state==='delivery-delivered'&&x.appliedAt)&&saved.some(x=>x.kind==='task-close'&&x.state==='eligible-tasks-closed')&&(!f.expectsWithdrawal||saved.some(x=>x.kind==='certificate-withdrawal'&&x.state==='certificate-withdrawn')))break;await page.waitForTimeout(250);}
  assert.equal(saved.length,f.commercial?2:4);assert.ok(saved.every(x=>x.policyVersionId===f.versionId));assert.equal(saved.some(x=>x.kind==='certificate-withdrawal'),f.expectsWithdrawal);assert.equal(saved.some(x=>x.kind==='mid-removal'),!f.commercial);
  assert.equal(saved.find(x=>x.kind==='notice').state,'delivery-delivered');assert.ok(saved.find(x=>x.kind==='notice').appliedAt,'Original notice completion receipt must be applied after provider delivery');cases.push('exact saved cancellation version exposes only product-applicable consequences');
  await button('Refresh cancellation operations').click();await page.getByText(/Delivered by demo adapter/).waitFor();await page.getByText(/No eligible renewal tasks to close/).waitFor();cases.push('persisted notice delivery and effective task outcome render after refresh');
  await page.getByText('Withdrawal and renewal task closure take effect at the recorded cancellation time. A posted credit is not a cash refund.').waitFor();if(f.commercial){assert.equal(await page.getByText('Unavailable',{exact:true}).count(),0);await page.getByText(/−£/).first().waitFor();}cases.push('posted credit renders as a signed amount and remains separate from cash refund');
  await button('View cancellation notice').click();const dialog=page.getByRole('dialog');await dialog.getByRole('link',{name:/Download version/}).waitFor();await dialog.getByText(/cancellation-notice.pdf/).first().waitFor();await button('Close preview').click();cases.push('notice opens the real persisted PDF and exact-version download');
  await button('View notice delivery').click();const delivery=page.getByRole('region',{name:'Delivery details'});await delivery.getByText(/Status: delivered/).waitFor();assert.equal(await button('Resend original delivery').count(),0);await delivery.getByText(/Attempt 1/).waitFor();cases.push('original provider receipt and attempt are visible without a duplicate resend action');
  await page.getByRole('heading',{name:'Cancellation operations',exact:true}).scrollIntoViewIfNeeded();await page.screenshot({path:f.output+'/cancellation-desktop.png'});
  await page.setViewportSize({width:390,height:844});await page.getByRole('heading',{name:'Cancellation operations',exact:true}).scrollIntoViewIfNeeded();assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth+1),true);await page.screenshot({path:f.output+'/cancellation-mobile.png'});cases.push('cancellation outcomes fit the narrow viewport');
  await page.reload();await page.getByRole('tab',{name:'Transactions',exact:true}).click();await button('View notice delivery').waitFor();const after=(await history()).items;assert.deepEqual(after.map(x=>[x.id,x.deliveryId,x.documentVersionId]),saved.map(x=>[x.id,x.deliveryId,x.documentVersionId]));cases.push('reload retains original document delivery and consequence identities');
  assert.deepEqual(errors,[]);await writeFile(f.output+'/browser-report.json',JSON.stringify({sourceHash,cases,errors},null,2));
 }catch(error){await page.screenshot({path:f.output+'/failure.png'}).catch(()=>{});await writeFile(f.output+'/browser-failure.json',JSON.stringify({sourceHash,cases,errors,error:String(error).split('\n')[0],locatorLog:error.name==='TimeoutError'?error.message:undefined},null,2));process.exitCode=1;}finally{await browser.close();}
}
