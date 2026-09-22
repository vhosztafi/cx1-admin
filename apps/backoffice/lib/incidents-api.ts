import { QuoteError, validQuoteEtag } from './quotes.ts';
import { validTaskId } from './tasks-api.ts';
import type { OpsIncident, OpsIncidentDraftWrite, OpsOccurrenceResolution } from '../../../contracts/generated/operations.ts';
export type { OpsIncident, OpsIncidentDraftWrite, OpsOccurrenceResolution };
export type IncidentAction = 'create' | 'update' | 'description' | 'log' | 'occurrence' | 'occurrence-resolution';
export type IncidentCommand = Readonly<{action:IncidentAction; url:string; method:'POST'|'PUT'; body:string; key:string; etag?:string; id:string;actorId:string}>;
export class IncidentError extends QuoteError {
  constructor(status:number){super(status);this.message=status===401?'Your session has ended. Sign in again.':status===403?'Your current access does not allow this action.':status===404?'This incident or historical choice is unavailable.':[409,412,428].includes(status)?'The saved incident changed. Review the latest version; your edits are retained.':[400,413,422].includes(status)?'Check the incident details and readiness. Your edits are retained.':'The result is unconfirmed. Retry the same action.';}
}
export async function incidentFetch<T>(url:string,init:RequestInit={}):Promise<{data:T;etag:string|null}>{
  const response=await fetch(url,{...init,cache:'no-store',signal:init.signal??AbortSignal.timeout(15000)});
  if(!response.ok)throw new IncidentError(response.status);
  return {data:await response.json() as T,etag:response.headers.get('ETag')};
}
export function incidentCommand(action:IncidentAction,id:string,input:object,etag:string|undefined,actorId:string,key=crypto.randomUUID()):IncidentCommand{
  if(!validTaskId(id)||!validTaskId(actorId)||action!=='create'&&!validQuoteEtag(etag))throw new Error('Load the saved incident before continuing.');
  const url=action==='create'?'/api/v1/incidents':`/api/v1/incidents/${id}${action==='update'?'':`/${action}`}`;
  return Object.freeze({action,url,method:action==='update'||action==='description'?'PUT':'POST',body:JSON.stringify(input),key,etag,id,actorId});
}
export async function sendIncidentCommand(command:IncidentCommand,csrf:string,currentActorId:string){
  if(!csrf)throw new Error('The security token is unavailable. Retry this save.');
  if(currentActorId!==command.actorId)throw new IncidentError(403);
  const result=await incidentFetch<OpsIncident|OpsOccurrenceResolution>(command.url,{method:command.method,body:command.body,headers:{'Content-Type':'application/json','X-CSRF-Token':csrf,'Idempotency-Key':command.key,...(command.etag?{'If-Match':command.etag}:{})}});
  const saved=result.data;
  if(!saved||!validTaskId(saved.id)||!validQuoteEtag(result.etag)||!validTaskId(saved.revisionId))throw new Error('The saved incident could not be confirmed. Retry the same action.');
  if(command.action==='occurrence-resolution'){
    if(!('policyId' in saved)||!Array.isArray(saved.candidates))throw new Error('The occurrence result could not be confirmed. Retry the same action.');
  }else{
    const input=JSON.parse(command.body) as Record<string,unknown>;
    if(!('draft' in saved)||!Array.isArray(saved.missing)||(command.action==='create'?saved.draft.policyId!==command.id:saved.id!==command.id)||
      saved.state!==(command.action==='log'?'logged':'draft')||!Number.isFinite(Date.parse(saved.updatedAt))||
      ['create','update'].includes(command.action)&&stable(saved.draft)!==stable(input)||
      command.action==='description'&&saved.draft.description!==input.description||command.action==='occurrence'&&stable(saved.draft.occurrence)!==stable(input.occurrence))
      throw new Error('The saved incident could not be confirmed. Retry the same action.');
  }
  return result;
}
function stable(value:unknown):string{
  if(Array.isArray(value))return '['+value.map(stable).join(',')+']';
  if(value&&typeof value==='object')return '{'+Object.entries(value).sort(([a],[b])=>a.localeCompare(b)).map(([key,item])=>JSON.stringify(key)+':'+stable(item)).join(',')+'}';
  return JSON.stringify(value);
}
export type IncidentOptions={incidentId:string;revisionId:string;resolutionId:string;versionId:string;sourceHash:string;vehicles:Choice[];drivers:Choice[];locations:Choice[];occupations:Choice[];coverCodes:string[]};
export type Choice={id:string;label:string};
