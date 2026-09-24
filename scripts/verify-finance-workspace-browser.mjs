import assert from 'node:assert/strict';
import {createHash} from 'node:crypto';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {chromium} from 'playwright';

const webOrigin=process.env.COVER_FINANCE_WEB_ORIGIN??'http://127.0.0.1:3192';
assert.equal(new URL(webOrigin).hostname,'127.0.0.1');
const output='.local/phase10-12-browser';await mkdir(output,{recursive:true});
const fixture=JSON.parse(await readFile('.local/phase10-05-browser/fixture.json','utf8'));
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const providerId='1099f7a5-abdd-422f-8692-9c795e3f6d74';
const browser=await chromium.launch({channel:'chrome',headless:true});
const page=await browser.newPage({viewport:{width:1440,height:960}});page.setDefaultTimeout(45000);
const checks=[],errors=[];page.on('pageerror',error=>errors.push(error.message));
const button=name=>page.getByRole('button',{name,exact:true});
const field=name=>page.getByLabel(name,{exact:true});
async function api(path,method='GET',body,etag){return page.evaluate(async({path,method,body,etag})=>{
 const headers={};if(method!=='GET'){headers['X-CSRF-Token']=(await(await fetch('/api/v1/auth/csrf',{cache:'no-store'})).json()).requestToken;
  headers['Idempotency-Key']=crypto.randomUUID();if(etag)headers['If-Match']=etag;
  if(body!==undefined)headers['Content-Type']='application/json';}
 const response=await fetch(`/api/v1${path}`,{method,cache:'no-store',headers,...(body!==undefined?{body:JSON.stringify(body)}:{})});
 let data=null;try{data=await response.json();}catch{}
 return {status:response.status,body:data,etag:response.headers.get('ETag')};
 },{path,method,body,etag});}
const goto=async(tab,extras={})=>{const query=new URLSearchParams({tab,agencyId:fixture.agencyId,...extras});
 await page.goto(`${webOrigin}/accounting?${query}`);};
try{
 await page.goto(webOrigin+'/login');await field('Email address').fill('finance@cover.example');
 await field('Password').fill(password);await button('Sign in').click();await page.waitForURL(webOrigin+'/');
 await goto('overview');await page.getByRole('heading',{name:'Accounting overview'}).waitFor();
 const tabLabels=['Overview','Transactions','Broker accounts','Payments','Reconciliation','Bordereaux','Refunds'];
 for(const label of tabLabels)assert.equal(await page.getByRole('link',{name:label,exact:true}).count(),1);
 checks.push('seven accounting source tabs on live scoped overview');
 const ledger=await api(`/finance/ledger?agencyId=${fixture.agencyId}&page=1&pageSize=50`);
 assert.equal(ledger.status,200);assert.ok(ledger.body.items.length>0);
 const insurance=ledger.body.items.find(row=>row.sourceKind==='insurance'&&row.transactionId&&row.policyId);
 assert.ok(insurance);await page.getByText('Written premium',{exact:true}).waitFor();
 checks.push('overview money reads saved agency ledger and period');
 await goto('transactions',{transactionId:insurance.transactionId});
 await page.getByRole('heading',{name:'Selected transaction'}).waitFor();
 assert.ok(page.url().includes(`transactionId=${insurance.transactionId}`));
 await page.reload();await page.getByRole('heading',{name:'Selected transaction'}).waitFor();
 checks.push('selected posted transaction and policy identity survive reload');
 const servicing=await browser.newPage({viewport:{width:1440,height:960}});servicing.setDefaultTimeout(45000);
 await servicing.goto(webOrigin+'/login');await servicing.getByLabel('Email address').fill('servicing@cover.example');
 await servicing.getByLabel('Password').fill(password);await servicing.getByRole('button',{name:'Sign in'}).click();
 await servicing.waitForURL(webOrigin+'/');await servicing.goto(`${webOrigin}/policies/${insurance.policyId}`);
 await servicing.getByRole('heading',{name:'Policy finance',exact:true}).waitFor();
 const policyFinance=await servicing.evaluate(async id=>{const response=await fetch(`/api/v1/policies/${id}/finance`);return {status:response.status,body:await response.json()};},insurance.policyId);
 assert.ok(policyFinance.body.items.some(row=>row.transactionId===insurance.transactionId));
 const scopedAccount=await servicing.getByRole('link',{name:'Open broker account'}).getAttribute('href');
 assert.equal(new URL(scopedAccount,webOrigin).searchParams.get('agencyId'),fixture.agencyId);
 await servicing.close();
 checks.push('policy finance and broker account link use actual policy and agency scope');
 await page.goto(`${webOrigin}/policies/${insurance.policyId}`);
 await page.getByRole('heading',{name:'Policy finance',exact:true}).waitFor();
 await page.getByRole('link',{name:'Open broker account'}).waitFor();
 checks.push('finance actor opens policy-scoped finance without full policy servicing details');
 await goto('accounts');await page.getByRole('heading',{name:'Broker account',exact:true}).waitFor();
 const account=await api(`/finance/accounts/${fixture.agencyId}`);assert.equal(account.status,200);
 assert.equal(account.body.agencyId.toLowerCase(),fixture.agencyId);checks.push('agency account reads saved scoped balance');
 await goto('payments');await page.getByRole('heading',{name:'Receipts',exact:true}).waitFor();
 const receipts=await api(`/finance/agencies/${fixture.agencyId}/receipts?page=1&pageSize=50`);
 assert.equal(receipts.status,200);assert.ok(receipts.body.items.length>0);
 await goto('payments',{receiptId:receipts.body.items[0].id});
 await page.getByRole('heading',{name:'Receipt detail',exact:true}).waitFor();
 checks.push('payment tab opens selected saved receipt');
 await goto('reconciliation');await page.getByRole('heading',{name:'Reconciliation',exact:true}).waitFor();
 const bank=await api(`/finance/agencies/${fixture.agencyId}/bank-lines?page=1&pageSize=50`);
 assert.equal(bank.status,200);checks.push('reconciliation reads bank lines and exposes match/explain workflow');
 await goto('bordereaux');await page.getByRole('heading',{name:'Insurer bordereaux'}).waitFor();
 await field('Provider ID').fill(providerId);await page.getByRole('button',{name:'Generate saved batch'}).waitFor();
 const periods=await api('/finance/periods');assert.equal(periods.status,200);
 const period=periods.body.find(row=>row.from<=insurance.postingDate&&row.to>insurance.postingDate);
 assert.ok(period);await page.getByRole('combobox',{name:'Accounting period'}).selectOption(period.id);
 await button('Generate saved batch').click();await page.waitForURL(/batchId=[a-f0-9-]+/i);
 const batchId=new URL(page.url()).searchParams.get('batchId');assert.ok(batchId);
 let batch=await api(`/finance/bordereaux/${batchId}`);assert.equal(batch.status,200);
 assert.equal(batch.body.providerId.toLowerCase(),providerId);assert.ok(batch.body.members.length>0);
 const broken=batch.body.members.slice(0,3);assert.equal(broken.length,3);
 for(const member of broken){const changed=await api(`/finance/bordereaux/${batchId}/members/${member.sourceJournalId}/corrections`,
  'POST',{policyReference:'',reason:'Fictional browser invalid mapping evidence'},`"${batch.body.id.replaceAll('-','')}"`);
  assert.equal(changed.status,201,JSON.stringify(changed));batch=await api(`/finance/bordereaux/${batchId}`);}
 await button('Refresh saved batches').click();await page.getByText(`Version ${batch.body.number}`,{exact:false}).first().waitFor();
 const validation=await api(`/finance/bordereaux/${batchId}/validations`,'POST',undefined,
  `"${batch.body.id.replaceAll('-','')}"`);
 assert.equal(validation.status,201,JSON.stringify(validation));
 batch=await api(`/finance/bordereaux/${batchId}`);
 await button('Refresh saved batches').click();
 await page.getByText(`Version ${batch.body.number}`,{exact:false}).first().waitFor();
 checks.push('batch generation saves exact provider, period, version and member identities');
 const failing=batch.body.members.filter(row=>batch.body.validation.some(issue=>issue.sourceJournalId?.toLowerCase()===row.sourceJournalId.toLowerCase()));
 assert.ok(failing.length>=3,JSON.stringify({state:batch.body.state,issues:batch.body.validation.slice(0,4),members:broken.map(row=>row.sourceJournalId)}));
 for(const member of failing.slice(0,3)){
  const section=page.locator(`[data-source-journal-id="${member.sourceJournalId}"]`);
  await section.getByRole('button',{name:'Correct mapping'}).waitFor();
  await section.getByRole('button',{name:'Exclude row'}).waitFor();
 }
 checks.push('three distinct invalid saved members expose their own correction and exclusion actions');
 const second=page.locator(`[data-source-journal-id="${broken[1].sourceJournalId}"]`);
 await second.getByLabel('Policy reference').fill(broken[1].policyReference);
 await second.getByLabel('Reason').fill('Fictional correction of second failed member');
 const correctionRequests=[];let dropped=false;
 await page.route(`**/api/v1/finance/bordereaux/${batchId}/members/${broken[1].sourceJournalId}/corrections`,async route=>{
  if(route.request().method()!=='POST')return route.continue();
  correctionRequests.push({body:route.request().postData(),key:route.request().headers()['idempotency-key'],etag:route.request().headers()['if-match']});
  if(!dropped){dropped=true;const saved=await route.fetch();assert.equal(saved.status(),201);await route.abort('failed');}
  else await route.continue();
 });
 await second.getByRole('button',{name:'Correct mapping'}).click();
 await button('Retry same action').waitFor();
 assert.equal(await second.getByLabel('Policy reference').inputValue(),broken[1].policyReference);
 await button('Retry same action').click();
 await page.waitForFunction(async({id,source})=>{const response=await fetch(`/api/v1/finance/bordereaux/${id}`);
  const version=await response.json();return version.members.find(row=>row.sourceJournalId===source)?.policyReference!=='';},
  {id:batchId,source:broken[1].sourceJournalId});
 await page.getByText(`Correction for ${broken[1].sourceJournalId} saved.`,{exact:false}).waitFor();
 assert.equal(correctionRequests.length,2);assert.deepEqual(correctionRequests[0],correctionRequests[1]);
 checks.push('second failed member mapping correction retries same body/key/ETag once after lost response');
 await page.unroute(`**/api/v1/finance/bordereaux/${batchId}/members/${broken[1].sourceJournalId}/corrections`);
 const third=page.locator(`[data-source-journal-id="${broken[2].sourceJournalId}"]`);
 await third.getByLabel('Reason').fill('Fictional exclusion of third failed member');
 await third.getByRole('button',{name:'Exclude row'}).click();
 await page.waitForFunction(async({id,source})=>{const response=await fetch(`/api/v1/finance/bordereaux/${id}`);
  const version=await response.json();return Boolean(version.members.find(row=>row.sourceJournalId===source)?.exclusionReason);},
  {id:batchId,source:broken[2].sourceJournalId});
 await page.getByText(`Exclusion for ${broken[2].sourceJournalId} saved.`,{exact:false}).waitFor();
 checks.push('third failed member exclusion saved with reason as linked successor');
 const beforeRevalidation=await api(`/finance/bordereaux/${batchId}`);
 await button('Re-run validation').click();
 await page.waitForFunction(async({id,previous})=>{const response=await fetch(`/api/v1/finance/bordereaux/${id}`);
  if(!response.ok)return false;const version=await response.json();
  return version.id!==previous&&version.state==='invalid'&&version.validation.length===1;},
  {id:batchId,previous:beforeRevalidation.body.id});
 checks.push('revalidation persists only the remaining invalid member after two row actions');
 await page.getByText('Validation saved.',{exact:false}).waitFor();
 assert.equal(await button('Submit exact valid version').count(),0);
 const first=page.locator(`[data-source-journal-id="${broken[0].sourceJournalId}"]`);
 await first.getByLabel('Policy reference').fill(broken[0].policyReference);
 await first.getByLabel('Reason').fill('Fictional correction of first remaining failure');
 await first.getByRole('button',{name:'Correct mapping'}).click();
 await page.getByText(`Correction for ${broken[0].sourceJournalId} saved.`,{exact:false}).waitFor();
 const beforeValid=await api(`/finance/bordereaux/${batchId}`);
 await button('Re-run validation').click();
 await page.waitForFunction(async({id,previous})=>{const response=await fetch(`/api/v1/finance/bordereaux/${id}`);
  if(!response.ok)return false;const version=await response.json();return version.id!==previous&&version.state==='valid';},
  {id:batchId,previous:beforeValid.body.id});
 await button('Export CSV').waitFor();
 const valid=await api(`/finance/bordereaux/${batchId}`);assert.equal(valid.body.state,'valid');
 const [download]=await Promise.all([page.waitForEvent('download'),button('Export CSV').click()]);
 const bytes=await readFile(await download.path());
 assert.equal(createHash('sha256').update(bytes).digest('hex').toUpperCase(),valid.body.contentHash);
 checks.push('invalid version cannot submit; corrected valid version downloads exact hashed CSV bytes');
 await button('Submit exact valid version').click();
 await page.getByText('Submission saved.',{exact:false}).waitFor();
 const submitted=await api(`/finance/bordereaux/${batchId}/versions/${valid.body.id}/submission`);
 assert.equal(submitted.status,200,JSON.stringify(submitted));
 assert.equal(submitted.body.versionId.toLowerCase(),valid.body.id.toLowerCase());
 checks.push('exact validated batch version has one saved submission record');
 let applied;
 for(let attempt=0;attempt<60;attempt++){
  applied=await api(`/finance/bordereaux/${batchId}/versions/${valid.body.id}/submission`);
  if(applied.status===200&&applied.body.state==='submitted')break;
  await new Promise(resolve=>setTimeout(resolve,500));
 }
 assert.equal(applied?.body?.state,'submitted',JSON.stringify(applied));
 const successorMember=page.locator(`[data-source-journal-id="${broken[0].sourceJournalId}"]`);
 await successorMember.getByLabel('Policy reference').fill(`${broken[0].policyReference}-REVIEW`);
 await successorMember.getByLabel('Reason').fill('Fictional successor after insurer submission');
 await successorMember.getByRole('button',{name:'Correct mapping'}).click();
 await page.getByText(`Correction for ${broken[0].sourceJournalId} saved.`,{exact:false}).waitFor();
 const newer=await api(`/finance/bordereaux/${batchId}`);assert.notEqual(newer.body.id,valid.body.id);
 const [historical]=await Promise.all([page.waitForEvent('download'),button('Download exact saved CSV').click()]);
 assert.equal(createHash('sha256').update(await readFile(await historical.path())).digest('hex').toUpperCase(),valid.body.contentHash);
 checks.push('submitted historical version remains downloadable byte-for-byte after linked successor');
 await goto('refunds');await page.getByRole('heading',{name:'Refunds',exact:true}).waitFor();
 assert.ok(await field('Credit obligation ID').count());checks.push('refund request, approval and payment entry use saved identities');
 const admin=await browser.newPage();admin.setDefaultTimeout(45000);await admin.goto(webOrigin+'/login');
 await admin.getByLabel('Email address').fill('system-admin@cover.example');await admin.getByLabel('Password').fill(password);
 await admin.getByRole('button',{name:'Sign in'}).click();await admin.waitForURL(webOrigin+'/');
 await admin.goto(`${webOrigin}/admin?tab=overview`);await admin.getByRole('link',{name:'Open bordereaux'}).waitFor();await admin.close();
 checks.push('admin inherited bordereaux navigation is bound');
 await page.goto(`${webOrigin}/agents/${fixture.agencyId}?tab=Accounts`);
 await page.getByRole('link',{name:'Open in Accounting'}).waitFor();
 assert.equal(new URL(await page.getByRole('link',{name:'Open in Accounting'}).getAttribute('href'),webOrigin).searchParams.get('agencyId'),fixture.agencyId);
 checks.push('agency inherited accounting link preserves selected agency');
 await goto('overview');await page.setViewportSize({width:390,height:844});await page.screenshot({path:`${output}/overview-mobile.png`,fullPage:true});
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 await page.setViewportSize({width:1440,height:960});await page.evaluate(()=>document.body.style.zoom='200%');
 await page.screenshot({path:`${output}/overview-zoom200.png`,fullPage:true});
 await page.evaluate(()=>document.body.style.zoom='');await field('Agency ID').focus();await page.keyboard.press('Tab');
 assert.equal(await button('Open agency account').evaluate(element=>document.activeElement===element),true);
 await page.screenshot({path:`${output}/overview-desktop.png`,fullPage:true});
 checks.push('mobile, 200% zoom and keyboard focus inspected');
 await goto('accounts',{agencyId:'00000000-0000-0000-0000-000000000001'});
 await page.getByRole('alert').first().waitFor();checks.push('foreign agency selection does not reveal saved account');
 assert.deepEqual(errors,[]);
 await writeFile(`${output}/browser-report.json`,JSON.stringify({passed:true,checks,batchId,periodId:period.id,agencyId:fixture.agencyId,policyId:insurance.policyId,transactionId:insurance.transactionId,errors,checkedAt:new Date().toISOString()},null,2));
 console.log(`Passed ${checks.length} live finance workspace browser checks. Batch ${batchId}.`);
}catch(cause){await page.screenshot({path:`${output}/failure.png`,fullPage:true}).catch(()=>{});
 await writeFile(`${output}/browser-report.json`,JSON.stringify({passed:false,checks,error:String(cause),errors},null,2));throw cause;
}finally{await browser.close();}
