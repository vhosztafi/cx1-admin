import { quoteFetch, validQuoteEtag } from './quotes.ts';
import type { ProofScope } from './servicing-proof.ts';

export type AdjustmentIssueInput = { cycleId: string; ratingId: string; termsVersionId: string; acceptanceId: string; termsHash: string; assuranceHash: string; reason: string };
export type AdjustmentIssueCommand = Readonly<{ scope: Readonly<ProofScope>; key: string; body: string; productCode: 'motor-trade' | 'commercial-combined' }>;
export type AdjustmentIssueReceipt = { policyId: string; policyReference: string; draftId: string; draftEtag: string; termId: string; transactionId: string; decisionId: string;
  versionId: string; versionIds: string[]; obligationId: string; journalId: string; accountingPeriodId: string; postingDate: string; currency: 'GBP';
  amountDue: string; amountCredit: string; netAmount: string; documentRequestIds: string[]; midIntentIds: string[]; processedAt: string };
const id = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000';
export function adjustmentIssueCommand(scope: ProofScope, input: AdjustmentIssueInput, productCode: 'motor-trade' | 'commercial-combined' = 'motor-trade'): AdjustmentIssueCommand {
  if (![scope.draftId,scope.cycleId,scope.revisionId,scope.fence,input.ratingId,input.termsVersionId,input.acceptanceId].every(id) ||
    !validQuoteEtag(scope.etag) || input.cycleId !== scope.cycleId || input.reason.trim().length < 10 || input.reason.length > 1000 ||
    ![input.termsHash,input.assuranceHash].every(x => /^[0-9a-f]{64}$/.test(x))) throw new Error('Refresh the accepted terms and check the issue reason.');
  return Object.freeze({ productCode, scope: Object.freeze({...scope}), key: crypto.randomUUID(), body: JSON.stringify(input) });
}
export function confirmAdjustmentIssue(command: AdjustmentIssueCommand, receipt: AdjustmentIssueReceipt, etag: string | null) {
  const commercial = command.productCode === 'commercial-combined';
  if (!receipt || receipt.draftId !== command.scope.draftId || !validQuoteEtag(etag) || receipt.draftEtag !== etag ||
    ![receipt.policyId,receipt.termId,receipt.transactionId,receipt.decisionId,receipt.obligationId,receipt.journalId,receipt.accountingPeriodId].every(id) ||
    !Array.isArray(receipt.versionIds) || receipt.versionIds.length < 1 || receipt.versionIds.length > 100 || !receipt.versionIds.every(id) ||
    receipt.versionId !== receipt.versionIds[0] || new Set(receipt.versionIds).size !== receipt.versionIds.length || receipt.currency !== 'GBP' ||
    ![receipt.amountDue,receipt.amountCredit].every(x => /^(0|[1-9][0-9]{0,12})\.[0-9]{2}$/.test(x)) ||
    !/^-?(0|[1-9][0-9]{0,12})\.[0-9]{2}$/.test(receipt.netAmount) ||
    !Array.isArray(receipt.documentRequestIds) || (commercial ? receipt.documentRequestIds.length < receipt.versionIds.length * 2 || receipt.documentRequestIds.length > receipt.versionIds.length * 3 : receipt.documentRequestIds.length !== receipt.versionIds.length * 3) || !receipt.documentRequestIds.every(id) || new Set(receipt.documentRequestIds).size !== receipt.documentRequestIds.length ||
    !Array.isArray(receipt.midIntentIds) || receipt.midIntentIds.length !== (commercial ? 0 : receipt.versionIds.length) || !receipt.midIntentIds.every(id))
    throw new Error('The issued result could not be confirmed. Retry the retained issue action.');
  return receipt;
}
export async function sendAdjustmentIssue(command: AdjustmentIssueCommand) {
  const {data: csrf} = await quoteFetch<{requestToken:string}>('/api/v1/auth/csrf');
  const result = await quoteFetch<AdjustmentIssueReceipt>(`/api/v1/drafts/${command.scope.draftId}/issue`, {method:'POST',body:command.body,
    headers:{'Content-Type':'application/json','X-CSRF-Token':csrf.requestToken,'Idempotency-Key':command.key,'If-Match':command.scope.etag,'X-Edit-Lease':command.scope.fence}});
  return confirmAdjustmentIssue(command,result.data,result.etag);
}
