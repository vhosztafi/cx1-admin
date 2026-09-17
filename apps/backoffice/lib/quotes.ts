export type QuoteProductCode = 'motor-trade-road-risks' | 'motor-trade-combined';
export type QuoteValue = string | number | boolean | QuoteObject | QuoteValue[];
export type QuoteObject = { [key: string]: QuoteValue };
export type QuoteTermIntent = {
  kind?: 'annual' | 'short-period'; timeZone?: 'Europe/London';
  localStartDate?: string; localStartTime?: string; utcOffsetMinutes?: 0 | 60;
  localEndDate?: string; localEndTime?: string; endUtcOffsetMinutes?: 0 | 60;
};
export type QuoteProposal = {
  schemaVersion: '1.0'; productCode: QuoteProductCode;
  insured?: QuoteObject; termIntent?: QuoteTermIntent; risk?: QuoteObject; cover?: QuoteObject;
};
export type QuoteIssue = { path: string; code: string; message: string; category: string; severity: 'error' | 'warning'; questionId?: string; relatedPath?: string };
export type QuoteReadiness = { quoteId: string; revisionId: string; ready: boolean; issues: QuoteIssue[] };
export type QuoteProduct = {
  productVersionId: string; productCode: QuoteProductCode; displayName: string; versionLabel: string;
  questionSetVersion: string; referenceDataVersion: string; captureEligible: boolean; unavailableReason?: string;
};
export type QuoteView = {
  id: string; reference: string; relationshipId: string; clientId: string; agencyId: string;
  clientName: string; agencyName: string; productCode: QuoteProductCode; state: string;
  revisionId: string; revisionNumber: number; updatedAt: string; productVersionId: string;
  proposal: QuoteProposal; captureClosed: boolean;
  boundPolicyId?: string | null;
  captureClosedAt: string | null; captureClosedReason: string | null; matchReviewId?: string | null;
  captureVersions: { schemaVersion: string; questionSetVersion: string; referenceDataVersion: string };
  capabilities: { canSave: boolean; canClone: boolean; canWithdraw: boolean; canAttachEvidence: boolean };
  readiness: QuoteReadiness;
};
export const canCaptureQuotes = (roles: string[]) => roles.some(role => ['servicing', 'underwriter', 'senior-underwriter'].includes(role));

// UI permission hints never replace requireActor or current server authority.
// Do not redirect on an error here: the wizard must retain unsaved proposals.
export class QuoteError extends Error {
  status: number;
  constructor(status: number) {
    super(status === 401 ? 'Your session has ended. Keep these edits and sign in again.'
      : status === 403 ? 'Your current access does not allow this quote action.'
      : status === 404 ? 'This quote or relationship is unavailable.'
      : status === 412 || status === 428 ? 'The saved quote has changed. Your edits are retained for comparison.'
      : status === 409 ? 'This action conflicts with the current quote or command. Your edits are retained.'
      : status === 422 ? 'Check the entered values. Incomplete answers can be saved, but invalid values must be corrected.'
      : status === 413 ? 'The proposal exceeds the supported size.'
      : status === 400 || status === 415 ? 'The quote request is invalid. Your edits are retained.'
      : 'The service could not confirm the result. Retry the same action.');
    this.status = status;
  }
}
export const uncertainQuoteFailure = (failure: unknown) => !(failure instanceof QuoteError) || failure.status >= 500 || [408, 429].includes(failure.status);
export const staleQuoteFailure = (failure: unknown) => failure instanceof QuoteError && [409, 412, 428].includes(failure.status);

export async function quoteFetch<T>(url: string, init: RequestInit = {}): Promise<{ data: T; etag: string | null }> {
  const response = await fetch(url, { ...init, cache: 'no-store', signal: init.signal ?? AbortSignal.timeout(15_000) });
  // Never turn an arbitrary server body, internal diagnostic or HTML page into UI copy.
  if (!response.ok) throw new QuoteError(response.status);
  return { data: await response.json() as T, etag: response.headers.get('ETag') };
}

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const validId = (id: string) => guid.test(id) && id !== '00000000-0000-0000-0000-000000000000';
export const validQuoteEtag = (etag: string | null | undefined): etag is string => typeof etag === 'string' && /^"[A-Za-z0-9+/]{10}[AEIMQUYcgkosw048]="$/.test(etag);

// Body bytes are snapshotted once, before any request. Keep this object in a ref
// through uncertain outcomes; retries must not recapture current form values.
export type PendingQuoteCommand = Readonly<{
  method: 'POST' | 'PUT'; url: string; body: string; key: string; etag?: string; expectedId?: string; upload?: File;
}>;
function commandKey(key: string) {
  if (key.length < 16 || key.length > 200 || key.trim() !== key || /[\u0000-\u001f\u007f-\u009f]/.test(key)) throw new Error('The save command identity is invalid.');
  return key;
}
export function createQuoteCommand(relationshipId: string, productVersionId: string, proposal: QuoteProposal, key: string = crypto.randomUUID(), matchSubmissionId?: string): PendingQuoteCommand {
  if (!validId(relationshipId) || !validId(productVersionId)) throw new Error('Select a saved relationship and product.');
  if (matchSubmissionId !== undefined && !validId(matchSubmissionId)) throw new Error('Select a saved matching intake.');
  return Object.freeze({ method: 'POST', url: '/api/v1/quotes', body: JSON.stringify({ relationshipId, productVersionId, proposal, ...(matchSubmissionId ? { matchSubmissionId } : {}) }), key: commandKey(key) });
}
export function saveQuoteCommand(id: string, etag: string, proposal: QuoteProposal, reason?: string, key: string = crypto.randomUUID()): PendingQuoteCommand {
  if (!validId(id) || !validQuoteEtag(etag)) throw new Error('Reload the saved quote version before saving.');
  if (reason !== undefined && (!reason.trim() || reason.length > 1000)) throw new Error('Enter a reason of up to 1,000 characters.');
  return Object.freeze({ method: 'PUT', url: `/api/v1/quotes/${id}/proposal`, body: JSON.stringify({ proposal, ...(reason === undefined ? {} : { reason }) }),
    key: commandKey(key), etag, expectedId: id });
}
export async function sendQuoteCommand(command: PendingQuoteCommand, csrf: string): Promise<{ id: string; etag: string }> {
  if (!csrf) throw new Error('The security token is unavailable. Retry this save.');
  let body: string | FormData = command.body;
  if (command.upload) {
    body = new FormData(); body.append('file', command.upload, command.upload.name);
    body.append('fileName', command.upload.name); body.append('contentType', command.upload.type);
  }
  const result = await quoteFetch<{ id: string }>(command.url, {
    method: command.method, body,
    headers: { ...(command.upload ? {} : { 'Content-Type': 'application/json' }), 'X-CSRF-Token': csrf, 'Idempotency-Key': command.key, ...(command.etag ? { 'If-Match': command.etag } : {}) },
  });
  if (!result.data || typeof result.data.id !== 'string' || !validId(result.data.id) || !validQuoteEtag(result.etag) ||
      (command.expectedId && command.expectedId.toLowerCase() !== result.data.id.toLowerCase())) {
    // A malformed success does not prove failure: retain the identical command.
    throw new Error('The saved quote identity or version could not be confirmed. Retry this same save.');
  }
  return { id: result.data.id, etag: result.etag };
}
