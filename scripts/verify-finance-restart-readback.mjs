import assert from 'node:assert/strict';
import {readFile,writeFile} from 'node:fs/promises';
import {chromium} from 'playwright';

const origin=process.env.COVER_FINANCE_WEB_ORIGIN??'http://127.0.0.1:3192';
assert.equal(new URL(origin).hostname,'127.0.0.1');
const earlier=JSON.parse(await readFile('.local/phase10-05-browser/browser-report.json','utf8'));
const finance=JSON.parse(await readFile('.local/phase10-12-browser/browser-report.json','utf8'));
assert.equal(earlier.passed,true);assert.equal(finance.passed,true);
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
 await writeFile('.local/phase10-final/restart-readback.json',JSON.stringify({passed:true,checkedAt:new Date().toISOString(),checks,agencyId:finance.agencyId,policyId:finance.policyId,receiptId:earlier.receiptId,statementId:earlier.statementId,batchId:finance.batchId,submissionId:submission.body.id,statementSha256:statementFile.sha256,csvSha256:csv.sha256},null,2));
 console.log(`Passed ${checks.length} saved finance restart readbacks.`);
}finally{await browser.close();}
