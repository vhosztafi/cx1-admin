import assert from 'node:assert/strict';
import {execFileSync} from 'node:child_process';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {dirname} from 'node:path';
import {chromium} from 'playwright';

const origin=process.env.COVER_FINANCE_WEB_ORIGIN??'http://127.0.0.1:3192';
assert.ok(['127.0.0.1','localhost'].includes(new URL(origin).hostname));
const earlier=JSON.parse(await readFile('.local/phase10-05-browser/browser-report.json','utf8'));
const finance=JSON.parse(await readFile('.local/phase10-12-browser/browser-report.json','utf8'));
const journey=JSON.parse(await readFile('.local/phase10-gap18-refund/journey.json','utf8'));
assert.equal(earlier.passed,true);assert.equal(finance.passed,true);
assert.equal(journey.passed,true,'A saved refund/payment journey is mandatory for restart readback.');
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const browser=await chromium.launch({channel:'chrome',headless:true});
const page=await browser.newPage();page.setDefaultTimeout(30000);
const checks=[];
async function get(path,binary=false){return page.evaluate(async({path,binary})=>{
 const response=await fetch(`/api/v1${path}`,{cache:'no-store'});
 if(!binary)return {status:response.status,body:await response.json()};
 const bytes=await response.arrayBuffer();
 const hash=await crypto.subtle.digest('SHA-256',bytes);
 return {status:response.status,sha256:Array.from(new Uint8Array(hash),x=>x.toString(16).padStart(2,'0')).join('').toUpperCase(),length:bytes.byteLength};
 },{path,binary});}
async function replay(path,body,key,etag){return page.evaluate(async({path,body,key,etag})=>{
 const token=(await(await fetch('/api/v1/auth/csrf',{cache:'no-store'})).json()).requestToken;
 const response=await fetch(`/api/v1${path}`,{method:'POST',cache:'no-store',headers:{
  'Content-Type':'application/json','X-CSRF-Token':token,'Idempotency-Key':key,...(etag?{'If-Match':etag}:{})},
  body:JSON.stringify(body)});return {status:response.status,body:await response.json()};
 },{path,body,key,etag});}
try{
 await page.goto(origin+'/login');
 await page.getByLabel('Email address').fill('finance@cover.example');
 await page.getByLabel('Password').fill(password);
 await page.getByRole('button',{name:'Sign in'}).click();
 await page.waitForURL(origin+'/');
 const account=await get(`/finance/accounts/${finance.agencyId}`);
 assert.equal(account.status,200);assert.equal(account.body.agencyId.toLowerCase(),finance.agencyId.toLowerCase());
 checks.push('saved agency account available after API restart');
 const ledger=await get(`/finance/ledger?agencyId=${finance.agencyId}&page=1&pageSize=100`);
 assert.equal(ledger.status,200);assert.ok(ledger.body.items.some(x=>x.transactionId===finance.transactionId));
 checks.push('posted transaction identity remains in scoped ledger');
 const policy=await get(`/policies/${finance.policyId}/finance`);
 assert.equal(policy.status,200);assert.ok(policy.body.items.some(x=>x.transactionId===finance.transactionId));
 checks.push('policy-scoped finance movement remains linked');
 const receipt=await get(`/finance/receipts/${earlier.receiptId}`);
 assert.equal(receipt.status,200);assert.equal(receipt.body.id.toLowerCase(),earlier.receiptId.toLowerCase());
 checks.push('saved receipt and allocation history remain addressable');
 const statement=await get(`/finance/statements/${earlier.statementId}`);
 assert.equal(statement.status,200);assert.equal(statement.body.id.toLowerCase(),earlier.statementId.toLowerCase());
 const statementFile=await get(`/finance/statements/${earlier.statementId}/download`,true);
 assert.equal(statementFile.status,200);assert.ok(statementFile.length>0);
 assert.equal(statementFile.sha256,statement.body.contentHash.toUpperCase());
 checks.push('retained statement exact bytes match saved SHA-256');
 const current=await get(`/finance/bordereaux/${finance.batchId}`);
 assert.equal(current.status,200);assert.ok(current.body.parentVersionId);
 const submitted=await get(`/finance/bordereaux/${finance.batchId}?versionId=${current.body.parentVersionId}`);
 assert.equal(submitted.status,200);assert.equal(submitted.body.state,'valid');
 const submission=await get(`/finance/bordereaux/${finance.batchId}/versions/${submitted.body.id}/submission`);
 assert.equal(submission.status,200);assert.equal(submission.body.state,'submitted');
 const csv=await get(`/finance/bordereaux/${finance.batchId}/versions/${submitted.body.id}/download`,true);
 assert.equal(csv.status,200);assert.ok(csv.length>0);assert.equal(csv.sha256,submitted.body.contentHash.toUpperCase());
 checks.push('submitted historical bordereau and exact CSV survive API restart');
 const period=await get(`/finance/periods/${finance.periodId}`);
 assert.equal(period.status,200);
 checks.push('period review remains available with saved identity');
 const earning=await get(`/finance/agencies/${finance.agencyId}/earned-premium?periodId=${finance.periodId}`);
 assert.equal(earning.status,200);assert.equal(earning.body.periodId,finance.periodId);
 assert.ok(earning.body.items.length>0);
 checks.push('source-pinned earned premium remains reconciled after API restart');
 const refund=await get(`/finance/refunds/${journey.refundId}`);
 assert.equal(refund.status,200);assert.equal(refund.body.id,journey.refundId);
 assert.equal(refund.body.state,'approved');assert.equal(refund.body.creditObligationId,journey.input.creditObligationId);
 assert.ok(refund.body.decisions.some(item=>item.actorId!==refund.body.requestedBy));
 const payment=await get(`/finance/payments/${journey.paymentId}`);
 assert.equal(payment.status,200);assert.equal(payment.body.id,journey.paymentId);
 assert.equal(payment.body.refundRequestId,journey.refundId);assert.equal(payment.body.state,'paid');
 assert.equal(payment.body.providerOperationId,journey.payment.providerOperationId);
 const pending=await get(`/finance/agencies/${journey.input.agencyId}/refunds?state=pending&page=1&pageSize=50`);
 assert.equal(pending.status,200);assert.equal(pending.body.total,journey.finalPendingCount);
 checks.push('exact approved refund, independent decision, terminal payment and pending count survive restart');
 const requestReplay=await replay(`/finance/credits/${journey.input.creditObligationId}/refunds`,{
  amount:journey.input.refundAmount,sources:[{allocationId:journey.allocationId,amount:journey.input.refundAmount}],
  reason:'Return collected fictional cancellation premium'},journey.keys.request);
 assert.equal(requestReplay.status,201,JSON.stringify(requestReplay));assert.equal(requestReplay.body.id,journey.refundId);
 const paymentReplay=await replay(`/finance/refunds/${journey.refundId}/payments`,{
  reason:'Release independently approved fictional refund'},journey.keys.queue,journey.approvedEtag);
 assert.equal(paymentReplay.status,202,JSON.stringify(paymentReplay));assert.equal(paymentReplay.body.paymentId,journey.paymentId);
 const sql=JSON.parse(execFileSync('pwsh',['-NoProfile','-File','scripts/verify-finance-refund-sql.ps1'],
  {encoding:'utf8',windowsHide:true}));
 assert.equal(sql.passed,true);assert.equal(sql.refundId,journey.refundId);
 assert.equal(sql.paymentId,journey.paymentId);assert.equal(sql.providerOperationId,journey.payment.providerOperationId);
 assert.equal(sql.providerOperationCount,1);assert.equal(sql.postingCount,1);
 checks.push('same-key replay retains one provider operation and one exact negative-cash posting');
 const output=process.env.COVER_FINANCE_RESTART_EVIDENCE??'.local/phase10-gap-final/restart-readback.json';
 await mkdir(dirname(output),{recursive:true});
 await writeFile(output,JSON.stringify({passed:true,checkedAt:new Date().toISOString(),checks,
  agencyId:finance.agencyId,policyId:finance.policyId,receiptId:earlier.receiptId,
  statementId:earlier.statementId,batchId:finance.batchId,submissionId:submission.body.id,
  statementSha256:statementFile.sha256,csvSha256:csv.sha256,
  earnedPremium:earning.body.earnedPremium,earningPeriodId:finance.periodId,
  refundId:journey.refundId,paymentId:journey.paymentId,decisionId:sql.decisionId,
  providerOperationId:sql.providerOperationId,postingId:sql.postingId,workId:sql.workId,
  requestHash:sql.requestHash,sql},null,2));
 console.log(`Passed ${checks.length} saved finance restart readbacks.`);
}finally{await browser.close();}
