import type { FinanceReceipt, FinanceReceiptPage } from '../../../contracts/generated/finance-receipts.ts';
import type { FinanceLedgerPage, FinanceLedgerRow, FinanceAccountSummary, FinanceTransactionDetail } from '../../../contracts/generated/finance.ts';
import type { FinanceStatementPage, FinanceStatementView } from '../../../contracts/generated/finance-statements.ts';

export type { FinanceReceipt, FinanceReceiptPage, FinanceLedgerRow, FinanceAccountSummary, FinanceStatementView };
export type InvoiceCandidate = { transactionId: string; policyId: string; dueDate: string | null;
  debtorKind: 'agency'|'relationship'; debtorId:string|null; postedAmount: string; residual: string; paymentState: string };
export type FinanceCommand = Readonly<{action: 'record' | 'assign' | 'allocate' | 'reverse' | 'statement';
  url: string; body: string; key: string; etag?: string; expectedReceiptId?: string; expectedAllocationId?: string}>;

const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const isId=(value: unknown): value is string=>typeof value==='string'&&uuid.test(value)&&value!=='00000000-0000-0000-0000-000000000000';
const sameId=(left:unknown,right:unknown)=>isId(left)&&isId(right)&&left.toLowerCase()===right.toLowerCase();
const exact=/^-?(0|[1-9]\d{0,13})\.\d{2}$/;
export function minorUnits(value: string): number {
 if(!exact.test(value))throw Error('Enter an exact amount with two decimal places.');
 const negative=value.startsWith('-'),[whole,fraction]=value.replace('-','').split('.');
 const pence=(BigInt(whole)*BigInt(100)+BigInt(fraction))*(negative?BigInt(-1):BigInt(1));
 if(pence>BigInt(Number.MAX_SAFE_INTEGER)||pence<BigInt(Number.MIN_SAFE_INTEGER))throw Error('Amount is too large.');
 return Number(pence);
}
export function decimalMoney(pence: number): string {
 if(!Number.isSafeInteger(pence))throw Error('Amount is outside the safe range.');
 const value=BigInt(pence),positive=value<BigInt(0)?-value:value;
 return `${value<BigInt(0)?'-':''}${positive/BigInt(100)}.${String(positive%BigInt(100)).padStart(2,'0')}`;
}
function penceLabel(pence:bigint): string {
 const positive=pence<BigInt(0)?-pence:pence;
 return `${pence<BigInt(0)?'-':''}£${(positive/BigInt(100)).toLocaleString('en-GB')}.${String(positive%BigInt(100)).padStart(2,'0')}`;
}
export function moneyLabel(value: string): string {return penceLabel(BigInt(minorUnits(value)));}
export function combinedBalance(left:string,right:string){
 const total=BigInt(minorUnits(left))+BigInt(minorUnits(right));
 return {label:penceLabel(total),credit:total<BigInt(0)};
}
export function receiptPaymentState(input:{amountDue:string;allocated:string}): 'Unpaid'|'Part paid'|'Paid'|'Credit balance' {
 const due=minorUnits(input.amountDue),allocated=minorUnits(input.allocated);
 if(due<0)return 'Credit balance';if(due===0||allocated>=due)return 'Paid';
 return allocated>0?'Part paid':'Unpaid';
}
export function statementEquation(row:{opening:string;debits:string;credits:string;closing:string}) {
 const opening=minorUnits(row.opening),debits=minorUnits(row.debits),credits=minorUnits(row.credits),closing=minorUnits(row.closing);
 return {opening,debits,credits,closing,balanced:BigInt(opening)+BigInt(debits)-BigInt(credits)===BigInt(closing)};
}
export function invoiceCandidates(rows: FinanceLedgerRow[]): InvoiceCandidate[] {
 const totals=new Map<string,bigint>();
 for(const row of rows)if(row.transactionId)totals.set(row.transactionId,
  (totals.get(row.transactionId)??BigInt(0))+BigInt(minorUnits(row.debtorDelta)));
 return rows.filter(row=>row.sourceKind==='insurance'&&row.transactionId&&row.policyId&&minorUnits(row.debtorDelta)>0)
  .map(row=>{const due=BigInt(minorUnits(row.debtorDelta)),residual=totals.get(row.transactionId!)??due;
   if(residual>BigInt(Number.MAX_SAFE_INTEGER)||residual<BigInt(Number.MIN_SAFE_INTEGER)||
    due-residual>BigInt(Number.MAX_SAFE_INTEGER)||due-residual<BigInt(Number.MIN_SAFE_INTEGER))
    throw Error('Ledger amount is outside the safe pence range.');
   return {transactionId:row.transactionId!,policyId:row.policyId!,dueDate:row.dueDate,
    debtorKind:row.debtorKind,debtorId:row.debtorId,
    postedAmount:row.debtorDelta,residual:decimalMoney(Number(residual)),
    paymentState:receiptPaymentState({amountDue:row.debtorDelta,allocated:decimalMoney(Number(due-residual))})};})
  .filter(row=>minorUnits(row.residual)>0);
}
export class FinanceCommandError extends Error {
 readonly status:number;readonly uncertain:boolean;readonly conflict:boolean;
 constructor(status: number, uncertain=false, conflict=false) {
  super(uncertain?'The service could not confirm the result. Retry the same action.'
   : conflict?'The saved record changed. Review its current version; your entries are retained.'
   : status===403?'Your current access does not allow this finance record.'
   : status===404?'The saved finance record is unavailable.'
   : status===401?'Your session has ended. Sign in again.'
   : status===400||status===422?'Check the finance entries. Your inputs are retained.'
   :'The finance service could not complete this request. Your inputs are retained.');
  this.status=status;this.uncertain=uncertain;this.conflict=conflict;
 }
 get denied(){return this.status===401||this.status===403||this.status===404;}
}
async function responseError(response:Response):Promise<FinanceCommandError>{
 let code='';try{code=(await response.json() as {code?:string}).code??'';}catch{}
 const uncertain=response.status>=500||(response.status===409&&code==='command-busy');
 return new FinanceCommandError(response.status,uncertain,[409,412,428].includes(response.status)&&!uncertain);
}
export async function financeFetch<T>(url:string):Promise<T>{
 let response:Response;try{response=await fetch(url,{cache:'no-store',signal:AbortSignal.timeout(15000)});}
 catch{throw new FinanceCommandError(0,true);}
 if(!response.ok)throw await responseError(response);
 return await response.json() as T;
}
function commandKey(value:string){if(typeof value!=='string'||value.length<16||value.length>200||value.trim()!==value||/[\u0000-\u001f]/.test(value))throw Error('Prepare a new command key.');return value;}
function assignmentEtag(receipt:Pick<FinanceReceipt,'assignmentId'>){if(!isId(receipt.assignmentId))throw Error('Reload the saved payer assignment.');return `"${receipt.assignmentId.replaceAll('-','').toLowerCase()}"`;}
export function receiptCommand(action:'allocate'|'assign'|'reverse',receipt:FinanceReceipt,input:Record<string,unknown>,key=crypto.randomUUID()):FinanceCommand{
 if(!isId(receipt.id)||!isId(receipt.agencyId))throw Error('Open a saved receipt.');commandKey(key);
 let body:Record<string,unknown>,url:string,etag:string|undefined,expectedAllocationId:string|undefined;
 if(action==='allocate'){
  if(receipt.payerKind==='unidentified')throw Error('Assign a payer before allocating.');
  if(!isId(input.invoiceId)||typeof input.amount!=='string')throw Error('Choose a saved invoice and amount.');
  const amount=minorUnits(input.amount),residual=minorUnits(receipt.residual);
  if(amount<=0||amount>residual)throw Error('Amount must be positive and within the receipt residual.');
  body={items:[{invoiceId:input.invoiceId,amount:decimalMoney(amount)}]};url=`/api/v1/finance/receipts/${receipt.id}/allocations`;etag=assignmentEtag(receipt);
 }else if(action==='assign'){
  const kind=input.payerKind,payerId=input.payerId,reason=input.reason;
  if(!['unidentified','agency','relationship'].includes(String(kind))||
   (kind==='unidentified'?payerId!==null:!isId(payerId))||typeof reason!=='string'||reason.trim().length<10||reason.length>1000)
   throw Error('Choose a payer and give a reason of at least 10 characters.');
  body={payerKind:kind,payerId,reason:reason.trim()};url=`/api/v1/finance/receipts/${receipt.id}/payer`;etag=assignmentEtag(receipt);
 }else{
  if(!isId(input.allocationId)||typeof input.reason!=='string'||input.reason.trim().length<10||input.reason.length>1000)
   throw Error('Choose a saved allocation and give a reason of at least 10 characters.');
  expectedAllocationId=input.allocationId;body={reason:input.reason.trim()};url=`/api/v1/finance/allocations/${expectedAllocationId}/reversals`;
 }
 return Object.freeze({action,url,body:JSON.stringify(body),key,...(etag?{etag}:{}),expectedReceiptId:receipt.id,
  ...(expectedAllocationId?{expectedAllocationId}:{})});
}
export function recordReceiptCommand(agencyId:string,input:{amount:string;receivedOn:string;bankReference:string;payerKind:'unidentified'|'agency'|'relationship';payerId:string|null},key=crypto.randomUUID(),originId=crypto.randomUUID()):FinanceCommand{
 if(!isId(agencyId)||!isId(originId)||!/^\d{4}-\d{2}-\d{2}$/.test(input.receivedOn)||!input.bankReference.trim()||input.bankReference.length>200)
  throw Error('Enter agency, date and bank reference.');
 if(minorUnits(input.amount)<=0)throw Error('Enter a positive receipt amount.');
 if(input.payerKind==='unidentified'?input.payerId!==null:!isId(input.payerId))throw Error('Choose a valid payer.');
 return Object.freeze({action:'record',url:`/api/v1/finance/agencies/${agencyId}/receipts`,
  body:JSON.stringify({amount:input.amount,currency:'GBP',receivedOn:input.receivedOn,bankReference:input.bankReference.trim(),
   originKind:'manual',originId,payerKind:input.payerKind,payerId:input.payerId}),key:commandKey(key)});
}
export function statementCommand(agencyId:string,from:string,to:string,key=crypto.randomUUID()):FinanceCommand{
 if(!isId(agencyId)||!/^\d{4}-\d{2}-\d{2}$/.test(from)||!/^\d{4}-\d{2}-\d{2}$/.test(to)||from>=to)
  throw Error('Choose a valid statement window.');
 return Object.freeze({action:'statement',url:`/api/v1/finance/agencies/${agencyId}/statements`,body:JSON.stringify({from,to}),key:commandKey(key)});
}
export async function sendFinanceCommand(command:FinanceCommand,csrf:string):Promise<Record<string,unknown>>{
 if(!csrf)throw Error('Security token unavailable. Retry this action.');
 let response:Response;try{response=await fetch(command.url,{method:'POST',cache:'no-store',signal:AbortSignal.timeout(15000),
  headers:{'Content-Type':'application/json','X-CSRF-Token':csrf,'Idempotency-Key':command.key,...(command.etag?{'If-Match':command.etag}:{})},body:command.body});}
 catch{throw new FinanceCommandError(0,true);}
 if(!response.ok)throw await responseError(response);
 let saved:Record<string,unknown>;try{saved=await response.json() as Record<string,unknown>;}catch{throw new FinanceCommandError(0,true);}
 if(command.action==='record'||command.action==='assign'){
  if(!isId(saved.id)||(command.expectedReceiptId&&!sameId(saved.id,command.expectedReceiptId))||!isId(saved.assignmentId))throw new FinanceCommandError(0,true);
 }else if(command.action==='allocate'||command.action==='reverse'){
  if(!sameId(saved.receiptId,command.expectedReceiptId)||command.action==='allocate'&&(!Array.isArray(saved.allocationIds)||
   saved.allocationIds.length!==1||!isId(saved.allocationIds[0]))||command.action==='reverse'&&
   (!sameId(saved.allocationId,command.expectedAllocationId)||!isId(saved.reversalId)))throw new FinanceCommandError(0,true);
 }else if(!isId(saved.id))throw new FinanceCommandError(0,true);
 return saved;
}
export const receiptList=(agencyId:string,page=1)=>financeFetch<FinanceReceiptPage>(`/api/v1/finance/agencies/${agencyId}/receipts?page=${page}&pageSize=50`);
export const receiptDetail=(id:string)=>financeFetch<FinanceReceipt>(`/api/v1/finance/receipts/${id}`);
export const accountSummary=(agencyId:string)=>financeFetch<FinanceAccountSummary>(`/api/v1/finance/accounts/${agencyId}`);
export const statementList=(agencyId:string)=>financeFetch<FinanceStatementPage>(`/api/v1/finance/agencies/${agencyId}/statements?page=1&pageSize=50`);
export const statementDetail=(id:string)=>financeFetch<FinanceStatementView>(`/api/v1/finance/statements/${id}`);
export const transactionDetail=(id:string)=>financeFetch<FinanceTransactionDetail>(`/api/v1/finance/transactions/${id}`);
export async function ledgerAll(agencyId:string):Promise<FinanceLedgerRow[]>{
 const first=await financeFetch<FinanceLedgerPage>(`/api/v1/finance/ledger?agencyId=${agencyId}&page=1&pageSize=100`);
 const pages=Array.from({length:Math.ceil(first.total/100)-1},(_,i)=>i+2);
 const rest=await Promise.all(pages.map(page=>financeFetch<FinanceLedgerPage>(`/api/v1/finance/ledger?agencyId=${agencyId}&page=${page}&pageSize=100`)));
 return [first,...rest].flatMap(page=>page.items);
}
