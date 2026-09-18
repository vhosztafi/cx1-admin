import { quoteFetch, validQuoteEtag, type PendingQuoteCommand, type QuoteObject, type QuoteProposal } from './quotes.ts';
import { underwritingWrite, validUnderwritingId } from './underwriting-decisions.ts';
import type { QuotationTerms } from './underwriting-api.ts';

export function quotedIssueAmount(terms: Pick<QuotationTerms, 'rating' | 'settlement'>): string {
  const pennies = (value: string) => { if (!/^\d{1,13}\.\d{2}$/.test(value)) throw new Error('Reload the quotation amounts.'); return BigInt(value.replace('.', '')); };
  const rate = terms.settlement.feeShareBps;
  if (!Number.isInteger(rate) || rate < 0 || rate > 10000) throw new Error('Reload the quotation settlement.');
  let amount = pennies(terms.rating.grossPayable);
  if (terms.settlement.collector === 'agency' && terms.settlement.mode === 'net-remittance')
    amount -= pennies(terms.rating.brokerCommission) + (pennies(terms.rating.fee) * BigInt(rate) + BigInt(5000)) / BigInt(10000);
  if (amount < BigInt(0)) throw new Error('Reload the quotation amounts.');
  return `${amount / BigInt(100)}.${String(amount % BigInt(100)).padStart(2, '0')}`;
}

export type PolicyIssueReceipt = { policyId: string; policyReference: string; quoteId: string; quoteEtag: string; termId: string; versionId: string; transactionId: string; obligationId: string; documentRequestIds: string[] };
export type IssuedPolicySnapshot = Omit<QuoteProposal, 'termIntent'> & {
  insured: QuoteObject; risk: QuoteObject; cover: QuoteObject;
  term: { kind: string; startsAt: string; endsAt: string; timeZone: string };
  premium: { currency: 'GBP'; annualPremium: string; termPremium: string; tax: string; fee: string; grossPayable: string; brokerCommission: string;
    settlement: { collector: string; mode: string; brokerFeeShare: string; invoiceDue: string; remunerationPayable: string; netEconomicDue: string } };
  provenance: { source: string; quoteRevisionId: string; authorityVersionId: string } | { source: string; sourceQuoteId: string; servicingIssueDecisionId: string;
    baseVersionId: string; revisionId: string; transactionId: string; effectiveAt: string; processedAt: string; sliceOrdinal: number; inputHash: string };
};
export type PolicyView = {
  coverageState: 'scheduled' | 'active' | 'expired' | 'cancelled'; effectiveCutoff: string; knownCutoff: string;
  id: string; reference: string; sourceQuoteId: string; clientId: string; relationshipId: string; agencyId: string;
  termId: string; versionId: string; transactionId: string; issuedAt: string; effectiveAt: string; reason: string;
  termNumber: number; versionSequence: number; transactionSequence: number; contentHash: string; sourceCycleId: string; ratingId: string; acceptanceId: string;
  snapshot: IssuedPolicySnapshot;
  financials: { obligationId: string; transactionId: string; journalId: string; currency: 'GBP'; debtorKind: string; debtorId: string; amountDue: string;
    premium: string; tax: string; fee: string; brokerCommission: string; brokerFeeShare: string; insurerPayable: string; retainedFeeIncome: string; brokerRemunerationPayable: string;
    lines: { accountCode: string; side: 'debit' | 'credit'; amount: string; componentCode: string }[] };
  documentRequests: { id: string; versionId: string; templateVersionId: string; kind: string; state: string }[];
};
export type PolicyTemporalView = PolicyView | { id: string; coverageState: 'not-covered'; effectiveCutoff: string; knownCutoff: string };
export function issuePolicyCommand(quoteId: string, etag: string, body: Record<string, unknown>): PendingQuoteCommand {
  const fields = ['cycleId', 'ratingId', 'acceptanceId', 'termsHash', 'assuranceHash', 'reason'];
  if (Object.keys(body).length !== fields.length || fields.some(x => !Object.hasOwn(body, x)) ||
      ['cycleId', 'ratingId', 'acceptanceId'].some(x => typeof body[x] !== 'string' || !validUnderwritingId(body[x])) ||
      ['termsHash', 'assuranceHash'].some(x => typeof body[x] !== 'string' || !/^[0-9a-f]{64}$/.test(body[x])) ||
      typeof body.reason !== 'string' || !body.reason.trim() || body.reason.length > 1000 || /[\u0000-\u001f\u007f-\u009f]/.test(body.reason))
    throw new Error('Review the current accepted terms and enter an issue reason of up to 1,000 characters.');
  return underwritingWrite(quoteId, `/api/v1/quotes/${quoteId}/issue`, etag, body);
}
export async function sendPolicyIssue(command: PendingQuoteCommand, csrf: string): Promise<PolicyIssueReceipt> {
  if (!csrf || command.method !== 'POST' || command.url !== `/api/v1/quotes/${command.expectedId}/issue`) throw new Error('The issue command cannot be confirmed.');
  const { data, etag } = await quoteFetch<PolicyIssueReceipt>(command.url, { method: command.method, body: command.body,
    headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf, 'Idempotency-Key': command.key, 'If-Match': command.etag! } });
  if (!data || ['policyId', 'termId', 'versionId', 'transactionId', 'obligationId'].some(x => typeof data[x as keyof PolicyIssueReceipt] !== 'string' || !validUnderwritingId(data[x as keyof PolicyIssueReceipt] as string)) ||
      data.quoteId?.toLowerCase() !== command.expectedId?.toLowerCase() || !/^PL-MT-\d{10}$/.test(data.policyReference) || !validQuoteEtag(etag) || data.quoteEtag !== etag ||
      !Array.isArray(data.documentRequestIds) || data.documentRequestIds.length !== 3 || data.documentRequestIds.some(x => !validUnderwritingId(x)) || new Set(data.documentRequestIds).size !== 3)
    throw new Error('The issued policy could not be confirmed. Retry this same action to recover its saved result.');
  return data;
}
export function policyCoverageLabel(startsAt: string, endsAt: string, now = Date.now()): string {
  const from = Date.parse(startsAt), to = Date.parse(endsAt);
  if (!Number.isFinite(from) || !Number.isFinite(to) || to <= from) return 'Coverage dates unavailable';
  return now < from ? 'Inception scheduled' : now >= to ? 'Term ended' : 'In force';
}
