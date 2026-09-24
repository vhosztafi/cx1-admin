export function addFinanceBordereauContracts({schemas,ref,operation,paths,id,instant,date,decimal}) {
 const nullable=schema=>({anyOf:[schema,{type:'null'}]});
 const hash={type:'string',pattern:'^[0-9A-F]{64}$'};
 schemas.BordereauValidationIssue={type:'object',additionalProperties:false,properties:{sourceJournalId:nullable(id),field:{type:'string'},code:{type:'string'}},required:['sourceJournalId','field','code']};
 schemas.BordereauMember={type:'object',additionalProperties:false,properties:{
  sourceJournalId:id,policyId:id,transactionId:id,agencyId:id,productVersionId:id,agencyTermsVersionId:id,
  postingDate:date,postedAt:instant,premium:decimal,tax:decimal,fee:decimal,commission:decimal,netDue:decimal,
  currency:{const:'GBP'},policyReference:{type:'string'},providerProductCode:{type:'string'},agencyReference:{type:'string'},
  correctionActorId:nullable(id),correctionReason:nullable({type:'string'}),correctedAt:nullable(instant),
  exclusionActorId:nullable(id),exclusionReason:nullable({type:'string'}),excludedAt:nullable(instant)
 },required:['sourceJournalId','policyId','transactionId','agencyId','productVersionId','agencyTermsVersionId','postingDate','postedAt','premium','tax','fee','commission','netDue','currency','policyReference','providerProductCode','agencyReference','correctionActorId','correctionReason','correctedAt','exclusionActorId','exclusionReason','excludedAt']};
 schemas.BordereauVersion={type:'object',additionalProperties:false,properties:{id,batchId:id,providerId:id,accountingPeriodId:id,
  number:{type:'integer',minimum:1},parentVersionId:nullable(id),sourceCutoff:instant,sourceHash:hash,membersHash:hash,
  schemaVersion:{type:'string'},state:{enum:['unvalidated','invalid','valid']},validation:{type:'array',items:ref('BordereauValidationIssue')},
  contentHash:nullable(hash),members:{type:'array',items:ref('BordereauMember')},createdAt:instant},
  required:['id','batchId','providerId','accountingPeriodId','number','parentVersionId','sourceCutoff','sourceHash','membersHash','schemaVersion','state','validation','contentHash','members','createdAt']};
 schemas.BordereauBatchItem={type:'object',additionalProperties:false,properties:{id,providerId:id,accountingPeriodId:id,currentVersionId:id,
  version:{type:'integer',minimum:1},state:{enum:['unvalidated','invalid','valid']},createdAt:instant},
  required:['id','providerId','accountingPeriodId','currentVersionId','version','state','createdAt']};
 schemas.BordereauPage={type:'object',additionalProperties:false,properties:{page:{type:'integer',minimum:1},pageSize:{type:'integer',minimum:1,maximum:100},
  total:{type:'integer',minimum:0},items:{type:'array',items:ref('BordereauBatchItem')}},required:['page','pageSize','total','items']};
 schemas.BordereauCommand={type:'object',additionalProperties:false,properties:{batchId:id,versionId:id,version:{type:'integer',minimum:1},state:{enum:['unvalidated','invalid','valid']}},required:['batchId','versionId','version','state']};
 const access='finance-bordereau and current internal finance identity/provider scope';
 // Replace the earlier design-only detail operation with the implemented versioned read.
 delete paths['/finance/bordereaux/{batchId}'].get;
 operation('post','/finance/providers/{providerId}/periods/{periodId}/bordereaux','generateFinanceBordereau',access,{output:ref('BordereauCommand'),status:201});
 operation('get','/finance/providers/{providerId}/bordereaux','listFinanceBordereaux',access,{output:ref('BordereauPage'),query:[['page',{type:'integer',minimum:1}],['pageSize',{type:'integer',minimum:1,maximum:100}]]});
 operation('get','/finance/bordereaux/{batchId}','getFinanceBordereau',access,{output:ref('BordereauVersion'),query:[['versionId',id]]});
 operation('post','/finance/bordereaux/{batchId}/members/{sourceJournalId}/corrections','correctFinanceBordereauMapping',access,{existing:true,status:201,
  input:{type:'object',additionalProperties:false,properties:{policyReference:{type:'string',maxLength:100},providerProductCode:{type:'string',maxLength:100},agencyReference:{type:'string',maxLength:100},reason:{type:'string',minLength:10,maxLength:1000}},required:['reason'],anyOf:[{required:['policyReference']},{required:['providerProductCode']},{required:['agencyReference']}]},output:ref('BordereauCommand')});
 operation('post','/finance/bordereaux/{batchId}/members/{sourceJournalId}/exclusions','excludeFinanceBordereauMember',access,{existing:true,status:201,
  input:{type:'object',additionalProperties:false,properties:{reason:{type:'string',minLength:10,maxLength:1000}},required:['reason']},output:ref('BordereauCommand')});
 operation('post','/finance/bordereaux/{batchId}/validations','validateFinanceBordereau',access,{existing:true,status:201,output:ref('BordereauCommand')});
 operation('get','/finance/bordereaux/{batchId}/versions/{versionId}/download','downloadFinanceBordereau',access);
 const download=paths['/finance/bordereaux/{batchId}/versions/{versionId}/download'].get.responses['200'];
 download.content={'text/csv':{schema:{type:'string',format:'binary'}}};
 download.headers={'Content-Disposition':{description:'Exact saved valid version filename.',schema:{type:'string'}},
  'ETag':{description:'SHA-256 of saved CSV bytes.',schema:{type:'string'}},
  'Cache-Control':{schema:{type:'string',const:'no-store'}}};
 for(const path of ['/finance/providers/{providerId}/periods/{periodId}/bordereaux','/finance/providers/{providerId}/bordereaux',
  '/finance/bordereaux/{batchId}','/finance/bordereaux/{batchId}/members/{sourceJournalId}/corrections',
  '/finance/bordereaux/{batchId}/members/{sourceJournalId}/exclusions','/finance/bordereaux/{batchId}/validations'])
  for(const op of Object.values(paths[path]))op['x-runtime-status']='phase-10-09-implemented';
 paths['/finance/bordereaux/{batchId}/versions/{versionId}/download'].get['x-runtime-status']='phase-10-09-implemented';
}

export async function writeFinanceBordereauTypes() {
 const {mkdir,writeFile}=await import('node:fs/promises');
 await mkdir('contracts/generated',{recursive:true});
 await writeFile('contracts/generated/finance-bordereaux.ts',`// Generated by scripts/openapi-finance-bordereaux.mjs. Signed money uses decimal strings.
export type BordereauValidationIssue = { sourceJournalId: string | null; field: string; code: string };
export type BordereauMember = { sourceJournalId: string; policyId: string; transactionId: string; agencyId: string;
  productVersionId: string; agencyTermsVersionId: string; postingDate: string; postedAt: string;
  premium: string; tax: string; fee: string; commission: string; netDue: string; currency: 'GBP';
  policyReference: string; providerProductCode: string; agencyReference: string;
  correctionActorId: string | null; correctionReason: string | null; correctedAt: string | null;
  exclusionActorId: string | null; exclusionReason: string | null; excludedAt: string | null };
export type BordereauVersion = { id: string; batchId: string; providerId: string; accountingPeriodId: string;
  number: number; parentVersionId: string | null; sourceCutoff: string; sourceHash: string; membersHash: string;
  schemaVersion: string; state: 'unvalidated' | 'invalid' | 'valid'; validation: BordereauValidationIssue[];
  contentHash: string | null; members: BordereauMember[]; createdAt: string };
export type BordereauBatchItem = { id: string; providerId: string; accountingPeriodId: string;
  currentVersionId: string; version: number; state: BordereauVersion['state']; createdAt: string };
export type BordereauPage = { page: number; pageSize: number; total: number; items: BordereauBatchItem[] };
export type BordereauCommand = { batchId: string; versionId: string; version: number; state: BordereauVersion['state'] };
`);
}
