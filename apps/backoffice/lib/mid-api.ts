import {incidentFetch,IncidentError} from './incidents-api.ts';
import {validTaskId} from './tasks-api.ts';
import {validQuoteEtag} from './quotes.ts';
export type MidRetry=Readonly<{submissionId:string;jobId:string;actorId:string;etag:string;body:string;key:string}>;
export function midRetryCommand(submissionId:string,jobId:string,actorId:string,etag:string,reason:string):MidRetry{
 if(!validTaskId(submissionId)||!validTaskId(jobId)||!validTaskId(actorId)||!validQuoteEtag(etag)||!reason.trim()||reason.length>1000)throw new Error('Review the saved MID submission and enter a reason.');
 return Object.freeze({submissionId,jobId,actorId,etag,body:JSON.stringify({reason}),key:crypto.randomUUID()});
}
export async function sendMidRetry(command:MidRetry,csrf:string,actorId:string){
 if(command.actorId!==actorId)throw new IncidentError(403);if(!csrf)throw new Error('The security token is unavailable. Retry the same action.');
 const result=await incidentFetch<{id:string;kind:string;state:string}>(`/api/v1/mid-submissions/${command.submissionId}/retry`,{method:'POST',body:command.body,headers:{'Content-Type':'application/json','X-CSRF-TOKEN':csrf,'Idempotency-Key':command.key,'If-Match':command.etag}});
 if(result.data?.id!==command.jobId||!['mid-update','cancellation-mid-removal'].includes(result.data.kind)||!['pending','leased','succeeded','failed'].includes(result.data.state))throw new Error('The MID retry result is unconfirmed. Retry the same action.');return result.data;
}
