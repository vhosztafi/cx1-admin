export function addFinanceReconciliationContracts({schemas,ref,operation,paths,id,instant,date,decimal}){
 const nullable=schema=>({anyOf:[schema,{type:'null'}]});
 const text=(max,min=1)=>({type:'string',minLength:min,maxLength:max});
 const object=(properties,required=Object.keys(properties))=>({type:'object',additionalProperties:false,properties,required});
 const amount={type:'string',pattern:'^-?(?:0|[1-9][0-9]{0,12})\\.[0-9]{2}$'};
 const pageNumber={type:'integer',minimum:1},pageSize={type:'integer',minimum:1,maximum:100};
 schemas.FinanceBankLine=object({id,agencyId:id,importKey:text(200),valueDate:date,reference:text(200),
  signedAmount:decimal,currency:{const:'GBP'},rawJson:{type:'string'},importedAt:instant,
  duplicateCandidateIds:{type:'array',items:id}});
 schemas.FinanceBankLinePage=object({page:pageNumber,pageSize,total:{type:'integer',minimum:0},
  items:{type:'array',items:ref('FinanceBankLine')}});
 schemas.FinanceReconciliationMatch=object({id,bankLineId:id,financePostingId:id,signedAmount:decimal,
  reversalOfId:nullable(id),reason:text(1000,10),matchedAt:instant});
 schemas.FinanceReconciliationLine=object({bankLineId:id,importKey:text(200),valueDate:date,
  signedAmount:decimal,residual:decimal,excluded:{type:'boolean'},duplicateOfBankLineId:nullable(id),
  explanation:nullable({type:'string'}),addressed:{type:'boolean'}});
 schemas.FinanceReconciliationTarget=object({financePostingId:id,postingDate:date,sourceKind:{const:'receipt'},
  sourceId:id,signedAmount:decimal,residual:decimal,explanation:nullable({type:'string'}),addressed:{type:'boolean'}});
 schemas.FinanceReconciliation=object({id,agencyId:id,from:date,to:date,completedAt:nullable(instant),
  netVariance:decimal,absoluteVariance:decimal,unaddressedCount:{type:'integer',minimum:0},
  lines:{type:'array',items:ref('FinanceReconciliationLine')},
  targets:{type:'array',items:ref('FinanceReconciliationTarget')},
  matches:{type:'array',items:ref('FinanceReconciliationMatch')}});
 schemas.FinanceBankImportResult=object({id,reimported:{type:'boolean'}});
 schemas.FinanceReconciliationCreateResult=object({id,existing:{type:'boolean'}});
 schemas.FinanceBankMatchResult=object({id,bankLineId:id,postingId:id});
 schemas.FinanceBankReverseResult=object({id,matchId:id});
 schemas.FinanceBankExcludeResult=object({id,bankLineId:id});
 schemas.FinanceBankExplainResult=object({id,bankLineId:id,residual:decimal});
 schemas.FinanceTargetExplainResult=object({id,financePostingId:id,residual:decimal});
 schemas.FinanceReconciliationCompleteResult=object({id,actualVariance:decimal,
  absoluteVariance:decimal,completedAt:instant});
 // Replace earlier design-only operations whose IDs and body shapes did not
 // match the saved 10-06 reconciliation source contract.
 delete paths['/finance/reconciliations/{reconciliationId}']?.get;
 delete paths['/finance/reconciliations/{reconciliationId}/matches']?.post;
 delete paths['/finance/reconciliations/{reconciliationId}/complete']?.post;
 const write='finance-reconcile with current stored finance agency scope';
 const read='finance-read with current stored finance agency scope';
 operation('post','/finance/agencies/{agencyId}/bank-lines','importFinanceBankLine',write,{
  input:object({importKey:text(200),valueDate:date,reference:text(200),signedAmount:amount,
   currency:{const:'GBP'},raw:{type:'object',additionalProperties:true}}),
  output:ref('FinanceBankImportResult'),status:201});
 operation('get','/finance/agencies/{agencyId}/bank-lines','listFinanceBankLines',read,{
  output:ref('FinanceBankLinePage'),query:[['page',pageNumber],['pageSize',pageSize]]});
 operation('get','/finance/bank-lines/{bankLineId}','getFinanceBankLine',read,{output:ref('FinanceBankLine')});
 operation('post','/finance/agencies/{agencyId}/reconciliations','createFinanceReconciliation',write,{
  input:object({from:date,to:date}),output:ref('FinanceReconciliationCreateResult'),status:201});
 operation('get','/finance/reconciliations/{reconciliationId}','getFinanceReconciliation',read,
  {output:ref('FinanceReconciliation')});
 operation('post','/finance/reconciliations/{reconciliationId}/matches','matchFinanceBankLine',write,{
  input:object({bankLineId:id,financePostingId:id,signedAmount:amount,reason:text(1000,10)}),
  output:ref('FinanceBankMatchResult'),status:201});
 operation('post','/finance/reconciliation-matches/{matchId}/reversals','reverseFinanceBankMatch',write,{
  input:object({reason:text(1000,10)}),output:ref('FinanceBankReverseResult'),status:201});
 operation('post','/finance/reconciliations/{reconciliationId}/exclusions','excludeFinanceBankDuplicate',write,{
  input:object({bankLineId:id,duplicateOfBankLineId:id,evidenceReference:text(300,10),reason:text(1000,10)}),
  output:ref('FinanceBankExcludeResult'),status:201});
 operation('post','/finance/reconciliations/{reconciliationId}/variances','explainFinanceBankVariance',write,{
  input:object({bankLineId:id,reason:text(1000,10)}),output:ref('FinanceBankExplainResult'),status:201});
 operation('post','/finance/reconciliations/{reconciliationId}/target-variances','explainFinanceCashVariance',write,{
  input:object({financePostingId:id,reason:text(1000,10)}),output:ref('FinanceTargetExplainResult'),status:201});
 operation('post','/finance/reconciliations/{reconciliationId}/complete','completeFinanceReconciliation',write,{
  input:object({}),output:ref('FinanceReconciliationCompleteResult'),status:201});
 for(const path of ['/finance/agencies/{agencyId}/bank-lines','/finance/bank-lines/{bankLineId}',
  '/finance/agencies/{agencyId}/reconciliations','/finance/reconciliations/{reconciliationId}',
  '/finance/reconciliations/{reconciliationId}/matches','/finance/reconciliation-matches/{matchId}/reversals',
  '/finance/reconciliations/{reconciliationId}/exclusions','/finance/reconciliations/{reconciliationId}/variances',
  '/finance/reconciliations/{reconciliationId}/target-variances',
  '/finance/reconciliations/{reconciliationId}/complete'])for(const op of Object.values(paths[path])){
   if(!op.operationId?.includes('Finance'))continue;
   op['x-runtime-status']='phase-10-06-implemented';
   const success=op.responses[op.responses['201']?'201':'200'];
   success.headers={...success.headers,'Cache-Control':{schema:{type:'string',const:'no-store'}}};
  }
}

export async function writeFinanceReconciliationTypes(){
 const {writeFile}=await import('node:fs/promises');
 await writeFile('contracts/generated/finance-reconciliation.ts',`// Generated by scripts/openapi-finance-reconciliation.mjs. Money is exact decimal text.
export type FinanceBankLine = { id:string; agencyId:string; importKey:string; valueDate:string;
 reference:string; signedAmount:string; currency:'GBP'; rawJson:string; importedAt:string; duplicateCandidateIds:string[] };
export type FinanceBankLinePage = { page:number; pageSize:number; total:number; items:FinanceBankLine[] };
export type FinanceReconciliationMatch = { id:string; bankLineId:string; financePostingId:string;
 signedAmount:string; reversalOfId:string|null; reason:string; matchedAt:string };
export type FinanceReconciliationLine = { bankLineId:string; importKey:string; valueDate:string;
 signedAmount:string; residual:string; excluded:boolean; duplicateOfBankLineId:string|null;
 explanation:string|null; addressed:boolean };
export type FinanceReconciliationTarget = { financePostingId:string; postingDate:string;
 sourceKind:'receipt'; sourceId:string; signedAmount:string; residual:string;
 explanation:string|null; addressed:boolean };
export type FinanceReconciliation = { id:string; agencyId:string; from:string; to:string;
 completedAt:string|null; netVariance:string; absoluteVariance:string; unaddressedCount:number;
 lines:FinanceReconciliationLine[]; targets:FinanceReconciliationTarget[]; matches:FinanceReconciliationMatch[] };
export type FinanceBankImportResult = { id:string; reimported:boolean };
export type FinanceReconciliationCreateResult = { id:string; existing:boolean };
export type FinanceBankMatchResult = { id:string; bankLineId:string; postingId:string };
export type FinanceBankReverseResult = { id:string; matchId:string };
export type FinanceBankExcludeResult = { id:string; bankLineId:string };
export type FinanceBankExplainResult = { id:string; bankLineId:string; residual:string };
export type FinanceTargetExplainResult = { id:string; financePostingId:string; residual:string };
export type FinanceReconciliationCompleteResult = { id:string; actualVariance:string; absoluteVariance:string; completedAt:string };
`);
}
