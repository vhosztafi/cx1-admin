import { quoteFetch, validQuoteEtag, type PendingQuoteCommand, type QuoteProposal } from './quotes.ts';

export type UnderwritingState = 'draft' | 'rating-pending' | 'rated' | 'referred' | 'approved' | 'sent' | 'accepted' | 'declined' | 'bound' | 'withdrawn';
export type UnderwritingBlocker = { code: string; message: string; path?: string; targetId?: string; dimension?: string };
export type UnderwritingContext = { quoteId: string; cycleId: string; revisionId: string; pricingInputHash: string; clientId: string; relationshipId: string; productVersionId: string; agencyTermsVersionId: string; ratingRuleVersionId: string; binderVersionId: string; authorityVersionId: string };
export type UnderwritingAssessment = {
  quoteId: string; quoteEtag: string; state: UnderwritingState; context?: UnderwritingContext; ratingId?: string; jobId?: string;
  createdAt: string; productLabel: string; productVersionLabel: string; providerLabel: string;
  submissionId?: string; assignedTeamId?: string; assignedTeamLabel?: string; assignedUserId?: string; assignedUserLabel?: string;
  blockers: UnderwritingBlocker[]; refreshOptions: UnderwritingRefreshOption[];
  proofRequirements: ProofRequirement[]; appliedEndorsements: { code: string; version: string; wording: string; decisionId: string; targetIds: string[] }[];
  authorityViews: { hasCurrentGrant: boolean; authorityVersionId?: string; rows: { code: string; label: string; requested: string; actorLimit: string; binderLimit: string; actorAllows: boolean; binderAllows: boolean }[] }[];
  assuranceHash?: string;
  capabilities: { canRate: boolean; canSubmit: boolean; canRevise: boolean; canReviewEvidence: boolean; canDecide: boolean; canEscalate: boolean; canPrepareTerms: boolean; canSend: boolean; canAccept: boolean; canIssue: boolean };
};
export type UnderwritingRating = {
  id: string; quoteId: string; cycleId: string; revisionId: string; pricingInputHash: string; ruleVersionId: string;
  completedAt: string; expiresAt: string; applicable: boolean; currency: 'GBP'; annualPremium: string; termPremium: string;
  tax: string; fee: string; grossPayable: string; brokerCommission: string; agencyTermsVersionId: string;
  factors: { code: string; label: string; amount: string; direction: 'charge' | 'discount'; basisAmount?: string; basisPoints?: number; targetId?: string }[];
  input: QuoteProposal; blockers: UnderwritingBlocker[];
};
export type UnderwritingRefreshOption = { productVersionId: string; displayName: string; versionLabel: string; agencyTermsVersionId: string; termsVersion: number; effectiveFrom: string };
export type RatingHistoryItem = { id: string; cycleId: string; revisionId: string; revisionNumber: number; completedAt: string; expiresAt: string; outcome: string; grossPayable: string };
export type UnderwritingReceipt = { id: string; quoteId: string; quoteEtag: string; jobId?: string; state?: 'queued' };
export type UnderwritingAction = 'rate' | 'submit' | 'return-to-draft' | 'underwriting/refresh';
export type ProofRequirement = { code: string; label: string; path: string; riskItemId?: string; conditionId?: string; termsVersionId?: string; inputFingerprint: string; satisfied: boolean };
export type ConditionDefinition = { code: string; driverId?: string; driverIds?: string[]; premisesId?: string; vehicleId?: string; requirementCode?: string; wordingVersion?: string; minimumYears?: number; maximumAmount?: string };
export type ReferralCondition = { id: string; decisionId: string; cycleId: string; etag: string; definition: ConditionDefinition; state: 'outstanding' | 'resolved' | 'superseded'; evidenceAssociationId?: string };
export type ReferralDecision = { id: string; referralId: string; outcome: string; reason: string; question?: string; actorLabel: string; recordedAt: string; conditions: ConditionDefinition[] };
export type Referral = { id: string; quoteId: string; cycleId: string; revisionId: string; etag: string; ruleCode: string; dimension: string; targetId?: string; reason: string; state: string; decisions: ReferralDecision[]; conditions: ReferralCondition[] };
export type UnderwritingEvidence = { id: string; quoteId: string; cycleId: string; revisionId: string; fileId: string; fileName: string; requirementCode: string; riskItemId?: string; conditionId?: string; termsVersionId?: string; inputFingerprint: string; etag: string; screeningState: string; reviewState: string; withdrawn: boolean };
export type EvidenceEvent = { id: string; kind: string; outcome?: string; reason: string; actorLabel: string; recordedAt: string };

const stateLabels: Record<UnderwritingState, string> = { draft: 'Draft', 'rating-pending': 'Rating requested', rated: 'Rated', referred: 'Referred', approved: 'Approved', sent: 'Sent', accepted: 'Accepted', declined: 'Declined', bound: 'Policy issued', withdrawn: 'Withdrawn' };
export function quoteStateLabel(state: string, expiresAt?: string, now: number = Date.now()): string {
  if (['rated', 'referred', 'approved', 'sent', 'accepted'].includes(state) && expiresAt && Date.parse(expiresAt) <= now) return 'Rating expired';
  return Object.hasOwn(stateLabels, state) ? stateLabels[state as UnderwritingState] : 'Status unavailable';
}
export function formatGbp(value: string): string {
  if (!/^\d{1,13}\.\d{2}$/.test(value)) return 'Unavailable';
  const [whole, fraction] = value.split('.');
  return `£${whole.replace(/\B(?=(\d{3})+(?!\d))/g, ',')}.${fraction}`;
}
const validId = (value: unknown): value is string => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value) && value !== '00000000-0000-0000-0000-000000000000';
export function underwritingCommand(quoteId: string, action: UnderwritingAction, etag: string, body: Record<string, unknown>, key: string = crypto.randomUUID()): PendingQuoteCommand {
  if (!validId(quoteId) || !validQuoteEtag(etag)) throw new Error('Reload the quote before reviewing this action.');
  const fields = action === 'rate' ? ['revisionId', 'reason'] : action === 'underwriting/refresh' ? ['revisionId', 'productVersionId', 'confirmedTermsVersionId', 'reason'] : ['cycleId', 'reason'];
  if (!['rate', 'submit', 'return-to-draft', 'underwriting/refresh'].includes(action) || Object.keys(body).length !== fields.length || fields.some(field => !Object.hasOwn(body, field))) throw new Error('The quote action context is incomplete.');
  if (fields.filter(field => field !== 'reason').some(field => !validId(body[field]))) throw new Error('Select a saved quote version.');
  const maximum = action === 'rate' || action === 'underwriting/refresh' ? 1000 : 2000;
  if (typeof body.reason !== 'string' || !body.reason.trim() || body.reason.length > maximum || /[\u0000-\u001f\u007f-\u009f]/.test(body.reason)) throw new Error(`Enter a reason of up to ${maximum.toLocaleString('en-GB')} characters.`);
  if (key.length < 16 || key.length > 200 || key.trim() !== key || /[\u0000-\u001f\u007f-\u009f]/.test(key)) throw new Error('The command identity is invalid.');
  return Object.freeze({ method: 'POST', url: `/api/v1/quotes/${quoteId}/${action}`, key, etag, body: JSON.stringify(body), expectedId: quoteId });
}
export async function sendUnderwritingCommand(command: PendingQuoteCommand, csrf: string): Promise<UnderwritingReceipt> {
  if (!csrf) throw new Error('The security token is unavailable. Retry this action.');
  let body: BodyInit = command.body;
  if (command.upload) { const form = new FormData(); form.append('file', command.upload, command.upload.name); form.append('fileName', command.upload.name); form.append('contentType', command.upload.type); body = form; }
  const { data, etag } = await quoteFetch<UnderwritingReceipt>(command.url, { method: command.method, body,
    headers: { ...(command.upload ? {} : { 'Content-Type': 'application/json' }), 'X-CSRF-Token': csrf, 'Idempotency-Key': command.key, 'If-Match': command.etag! } });
  if (!data || !validId(data.id) || data.quoteId?.toLowerCase() !== command.expectedId?.toLowerCase() || !validQuoteEtag(etag) || data.quoteEtag !== etag ||
      (command.url.endsWith('/rate') && (!validId(data.jobId) || data.state !== 'queued'))) throw new Error('The saved outcome could not be confirmed. Retry this same action.');
  return data;
}
