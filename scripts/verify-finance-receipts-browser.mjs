import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {chromium} from 'playwright';

const webOrigin=process.env.COVER_FINANCE_WEB_ORIGIN??'http://127.0.0.1:3185';
assert.equal(new URL(webOrigin).hostname,'127.0.0.1');
const output='.local/phase10-05-browser';await mkdir(output,{recursive:true});
const fixture=JSON.parse(await readFile(`${output}/fixture.json`,'utf8'));
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const browser=await chromium.launch({channel:'chrome',headless:true});
const page=await browser.newPage({viewport:{width:1440,height:960}});page.setDefaultTimeout(45000);
const cases=[],errors=[];page.on('pageerror',error=>errors.push(error.message));
const button=name=>page.getByRole('button',{name,exact:true});
const field=name=>page.getByLabel(name,{exact:true});
async function api(path,method='GET',body,etag){return page.evaluate(async({path,method,body,etag})=>{
 const headers={};if(method!=='GET'){headers['Content-Type']='application/json';
  headers['X-CSRF-Token']=(await(await fetch('/api/v1/auth/csrf',{cache:'no-store'})).json()).requestToken;
  headers['Idempotency-Key']=crypto.randomUUID();if(etag)headers['If-Match']=etag;}
 const response=await fetch('/api/v1'+path,{method,cache:'no-store',headers,...(body?{body:JSON.stringify(body)}:{})});
 return {status:response.status,body:await response.json(),etag:response.headers.get('ETag')};
 },{path,method,body,etag});}
async function receipt(id){const result=await api(`/finance/receipts/${id}`);assert.equal(result.status,200);return result.body;}
try{
 await page.goto(webOrigin+'/login');await field('Email address').fill('finance@cover.example');
 await field('Password').fill(password);await button('Sign in').click();await page.waitForURL(webOrigin+'/');
 await page.goto(`${webOrigin}/accounting?tab=payments&agencyId=${fixture.agencyId}`);
 await page.getByRole('heading',{name:'Receipts',exact:true}).waitFor();
 await field('Amount received (GBP)').fill('25.00');await field('Received on').fill(fixture.receivedOn);
 const reference=`FICTIONAL-BROWSER-${Date.now()}`;await field('Bank reference').fill(reference);
 await button('Record receipt').click();await page.waitForURL(/receiptId=[a-f0-9-]+/i);
 const receiptId=new URL(page.url()).searchParams.get('receiptId');assert.ok(receiptId);
 let saved=await receipt(receiptId);assert.equal(saved.bankReference,reference);assert.equal(saved.amount,'25.00');
 assert.equal(saved.residual,'25.00');assert.equal(saved.payerKind,'unidentified');cases.push('recorded exact saved receipt and cash residual');
 await page.reload();await page.getByRole('heading',{name:'Receipt detail',exact:true}).waitFor();
 assert.equal(await field('Bank reference').inputValue(),''); // The create form resets after confirmed save.
 assert.equal((await receipt(receiptId)).residual,'25.00');cases.push('receipt identity and amount survive reload');
 await field('Payer type').selectOption('agency');await field('Reason for payer assignment').fill('Fictional payer identified from bank reference');
 await button('Assign payer').click();await page.waitForFunction(async id=>{
  const response=await fetch('/api/v1/finance/receipts/'+id,{cache:'no-store'});return (await response.json()).payerKind==='agency';},receiptId);
 saved=await receipt(receiptId);assert.equal(saved.payerId,fixture.agencyId);const firstAssignment=saved.assignmentId;
 cases.push('payer assignment saved with new exact assignment ID');
 await page.waitForFunction(()=>document.querySelectorAll('#receipt-invoice-candidate option:not([value=""])').length>0);
 const transactionId=await field('Invoice candidate').locator('option:not([value=""])').first().getAttribute('value');assert.ok(transactionId);
 await field('Invoice candidate').selectOption(transactionId);await field('Amount to apply (GBP)').fill('10.00');
 const reassigned=await api(`/finance/receipts/${receiptId}/payer`,'POST',
  {payerKind:'agency',payerId:fixture.agencyId,reason:'Fictional concurrent payer reconfirmation'},
  `"${firstAssignment.replaceAll('-','')}"`);
 assert.equal(reassigned.status,201);assert.notEqual(reassigned.body.assignmentId,firstAssignment);
 await button('Allocate amount').click();await button('Review saved version').waitFor();
 assert.equal(await field('Amount to apply (GBP)').inputValue(),'10.00');
 assert.equal(await field('Invoice candidate').inputValue(),transactionId);
 assert.equal((await receipt(receiptId)).residual,'25.00');cases.push('stale assignment conflict keeps invoice and typed amount');
 await button('Review saved version').click();await page.getByText(reassigned.body.assignmentId,{exact:false}).waitFor();
 let dropped=false;const requests=[];
 await page.route(`**/api/v1/finance/receipts/${receiptId}/allocations`,async route=>{
  if(route.request().method()!=='POST')return route.continue();
  requests.push({body:route.request().postData(),key:route.request().headers()['idempotency-key'],etag:route.request().headers()['if-match']});
  if(!dropped){dropped=true;const response=await route.fetch();assert.equal(response.status(),201);await route.abort('failed');}
  else await route.continue();
 });
 await button('Allocate amount').click();await button('Retry same action').waitFor();
 assert.equal(await field('Amount to apply (GBP)').inputValue(),'10.00');
 await button('Retry same action').click();await page.waitForFunction(async id=>{
  const response=await fetch('/api/v1/finance/receipts/'+id,{cache:'no-store'});return (await response.json()).residual==='15.00';},receiptId);
 assert.equal(requests.length,2);assert.deepEqual(requests[0],requests[1]);
 saved=await receipt(receiptId);assert.equal(saved.allocations.filter(x=>!x.reversalOfId).length,1);
 assert.equal(saved.residual,'15.00');const allocationId=saved.allocations.find(x=>!x.reversalOfId).id;
 cases.push('lost allocation response retries identical body/key/ETag and applies once');
 await page.reload();await page.getByRole('heading',{name:'Receipt detail',exact:true}).waitFor();
 assert.equal((await receipt(receiptId)).residual,'15.00');cases.push('allocated residual survives reload');
 await button('Reverse allocation').click();await field('Reason for reversal').fill('Fictional browser verification of append-only reversal');
 await button('Confirm reversal').click();await page.waitForFunction(async({receiptId,allocationId})=>{
  const response=await fetch('/api/v1/finance/receipts/'+receiptId,{cache:'no-store'});
  const saved=await response.json();return saved.residual==='25.00'&&saved.allocations.some(x=>
   x.reversalOfId?.toLowerCase()===allocationId.toLowerCase());},{receiptId,allocationId});
 for(let attempt=0;attempt<20;attempt++){
  saved=await receipt(receiptId);
  if(saved.allocations.some(x=>x.reversalOfId?.toLowerCase()===allocationId.toLowerCase()))break;
  await new Promise(resolve=>setTimeout(resolve,100));
 }
 assert.equal(saved.allocations.filter(x=>x.reversalOfId?.toLowerCase()===allocationId.toLowerCase()).length,1,
  JSON.stringify({allocationId,allocations:saved.allocations}));
 assert.equal(saved.residual,'25.00');await page.reload();assert.equal((await receipt(receiptId)).residual,'25.00');
 cases.push('reasoned reversal is linked and restores residual after reload');
 await page.goto(`${webOrigin}/accounting?tab=accounts&agencyId=${fixture.agencyId}`);
 await page.getByRole('heading',{name:'Broker account',exact:true}).waitFor();
 const summary=await api(`/finance/accounts/${fixture.agencyId}`);assert.equal(summary.status,200);
 await page.getByText('Posting basis',{exact:false}).waitFor();cases.push('agency account displays scoped saved ledger basis');
 await field('From').fill(fixture.statementFrom);await field('To (exclusive)').fill(fixture.statementTo);
 await button('Generate statement').click();await page.waitForURL(/statementId=[a-f0-9-]+/i);
 const statementId=new URL(page.url()).searchParams.get('statementId');assert.ok(statementId);
 const statement=await api(`/finance/statements/${statementId}`);assert.equal(statement.status,200);
 const pence=x=>{const [whole,fraction]=x.split('.');return BigInt(whole)*BigInt(100)+BigInt(whole.startsWith('-')?'-'+fraction:fraction);};
 assert.equal(pence(statement.body.opening)+pence(statement.body.debits)-pence(statement.body.credits),pence(statement.body.closing));
 await page.reload();await page.getByRole('heading',{name:/Statement v/}).waitFor();
 await button('Download statement').click();cases.push('saved statement ID, equation, reload and exact download');
 await page.setViewportSize({width:390,height:844});await page.screenshot({path:`${output}/accounts-mobile.png`,fullPage:true});
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 await page.goto(`${webOrigin}/accounting?tab=payments&agencyId=${fixture.agencyId}&receiptId=${receiptId}`);
 await page.getByRole('heading',{name:'Receipt detail',exact:true}).waitFor();
 assert.equal(await page.evaluate(()=>document.documentElement.scrollWidth<=innerWidth),true);
 assert.equal(await page.getByRole('region',{name:'Saved receipts'}).getAttribute('tabindex'),'0');
 await field('Agency ID').focus();await page.keyboard.press('Tab');assert.equal(await button('Open agency account').evaluate(el=>document.activeElement===el),true);
 await page.screenshot({path:`${output}/receipts-mobile.png`,fullPage:true});
 cases.push('390px pages fit viewport; table region and agency picker support keyboard');
 await page.setViewportSize({width:1440,height:960});await page.screenshot({path:`${output}/receipts-desktop.png`,fullPage:true});
 assert.deepEqual(errors,[]);
 await writeFile(`${output}/browser-report.json`,JSON.stringify({passed:true,agencyId:fixture.agencyId,
  receiptId,allocationId,statementId,cases,errors,checkedAt:new Date().toISOString()},null,2));
 console.log(`Passed ${cases.length} live finance browser checks. Receipt ${receiptId}, statement ${statementId}.`);
}catch(error){await page.screenshot({path:`${output}/failure.png`,fullPage:true}).catch(()=>{});
 await writeFile(`${output}/browser-report.json`,JSON.stringify({passed:false,cases,errors,error:String(error)},null,2));throw error;
}finally{await browser.close();}
