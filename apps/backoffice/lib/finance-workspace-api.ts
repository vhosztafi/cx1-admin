import {csrfToken} from './auth.ts';
import {financeFetch,FinanceCommandError,minorUnits} from './finance-api.ts';

export type WorkspaceCommand=Readonly<{url:string;body:string;key:string;etag?:string;expectedId?:string}>;
const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const id=(value:string)=>{if(!uuid.test(value))throw Error('Open a saved finance record.');return value;};
const key=(value:string)=>{if(value.length<16||value.length>200)throw Error('Prepare a stable command key.');return value;};
const tabs=new Set(['overview','transactions','accounts','payments','reconciliation','bordereaux','refunds']);
export function routeForFinanceTab(tab:string,selected:Record<string,string|undefined>={}){
 if(!tabs.has(tab))throw Error('Choose an accounting section.');
 const query=new URLSearchParams({tab});
 for(const [name,value] of Object.entries(selected))if(value){
  if(!['agencyId','receiptId','statementId','reconciliationId','batchId','refundId','periodId','transactionId'].includes(name))
   throw Error('Unknown finance selection.');
  query.set(name,id(value));
 }
 return `/accounting?${query}`;
}
export function financeCommand(url:string,body:Record<string,unknown>|null,options:{etag?:string;key?:string;expectedId?:string}={}):WorkspaceCommand{
 if(!url.startsWith('/api/v1/finance/'))throw Error('Choose a finance command.');
 return Object.freeze({url,body:body===null?'':JSON.stringify(body),key:key(options.key??crypto.randomUUID()),
  ...(options.etag?{etag:options.etag}:{}),...(options.expectedId?{expectedId:id(options.expectedId)}:{})});
}
export function periodCloseCommand(period:{id:string;etag:string},reason:string,commandKey?:string){
 id(period.id);if(!/^"[A-Za-z0-9+/]{11}="$/.test(period.etag)||reason.trim().length<10)
  throw Error('Review the saved period and give a close reason.');
 return financeCommand(`/api/v1/finance/periods/${period.id}/close`,{reason:reason.trim()},
  {etag:period.etag,key:commandKey,expectedId:period.id});
}
type CorrectionInput={debtorDelta:string;providerDelta:string;cashDelta:string;internalDelta:string;
 effectiveAt:string;reason:string};
export function correctionCommand(kind:'insurance'|'finance-posting',sourceId:string,input:CorrectionInput,commandKey?:string){
 id(sourceId);const values=[input.debtorDelta,input.providerDelta,input.cashDelta,input.internalDelta];
 const pence=values.map(minorUnits);
 if(pence.every(value=>value===0)||BigInt(pence[0])-BigInt(pence[1])+BigInt(pence[2])+BigInt(pence[3])!==BigInt(0)||
  input.reason.trim().length<10||!/^\d{4}-\d{2}-\d{2}T.*(?:Z|\+00:00)$/.test(input.effectiveAt))
  throw Error('Enter a balanced correction, UTC effective time and reason.');
 return financeCommand('/api/v1/finance/corrections',{originalSourceKind:kind,originalSourceId:sourceId,
  ...Object.fromEntries(['debtorDelta','providerDelta','cashDelta','internalDelta'].map((field,index)=>[field,values[index]])),
  effectiveAt:input.effectiveAt,reason:input.reason.trim()},{key:commandKey});
}
export const financeRequest=financeFetch;
export function exactSum(values:readonly string[]):string{
 const pence=values.reduce((total,value)=>total+BigInt(minorUnits(value)),BigInt(0));
 const positive=pence<BigInt(0)?-pence:pence;
 return `${pence<BigInt(0)?'-':''}£${(positive/BigInt(100)).toLocaleString('en-GB')}.${String(positive%BigInt(100)).padStart(2,'0')}`;
}
export async function financeSend(command:WorkspaceCommand):Promise<Record<string,unknown>>{
 const csrf=await csrfToken();if(!csrf)throw Error('Security token unavailable.');
 let response:Response;
 try{response=await fetch(command.url,{method:'POST',cache:'no-store',signal:AbortSignal.timeout(15000),
  headers:{'Content-Type':'application/json','X-CSRF-Token':csrf,'Idempotency-Key':command.key,
   ...(command.etag?{'If-Match':command.etag}:{})},body:command.body});}
 catch{throw new FinanceCommandError(0,true);}
 if(!response.ok){let code='';try{code=(await response.json() as {code?:string}).code??'';}catch{}
  const uncertain=response.status>=500||response.status===409&&code==='command-busy';
  throw new FinanceCommandError(response.status,uncertain,[409,412,428].includes(response.status)&&!uncertain);}
 let saved:Record<string,unknown>;
 try{saved=await response.json() as Record<string,unknown>;}catch{throw new FinanceCommandError(0,true);}
 if(command.expectedId&&saved.periodId!==command.expectedId&&saved.id!==command.expectedId)
  throw new FinanceCommandError(0,true);
 return saved;
}
export async function financeDownload(url:string,filename:string,expectedHash?:string){
 let response:Response;try{response=await fetch(url,{cache:'no-store',signal:AbortSignal.timeout(15000)});}
 catch{throw new FinanceCommandError(0,true);}
 if(!response.ok)throw new FinanceCommandError(response.status);
 if(expectedHash&&response.headers.get('ETag')!==`"${expectedHash}"`)throw new FinanceCommandError(0,true);
 const objectUrl=URL.createObjectURL(await response.blob());
 try{const link=document.createElement('a');link.href=objectUrl;link.download=filename;link.click();}
 finally{URL.revokeObjectURL(objectUrl);}
}
