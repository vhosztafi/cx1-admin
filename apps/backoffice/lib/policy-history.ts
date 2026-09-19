import {quoteFetch,validQuoteEtag} from './quotes';
import type {ServicingCommand,ServicingDifference} from './servicing-api';
import {validUnderwritingId} from './underwriting-decisions';

export type PolicyHistoryVersion={id:string;termId:string;transactionId:string;termNumber:number;versionSequence:number;transactionSequence:number;sliceOrdinal:number;
 kind:string;effectiveAt:string;processedAt:string;contentHash:string;reason:string;actorId:string|null;applicability:string;
 actorLabel:string;obligationId:string;amountDue:string;documentRequests:{id:string;kind:string;state:string}[]};
export type PolicyHistoryView={policyId:string;reference:string;effectiveAt:string;knownAt:string;selectedVersionId:string|null;coverageState:string;versions:PolicyHistoryVersion[];policyEtag:string};
export type PolicyComparison={policyId:string;beforeVersionId:string;afterVersionId:string;beforeHash:string;afterHash:string;changes:ServicingDifference[]};
export type PolicyCloneTerms={policyId:string;versionId:string;relationshipId:string;termsId:string;termsVersion:number;policyEtag:string};
export type PolicyReconstruction={id:string;policyId:string;termId:string;versionId:string|null;contentHash:string|null;effectiveAt:string;knownAt:string;coverageState:string;manifestHash:string;actorId:string;reason:string;createdAt:string;state:string};
export type PolicyHistoryReceipt={quoteId?:string;revisionId?:string;lineageId?:string;sourcePolicyId?:string;sourceVersionId?:string;quoteEtag?:string;
 requestId?:string;policyId?:string;termId?:string;versionId?:string|null;contentHash?:string|null;effectiveAt?:string;knownAt?:string;coverageState?:string;state?:string};
export async function sendPolicyHistory(command:ServicingCommand):Promise<PolicyHistoryReceipt> {
 const {data:csrf}=await quoteFetch<{requestToken:string}>('/api/v1/auth/csrf');
 const result=await quoteFetch<PolicyHistoryReceipt>(command.url,{method:'POST',body:command.body,headers:{'Content-Type':'application/json',
  'X-CSRF-Token':csrf.requestToken,'Idempotency-Key':command.key,'If-Match':command.etag}});
 const body=JSON.parse(command.body!);const receipt=result.data;
 if(!validQuoteEtag(result.etag))throw new Error('The saved result could not be confirmed. Retry the same request.');
 if(command.url.endsWith('/clone')) {
  if(!receipt.quoteId||!validUnderwritingId(receipt.quoteId)||!receipt.revisionId||!validUnderwritingId(receipt.revisionId)||
   !receipt.lineageId||!validUnderwritingId(receipt.lineageId)||receipt.quoteEtag!==result.etag||receipt.sourceVersionId!==body.versionId||
   command.url!==`/api/v1/policies/${receipt.sourcePolicyId}/clone`)throw new Error('The new quote could not be confirmed. Retry the same request.');
 } else if(!receipt.requestId||!validUnderwritingId(receipt.requestId)||command.url!==`/api/v1/terms/${receipt.termId}/as-at/export`||
  receipt.versionId!==(body.versionId??null)||receipt.contentHash!==(body.contentHash??null)||Date.parse(receipt.effectiveAt??'')!==Date.parse(body.effectiveAt)||
  Date.parse(receipt.knownAt??'')!==Date.parse(body.knownAt)||receipt.state!=='pending')throw new Error('The reconstruction request could not be confirmed. Retry the same request.');
 return receipt;
}
