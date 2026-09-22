import {quoteFetch,validQuoteEtag} from './quotes.ts';

export type RenewalLifecycle = {policyId:string;termId:string;etag:string;ruleSettingVersionId:string;
 timeline:{invitationDueAt:string;expiringEnd:string;renewalInception:string;autoLapseAt:string};
 state:'not-due'|'due'|'overdue'|'invited'|'accepted'|'issued'|'lapsed'|'cancelled';canLapse:boolean;
 correspondenceMessageId?:string|null;lapseEventId:string|null;lapseReason:string|null;lapseMode:'manual'|'automatic'|null;recordedAt:string|null;notificationState:string|null;
 notificationAttempts:{number:number;startedAt:string;endedAt:string|null;outcome:string;errorCode:string|null}[]};
export type RenewalLapseReceipt={id:string;policyId:string;termId:string;termEtag:string;effectiveAt:string;recordedAt:string;mode:'manual'|'automatic';notificationId:string};
export type RenewalLapseCommand=Readonly<{termId:string;etag:string;key:string;body:string}>;
const validId=(value:unknown):value is string=>typeof value==='string'&&/^[a-f0-9]{8}(-[a-f0-9]{4}){3}-[a-f0-9]{12}$/i.test(value)&&value!=='00000000-0000-0000-0000-000000000000';
export function renewalLapseCommand(termId:string,etag:string,reason:string):RenewalLapseCommand {
 if(!validId(termId)||!validQuoteEtag(etag)||reason.trim().length<10||reason.length>1000||/[\u0000-\u001f\u007f-\u009f]/.test(reason))throw new Error('Refresh the renewal and enter a lapse reason of 10–1,000 characters.');
 return Object.freeze({termId,etag,key:crypto.randomUUID(),body:JSON.stringify({reason:reason.trim()})});
}
export async function sendRenewalLapse(command:RenewalLapseCommand) {
 const {data:csrf}=await quoteFetch<{requestToken:string}>('/api/v1/auth/csrf');
 const response=await quoteFetch<RenewalLapseReceipt>(`/api/v1/terms/${command.termId}/lapse`,{method:'POST',body:command.body,
  headers:{'Content-Type':'application/json','X-CSRF-Token':csrf.requestToken,'If-Match':command.etag,'Idempotency-Key':command.key}});
 const result=response.data;
 if(!result||result.termId!==command.termId||![result.id,result.policyId,result.notificationId].every(validId)||!validQuoteEtag(response.etag)||result.termEtag!==response.etag||
  !['manual','automatic'].includes(result.mode)||!Number.isFinite(Date.parse(result.effectiveAt))||!Number.isFinite(Date.parse(result.recordedAt)))throw new Error('Lapse readback is unconfirmed. Retry the retained command.');
 return result;
}
