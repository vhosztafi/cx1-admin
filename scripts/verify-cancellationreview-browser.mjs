import assert from 'node:assert/strict';
import {mkdir,writeFile,readFile} from 'node:fs/promises';
import {spawn} from 'node:child_process';
import {chromium} from 'playwright';

if(!process.argv.includes('--worker')) {
 await mkdir('.local/phase7-13-browser',{recursive:true});
 const child=spawn('dotnet',['test','backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj','--no-restore',...(process.argv.includes('--no-build')?['--no-build']:[]),
  '--filter','FullyQualifiedName~RealSqlCancellationReviewBrowser','--logger','trx;LogFileName=sql.trx','--results-directory','.local/phase7-13-browser'],{stdio:['ignore','pipe','pipe'],windowsHide:true});
 let log='';child.stdout.on('data',value=>log+=value);child.stderr.on('data',value=>log+=value);
 const code=await new Promise((resolve,reject)=>{child.on('error',reject);child.on('close',resolve);});
 await writeFile('.local/phase7-13-browser.log',log);console.log(log.slice(-5000));assert.equal(code,0,'Cancellation browser scenarios failed.');
 const trx=await readFile('.local/phase7-13-browser/sql.trx','utf8');assert.match(trx,/<Counters\b[^>]*total="2"[^>]*executed="2"[^>]*passed="2"[^>]*failed="0"/,'Both actual browser cases must pass.');
} else {
 const f=JSON.parse(process.env.COVER_CANCELLATION_BROWSER_FIXTURE);
 assert.ok(['localhost','127.0.0.1','[::1]'].includes(new URL(f.apiOrigin).hostname));assert.equal(new URL(f.webOrigin).hostname,'127.0.0.1');
 const browser=await chromium.launch({channel:'chrome',headless:true});const page=await browser.newPage({viewport:{width:1480,height:980}});page.setDefaultTimeout(45000);
 await page.clock.setFixedTime(new Date(f.clockNow));const errors=[];page.on('pageerror',error=>errors.push(error.message));
 await page.route('**/api/v1/**',async route=>{const url=new URL(route.request().url());const response=await route.fetch({url:f.apiOrigin+url.pathname+url.search});await route.fulfill({response});});
 const button=name=>page.getByRole('button',{name,exact:true});
 async function login(email){await page.goto(f.webOrigin+'/login');await page.getByLabel('Email address',{exact:true}).fill(email);await page.getByLabel('Password',{exact:true}).fill(process.env.COVER_CANCELLATION_BROWSER_PASSWORD);await button('Sign in').click();await page.waitForURL(f.webOrigin+'/');}
 let draftId;
 async function read(){const response=await page.request.get(f.apiOrigin+`/api/v1/drafts/${draftId}/cancellation-preview`);assert.equal(response.status(),200,await response.text());assert.equal(response.headers()['cache-control'],'no-store');return response.json();}
 async function capture(name,schema,data){const content=JSON.stringify({schema,data},null,2);await writeFile(f.output+'/'+name+'.json',content);const dir=f.issue?'.local/phase7-14-browser-responses':'.local/phase7-13-browser-responses';await mkdir(dir,{recursive:true});await writeFile(`${dir}/${f.product}-${name}.json`,content);}
 async function save(){const response=page.waitForResponse(r=>r.url().endsWith(`/drafts/${draftId}/proposal`)&&r.request().method()==='PUT');await button('Save draft').click();assert.equal((await response).status(),200);}
 try {
  await login('underwriter@cover.example');await page.goto(f.webOrigin+`/policies/${f.policyId}`);
  await button('Cancel policy').first().click();assert.equal(await page.getByLabel('Draft type',{exact:true}).inputValue(),'cancellation');await page.getByLabel('Requested effective date',{exact:true}).fill('2026-10-15');
  await page.getByLabel('Reason for draft',{exact:true}).fill('Fictional insured cancellation for browser acceptance');await button('Create servicing draft').click();await page.waitForURL(/\/drafts\//);
  draftId=new URL(page.url()).pathname.split('/').at(-1);await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await page.getByLabel('Cancellation reason',{exact:true}).selectOption('insured-request');await save();const before=await read();
  await page.getByRole('link',{name:'Amend reason or date',exact:true}).click();await page.getByLabel('Effective date (London)',{exact:true}).fill('2026-10-16');await save();const after=await read();
  assert.notEqual(before.previewHash,after.previewHash);assert.notEqual(before.amounts.posting.premium,after.amounts.posting.premium);
  await page.getByLabel('Cancellation reason',{exact:true}).selectOption('non-payment');await save();const reasonChanged=await read();assert.notEqual(after.previewHash,reasonChanged.previewHash);
  assert.ok(reasonChanged.blockers.includes('cancellation-notice-delivery-required'));
  await page.getByLabel('Cancellation evidence purpose',{exact:true}).selectOption('cancellation-notice');
  await page.getByLabel('Cancellation evidence file',{exact:true}).setInputFiles({name:'cancellation-notice.txt',mimeType:'text/plain',buffer:Buffer.from('Fictional delivered notice for the exact cancellation proposal.')});
  await page.getByLabel('Notice delivered at (UTC)',{exact:true}).fill(f.clockNow.slice(0,16));await button('Upload cancellation evidence').click();
  await page.getByLabel('Cancellation evidence to review',{exact:true}).selectOption({label:'cancellation-notice.txt · unreviewed'});
  await page.getByLabel('Cancellation evidence review reason',{exact:true}).fill('Accepted fictional notice and checked its recorded delivery');await button('Save cancellation evidence review').click();
  await page.getByRole('cell',{name:'accepted',exact:true}).waitFor();
  const reviewed=await read();assert.deepEqual(reviewed.blockers,[]);assert.equal(reviewed.canApprove,false);
  let headers,body;
  await page.route('**'+`/api/v1/drafts/${draftId}/cancellation-preview`,async route=>{
   if(route.request().method()!=='POST'){await route.fallback();return;}
   headers=route.request().headers();body=route.request().postData();const response=await route.fetch({url:f.apiOrigin+`/api/v1/drafts/${draftId}/cancellation-preview`});assert.equal(response.status(),201);
   await capture('receipt','CancellationReviewReceipt',await response.json());await route.abort('failed');await page.unroute('**'+`/api/v1/drafts/${draftId}/cancellation-preview`);
  });
  await button('Retain reviewed preview').click();await button('Retry same cancellation action').waitFor();
  const request=page.waitForRequest(r=>r.url().endsWith(`/drafts/${draftId}/cancellation-preview`)&&r.method()==='POST');await button('Retry same cancellation action').click();const repeated=await request;
  for(const key of ['idempotency-key','if-match','x-edit-lease'])assert.equal(repeated.headers()[key],headers[key]);assert.equal(repeated.postData(),body);
  await page.getByText('Awaiting approval',{exact:true}).waitFor();assert.equal(await button('Approve cancellation').isDisabled(),true);
  await button('Release editing lease').click();await page.getByRole('heading',{name:'Read-only draft',exact:true}).waitFor();
  await page.context().clearCookies();await login('senior-underwriter@cover.example');await page.goto(f.webOrigin+`/drafts/${draftId}`);
  await button('Acquire editing lease').click();await page.getByRole('heading',{name:'You are editing this draft',exact:true}).waitFor();
  await page.getByLabel('Cancellation approval reason',{exact:true}).fill('Independent senior approval of the exact cancellation preview');await button('Approve cancellation').click();
  await page.getByText('Cancellation approved',{exact:true}).waitFor();const approved=await read();assert.ok(approved.approvalId);assert.equal(approved.previewHash,reviewed.previewHash);
  await capture('approved','CancellationReviewView',approved);
  const expectedMoney=value=>`${value.startsWith('-')?'−':''}£${value.replace('-','').replace(/\B(?=(\d{3})+(?!\d))/g,',')}`;
  assert.ok((await page.locator('#cancellation-review').innerText()).includes(expectedMoney(approved.amounts.posting.invoiceDue)),'The calculated signed debtor credit must be displayed.');
  assert.equal((await page.locator('#cancellation-review').innerText()).includes('Unavailable'),false,'Every posted amount must render.');
  const evidence=await page.request.get(f.apiOrigin+`/api/v1/drafts/${draftId}/cancellation-evidence`);assert.equal(evidence.status(),200);await capture('evidence','CancellationEvidencePage',await evidence.json());
  await page.locator('#cancellation-review').evaluate(node=>node.scrollIntoView({block:'start'}));await page.screenshot({path:f.output+'/desktop.png'});
  await page.setViewportSize({width:390,height:844});assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await page.screenshot({path:f.output+'/mobile.png'});
  await page.reload();await page.getByText('Cancellation approved',{exact:true}).waitFor();assert.equal((await read()).approvalId,approved.approvalId);
  assert.match(await page.locator('body').innerText(),/Cash paid: £0.00/);
  await page.setViewportSize({width:1480,height:980});await button('Acquire editing lease').click();
  if(f.issue){
   await page.getByLabel('Cancellation issue reason',{exact:true}).fill('Issue the independently approved fictional cancellation');
   await page.getByLabel('I confirm the cancellation effective time and reviewed financial movement.',{exact:true}).check();
   let originalHeaders,originalBody,receipt;
   const issueUrl=`/api/v1/drafts/${draftId}/cancellation-issue`;
   await page.route('**'+issueUrl,async route=>{
    if(route.request().method()!=='POST'){await route.fallback();return;}
    originalHeaders=route.request().headers();originalBody=route.request().postData();
    const response=await route.fetch({url:f.apiOrigin+issueUrl});assert.equal(response.status(),201,await response.text());
    receipt=await response.json();await capture('issue-receipt','CancellationIssueReceipt',receipt);
    await route.abort('failed');await page.unroute('**'+issueUrl);
   });
   await button('Issue approved cancellation').click();await button('Retry same cancellation action').waitFor();
   const retried=page.waitForRequest(r=>r.url().endsWith(issueUrl)&&r.method()==='POST');
   await button('Retry same cancellation action').click();const retryRequest=await retried;
   for(const key of ['idempotency-key','if-match','x-edit-lease'])assert.equal(retryRequest.headers()[key],originalHeaders[key]);
   assert.equal(retryRequest.postData(),originalBody);
   await page.getByRole('heading',{name:'Issued cancellation',exact:true}).waitFor();
   await page.getByText('Demo delivery recorded',{exact:true}).waitFor();
   let issued;
   for(let attempt=0;attempt<60;attempt++){
    const response=await page.request.get(f.apiOrigin+issueUrl);assert.equal(response.status(),200);assert.equal(response.headers()['cache-control'],'no-store');
    issued=await response.json();
    if(issued.consequences.some(x=>x.kind==='cancellation-notice'&&x.state==='succeeded'&&x.noticeOutcome==='demo-delivered'))break;
    await new Promise(resolve=>setTimeout(resolve,500));
   }
   assert.equal(issued.transactionId,receipt.transactionId);assert.equal(issued.netAmount,approved.amounts.posting.invoiceDue);
   await capture('issued','CancellationIssuedView',issued);
   assert.deepEqual(issued.consequences.map(x=>x.id).sort(),receipt.consequenceIds.slice().sort());
   const notice=issued.consequences.filter(x=>x.kind==='cancellation-notice');
   assert.equal(notice.length,1);assert.equal(notice[0].state,'succeeded');assert.equal(notice[0].noticeOutcome,'demo-delivered');
   assert.equal(issued.consequences.filter(x=>x.state==='pending').length,2);
   if(issued.consequences.some(x=>x.state==='failed'))await page.getByText('Action failed. Review before retrying.',{exact:true}).first().waitFor();
   await page.reload();await page.getByText('Demo delivery recorded',{exact:true}).waitFor();
   const receiptPanel=page.locator('section.panel').filter({has:page.getByRole('heading',{name:'Issued cancellation',exact:true})});
   await receiptPanel.screenshot({path:f.output+'/issued-desktop.png'});await page.setViewportSize({width:390,height:844});
   assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);await receiptPanel.screenshot({path:f.output+'/issued-mobile.png'});
   await page.getByRole('link',{name:'View cancellation transaction',exact:true}).click();
   await page.getByRole('heading',{name:'Cancellation transaction',exact:true}).waitFor();
   const policy=await page.request.get(f.apiOrigin+`/api/v1/policies/${f.policyId}/terms/${issued.termId}/versions/${issued.versionId}`);
   assert.equal(policy.status(),200);const savedPolicy=await policy.json();await capture('policy','CancellationPolicyView',savedPolicy);
   assert.equal(savedPolicy.cancellationApprovalId,approved.approvalId);assert.equal(savedPolicy.ratingId,undefined);
   assert.deepEqual(errors,[]);await writeFile(f.output+'/report.json',JSON.stringify({product:f.product,draftId,transactionId:issued.transactionId,
    checks:['actual cancellation issue','lost response exact retry','saved financial readback','registered demo notice dispatcher','four durable consequence identities with delivered notice and two pending','reload retains issue and notice','cancellation transaction navigation','390px containment']},null,2));
  } else {
  await page.getByLabel('Takeover or abandonment reason',{exact:true}).fill('Fictional cancellation withdrawn after approval for browser verification');
  await page.getByLabel('I confirm this draft should be abandoned.',{exact:true}).check();await button('Abandon draft').click();await page.getByText('Abandoned',{exact:true}).waitFor();
  assert.ok((await read()).blockers.includes('servicing-draft-closed'));await page.getByRole('link',{name:'Back to policy',exact:true}).click();await page.waitForURL(f.webOrigin+`/policies/${f.policyId}`);
  const policy=await page.request.get(f.apiOrigin+`/api/v1/policies/${f.policyId}`);assert.equal(policy.status(),200);assert.equal((await policy.json()).versionId,f.originalVersionId);
  assert.deepEqual(errors,[]);await writeFile(f.output+'/report.json',JSON.stringify({completedAt:new Date().toISOString(),product:f.product,draftId,approved,
   checks:['actual Next.js and SQL API','policy cancellation action and amend navigation','editable reason and date change calculated preview','reviewed delivered notice','lost response exact retry','distinct senior approval','reload preserves approval','abandonment retains review history and policy navigation','cover unchanged and no cash paid','390px containment and signed amounts']},null,2));
  }
 } catch(error){await page.screenshot({path:f.output+'/failure.png'}).catch(()=>{});await writeFile(f.output+'/failure.txt',String(error)+'\n'+await page.locator('body').innerText().catch(()=>''));throw error;}
 finally{await page.unrouteAll({behavior:'wait'});await browser.close();}
}
