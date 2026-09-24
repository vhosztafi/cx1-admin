export function addFinanceReceiptContracts({schemas,ref,operation,paths,id,instant,date,decimal}) {
 const nullable=schema=>({anyOf:[schema,{type:'null'}]});
 const pageNumber={type:'integer',minimum:1};
 const pageSize={type:'integer',minimum:1,maximum:100};
 const payerKind={type:'string',enum:['unidentified','agency','relationship']};
 const amount={type:'string',pattern:'^(?:0\\.(?!00)[0-9]{2}|[1-9][0-9]{0,12}\\.[0-9]{2})$'};
 const etag={type:'string',pattern:'^"[0-9a-fA-F]{32}"$'};
 schemas.FinanceReceiptAllocation={type:'object',additionalProperties:false,properties:{
  id,invoiceId:id,amount:decimal,reversalOfId:nullable(id),reason:{type:'string'},
  appliedAt:instant,accountingPeriodId:id,postingDate:date},
 required:['id','invoiceId','amount','reversalOfId','reason','appliedAt','accountingPeriodId','postingDate']};
 schemas.FinanceReceipt={type:'object',additionalProperties:false,properties:{
  id,agencyId:id,originKind:{type:'string',enum:['manual','bank-import']},originId:id,
  bankReference:{type:'string'},amount:decimal,residual:decimal,currency:{const:'GBP'},receivedOn:date,
  accountingPeriodId:id,postingDate:date,postedAt:instant,assignmentId:id,payerKind,payerId:nullable(id),
  assignmentOrdinal:{type:'integer',minimum:1},allocations:{type:'array',items:ref('FinanceReceiptAllocation')}},
 required:['id','agencyId','originKind','originId','bankReference','amount','residual','currency','receivedOn',
  'accountingPeriodId','postingDate','postedAt','assignmentId','payerKind','payerId','assignmentOrdinal','allocations']};
 schemas.FinanceReceiptListItem={type:'object',additionalProperties:false,properties:{
  id,amount:decimal,residual:decimal,currency:{const:'GBP'},receivedOn:date,bankReference:{type:'string'},
  payerKind,payerId:nullable(id),assignmentId:id},
 required:['id','amount','residual','currency','receivedOn','bankReference','payerKind','payerId','assignmentId']};
 schemas.FinanceReceiptPage={type:'object',additionalProperties:false,properties:{
  page:pageNumber,pageSize,total:{type:'integer',minimum:0},items:{type:'array',items:ref('FinanceReceiptListItem')}},
 required:['page','pageSize','total','items']};
 schemas.FinanceReceiptRecord={type:'object',additionalProperties:false,properties:{id,assignmentId:id},required:['id','assignmentId']};
 schemas.FinanceReceiptApplication={type:'object',additionalProperties:false,properties:{
  receiptId:id,allocationIds:{type:'array',minItems:1,maxItems:100,items:id}},required:['receiptId','allocationIds']};
 schemas.FinanceReceiptReversal={type:'object',additionalProperties:false,properties:{
  receiptId:id,allocationId:id,reversalId:id},required:['receiptId','allocationId','reversalId']};
 // Earlier design-only receipt operations used these URLs. Replace their
 // placeholder responses with the actual 10-04 runtime representation.
 delete paths['/finance/receipts/{receiptId}'].get;
 delete paths['/finance/receipts/{receiptId}/allocations'].post;
 const writeScope='finance-cash-write and current finance agency scope (also requires finance-read)';
 const readScope='finance-read and current finance agency scope';
 operation('post','/finance/agencies/{agencyId}/receipts','recordFinanceReceipt',writeScope,{
  input:{type:'object',additionalProperties:false,properties:{amount,currency:{const:'GBP'},receivedOn:date,
   bankReference:{type:'string',minLength:1,maxLength:200},originKind:{type:'string',enum:['manual','bank-import']},
   originId:id,payerKind,payerId:nullable(id)},
   required:['amount','currency','receivedOn','bankReference','originKind','originId','payerKind','payerId']},
  output:ref('FinanceReceiptRecord'),status:201});
 operation('get','/finance/agencies/{agencyId}/receipts','listFinanceReceipts',readScope,{
  output:ref('FinanceReceiptPage'),query:[['page',pageNumber],['pageSize',pageSize]]});
 operation('get','/finance/receipts/{receiptId}','getFinanceReceipt',readScope,{output:ref('FinanceReceipt')});
 operation('post','/finance/receipts/{receiptId}/payer','assignFinanceReceiptPayer',writeScope,{
  existing:true,input:{type:'object',additionalProperties:false,properties:{payerKind,payerId:nullable(id),
   reason:{type:'string',minLength:10,maxLength:1000}},required:['payerKind','payerId','reason']},
  output:ref('FinanceReceiptRecord'),status:201});
 operation('post','/finance/receipts/{receiptId}/allocations','allocateFinanceReceipt',writeScope,{
  existing:true,input:{type:'object',additionalProperties:false,properties:{items:{type:'array',minItems:1,maxItems:100,
   items:{type:'object',additionalProperties:false,properties:{invoiceId:id,amount},required:['invoiceId','amount']}}},
   required:['items']},output:ref('FinanceReceiptApplication'),status:201});
 operation('post','/finance/allocations/{allocationId}/reversals','reverseFinanceReceiptAllocation',writeScope,{
  input:{type:'object',additionalProperties:false,properties:{reason:{type:'string',minLength:10,maxLength:1000}},
   required:['reason']},output:ref('FinanceReceiptReversal'),status:201});
 for(const path of ['/finance/agencies/{agencyId}/receipts','/finance/receipts/{receiptId}',
  '/finance/receipts/{receiptId}/payer','/finance/receipts/{receiptId}/allocations',
  '/finance/allocations/{allocationId}/reversals']) {
  for(const op of Object.values(paths[path])) {
   if(!op.operationId?.includes('FinanceReceipt'))continue;
   op['x-runtime-status']='phase-10-04-implemented';
   const success=op.responses[op.operationId==='listFinanceReceipts'||op.operationId==='getFinanceReceipt'?'200':'201'];
   success.headers={...success.headers,'Cache-Control':{schema:{type:'string',const:'no-store'}}};
   if(op.operationId==='getFinanceReceipt'||op.operationId==='assignFinanceReceiptPayer'||op.operationId==='recordFinanceReceipt')
    success.headers.ETag={description:'Current payer assignment version.',schema:etag};
  }
 }
}

export async function writeFinanceReceiptTypes() {
 const {mkdir,writeFile}=await import('node:fs/promises');
 await mkdir('contracts/generated',{recursive:true});
 await writeFile('contracts/generated/finance-receipts.ts',`// Generated by scripts/openapi-finance-receipts.mjs. Exact money uses decimal strings.
export type FinanceReceiptAllocation = { id: string; invoiceId: string; amount: string;
  reversalOfId: string | null; reason: string; appliedAt: string; accountingPeriodId: string; postingDate: string };
export type FinanceReceipt = { id: string; agencyId: string; originKind: 'manual' | 'bank-import';
  originId: string; bankReference: string; amount: string; residual: string; currency: 'GBP';
  receivedOn: string; accountingPeriodId: string; postingDate: string; postedAt: string;
  assignmentId: string; payerKind: 'unidentified' | 'agency' | 'relationship'; payerId: string | null;
  assignmentOrdinal: number; allocations: FinanceReceiptAllocation[] };
export type FinanceReceiptListItem = Pick<FinanceReceipt,'id' | 'amount' | 'residual' | 'currency' |
  'receivedOn' | 'bankReference' | 'payerKind' | 'payerId' | 'assignmentId'>;
export type FinanceReceiptPage = { page: number; pageSize: number; total: number; items: FinanceReceiptListItem[] };
export type FinanceReceiptRecord = { id: string; assignmentId: string };
export type FinanceReceiptApplication = { receiptId: string; allocationIds: string[] };
export type FinanceReceiptReversal = { receiptId: string; allocationId: string; reversalId: string };
`);
}
