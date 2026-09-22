import { incidentFetch, IncidentError } from './incidents-api.ts';
import { validTaskId } from './tasks-api.ts';
import { validQuoteEtag } from './quotes.ts';
export type ClaimsAction='log-and-handoff'|'handoff'|'contact'|'refresh'|'retry';
export type ClaimsCommand=Readonly<{action:ClaimsAction;incidentId:string;actorId:string;url:string;body:string;etag:string;key:string}>;
export function claimsCommand(action:ClaimsAction,incidentId:string,actorId:string,etag:string,input:object,requestId?:string):ClaimsCommand{
 if(!validTaskId(incidentId)||!validTaskId(actorId)||!validQuoteEtag(etag)||action==='retry'&&!validTaskId(requestId))throw new Error('Reload the saved claims request.');
 return Object.freeze({action,incidentId,actorId,url:`/api/v1/incidents/${incidentId}/${action==='retry'?`requests/${requestId}/retry`:action}`,body:JSON.stringify(input),etag,key:crypto.randomUUID()});
}
export async function sendClaimsCommand(command:ClaimsCommand,csrf:string,actorId:string){
 if(actorId!==command.actorId)throw new IncidentError(403);if(!csrf)throw new Error('The security token is unavailable. Retry the same action.');
 const result=await incidentFetch<{id:string;kind:string;state:string}>(command.url,{method:'POST',body:command.body,headers:{'Content-Type':'application/json','X-CSRF-TOKEN':csrf,'Idempotency-Key':command.key,'If-Match':command.etag}});
 if(!validTaskId(result.data?.id)||result.data.kind!=='operational-claims'||!['pending','leased','succeeded','failed'].includes(result.data.state))throw new Error('The claims request could not be confirmed. Retry the same action.');
 return result.data;
}
