import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {chromium} from 'playwright';

const origin=process.env.COVER_FINANCE_WEB_ORIGIN??'http://localhost:3193';
assert.ok(['localhost','127.0.0.1'].includes(new URL(origin).hostname));
const input=JSON.parse(await readFile('.local/phase10-gap18-refund-input.json','utf8'));
const password=(await readFile('.local/demo-password.txt','utf8')).trim();
const output='.local/phase10-gap18-refund';await mkdir(output,{recursive:true});
const statePath=`${output}/journey.json`;
let state;try{state=JSON.parse(await readFile(statePath,'utf8'));}catch{state={
 input,keys:{receipt:randomUUID(),allocation:randomUUID(),request:randomUUID(),decision:randomUUID(),queue:randomUUID()},
 originId:randomUUID(),startedAt:new Date().toISOString()};}
assert.deepEqual(state.input,input);
const save=async()=>writeFile(statePath,JSON.stringify(state,null,2));await save();
const browser=await chromium.launch({channel:'chrome',headless:true});
async function login(email){const page=await browser.newPage();page.setDefaultTimeout(45000);
 await page.goto(`${origin}/login`);await page.getByLabel('Email address',{exact:true}).fill(email);
 await page.getByLabel('Password',{exact:true}).fill(password);
 await page.getByRole('button',{name:'Sign in',exact:true}).click();await page.waitForURL(`${origin}/`);return page;}
async function api(page,path,method='GET',body,options={}){return page.evaluate(async({path,method,body,options})=>{
 const headers={};if(method!=='GET'){
  headers['X-CSRF-Token']=(await(await fetch('/api/v1/auth/csrf',{cache:'no-store'})).json()).requestToken;
  headers['Idempotency-Key']=options.key;
  if(options.etag)headers['If-Match']=options.etag;
  if(body!==undefined)headers['Content-Type']='application/json';}
 const response=await fetch(`/api/v1${path}`,{method,headers,cache:'no-store',
  ...(body===undefined?{}:{body:JSON.stringify(body)})});
 let result;try{result=await response.json();}catch{result=null;}
 return {status:response.status,body:result,etag:response.headers.get('ETag'),location:response.headers.get('Location')};
 },{path,method,body,options});}
function saved(response,status){assert.equal(response.status,status,JSON.stringify(response));return response.body;}
let finance,reviewer;
try{
 finance=await login('finance@cover.example');
 const receiptBody={amount:input.invoiceAmount,currency:'GBP',receivedOn:new Date().toISOString().slice(0,10),
  bankReference:'Fictional collected cancellation source',originKind:'manual',originId:state.originId,
  payerKind:'agency',payerId:input.agencyId};
 if(!state.receiptId){const result=await api(finance,`/finance/agencies/${input.agencyId}/receipts`,'POST',receiptBody,{key:state.keys.receipt});
  state.receiptId=saved(result,201).id;await save();}
 const receipt=saved(await api(finance,`/finance/receipts/${state.receiptId}`),200);
 assert.equal(receipt.agencyId.toLowerCase(),input.agencyId);
 if(!state.allocationId){const result=await api(finance,`/finance/receipts/${state.receiptId}/allocations`,'POST',
  {items:[{invoiceId:input.invoiceId,amount:input.invoiceAmount}]},
  {key:state.keys.allocation,etag:`"${receipt.assignmentId.replaceAll('-','')}"`});
  const value=saved(result,201);state.allocationId=value.allocationIds?.[0]??value.allocationId??value.id;await save();}
 assert.ok(state.allocationId);
 const requestBody={amount:input.refundAmount,sources:[{allocationId:state.allocationId,amount:input.refundAmount}],
  reason:'Return collected fictional cancellation premium'};
 if(!state.refundId){const result=await api(finance,`/finance/credits/${input.creditObligationId}/refunds`,'POST',requestBody,
  {key:state.keys.request});state.refundId=saved(result,201).id;await save();}
 const pending=saved(await api(finance,`/finance/refunds/${state.refundId}`),200);
 assert.equal(pending.creditObligationId.toLowerCase(),input.creditObligationId);
 if(!state.decisionId){assert.equal(pending.state,'pending');
  const pendingPage=saved(await api(finance,`/finance/agencies/${input.agencyId}/refunds?state=pending&page=1&pageSize=50`),200);
  assert.ok(pendingPage.items.some(item=>item.id===state.refundId));
  await finance.goto(`${origin}/accounting?tab=overview&agencyId=${input.agencyId}`);
  const attention=finance.getByRole('row',{name:/Refund approval/});
  await attention.getByText(`${pendingPage.total} pending refund${pendingPage.total===1?'':'s'}`,{exact:true}).waitFor();
  const direct=attention.getByRole('link',{name:'Open first saved refund'});
  assert.equal(new URL(await direct.getAttribute('href'),origin).searchParams.get('refundId'),state.refundId);
  await direct.focus();await finance.keyboard.press('Enter');
  await finance.waitForURL(new RegExp(`refundId=${state.refundId}`,'i'));
  await finance.reload();await finance.getByRole('heading',{name:`Refund ${state.refundId}`}).waitFor();
  state.pendingCount=pendingPage.total;state.pendingBrowserVerified=true;await save();
  reviewer=await login('finance-reviewer@cover.example');
  const decision=saved(await api(reviewer,`/finance/refunds/${state.refundId}/decisions`,'POST',
   {kind:'approve',reason:'Independent review of fictional collected premium'},
   {key:state.keys.decision,etag:pending.etag}),201);
  state.decisionId=decision.id;await save();
 }
 const approved=saved(await api(finance,`/finance/refunds/${state.refundId}`),200);
 assert.equal(approved.state,'approved');assert.ok(approved.decisions.some(item=>item.actorId!==approved.requestedBy));
 state.approvalActorIds=approved.decisions.map(item=>item.actorId);state.approvedEtag=approved.etag;await save();
 if(!state.paymentId){const result=await api(finance,`/finance/refunds/${state.refundId}/payments`,'POST',
  {reason:'Release independently approved fictional refund'},
  {key:state.keys.queue,etag:approved.etag});
  state.paymentId=saved(result,202).paymentId;await save();}
 let payment;
 for(let attempt=0;attempt<100;attempt++){
  payment=saved(await api(finance,`/finance/payments/${state.paymentId}`),200);
  if(['paid','rejected','failed'].includes(payment.state))break;
  await new Promise(resolve=>setTimeout(resolve,500));
 }
 state.payment=payment;await save();
 assert.equal(payment.state,'paid',JSON.stringify(payment));
 assert.ok(payment.providerOperationId);
 const replayRequest=await api(finance,`/finance/credits/${input.creditObligationId}/refunds`,'POST',requestBody,
  {key:state.keys.request});assert.equal(saved(replayRequest,201).id,state.refundId);
 const replayQueue=await api(finance,`/finance/refunds/${state.refundId}/payments`,'POST',
  {reason:'Release independently approved fictional refund'},
  {key:state.keys.queue,etag:approved.etag});assert.equal(saved(replayQueue,202).paymentId,state.paymentId);
 const after=saved(await api(finance,`/finance/agencies/${input.agencyId}/refunds?state=pending&page=1&pageSize=50`),200);
 assert.equal(after.total,state.pendingCount-1);
 await finance.goto(`${origin}/accounting?tab=overview&agencyId=${input.agencyId}`);
 await finance.getByRole('row',{name:/Refund approval/}).getByText(`${after.total} pending refund${after.total===1?'':'s'}`,{exact:true}).waitFor();
 state.finalPendingCount=after.total;state.passed=true;state.completedAt=new Date().toISOString();await save();
 console.log(JSON.stringify({refundId:state.refundId,paymentId:state.paymentId,providerOperationId:payment.providerOperationId,pendingBefore:state.pendingCount,pendingAfter:after.total}));
}finally{await reviewer?.close();await finance?.close();await browser.close();}
