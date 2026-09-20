import { quoteFetch, validQuoteEtag } from './quotes';
import type { QuoteObject, QuoteProposal, QuoteView } from './quotes';

export type ServicingEffectiveIntent = { localDate: string; localTime: string; timeZone: 'Europe/London'; utcOffsetMinutes?: 0 | 60 };
export type ServicingChange = { changeId: string; riskItemId: string; kind: 'driver' | 'vehicle' | 'premises' | 'business' | 'cover' | 'policyholder';
  operation: 'add' | 'update' | 'remove'; payload?: QuoteObject; payloadMode?: 'replace'; effectiveIntent?: ServicingEffectiveIntent;
  specifiedVehicle?: { selected: boolean; required: boolean } };
export type ServicingProposal = { schemaVersion: '1.0'; baseVersionId: string; reason: string; requestedBy: { kind: 'internal' | 'insured' | 'broker'; name?: string };
  commonEffectiveIntent: ServicingEffectiveIntent; dateBasis?: 'shared' | 'per-cover-change'; changes: ServicingChange[];
  cancellationReasonCode?: 'insured-request' | 'non-payment' | 'non-disclosure' | 'trade-ceased' | 'insurer-instruction' };
export type ServicingDifference = { kind: 'added' | 'removed' | 'changed'; path: string; itemId?: string; before?: { path: string; json: string }; after?: { path: string; json: string } };
export type ServicingEditor<T = QuoteProposal> = { draftId: string; revisionId: string; clientId: string; captureVersions: QuoteView['captureVersions'];
  assessment: { base: T; proposed: T; changes: ServicingDifference[]; readinessIssues: { code: string; path: string; questionId: string | null }[];
    slices: { effectiveAt: string; proposed: T; changeIds: string[] }[] } };
export type ServicingDraft<T = ServicingProposal> = { id: string; policyId: string; baseTermId: string; baseVersionId: string; revisionId: string; kind: string; state: string;
  proposal: T; createdAt: string; updatedAt: string;
  context?: { policyReference: string; productCode?: string; baseTermPremium?: string; preparedBy: { id: string; label: string } };
  lease: null | { id: string; holderId: string; generation: number; leaseToken: string; expiresAt: string; active: boolean } };
export type ServicingCommand = Readonly<{ url: string; method: 'POST' | 'PUT' | 'DELETE'; body?: string; etag: string; key: string; fence?: string }>;
export function servicingCommand(url: string, method: ServicingCommand['method'], etag: string, body?: unknown, fence?: string): ServicingCommand {
  if (!validQuoteEtag(etag)) throw new Error('Reload the saved draft before continuing.');
  return Object.freeze({ url, method, etag, body: body === undefined ? undefined : JSON.stringify(body), key: crypto.randomUUID(), fence });
}
export async function sendServicing<T = ServicingProposal>(command: ServicingCommand) {
  const { data: csrf } = await quoteFetch<{ requestToken: string }>('/api/v1/auth/csrf');
  const result = await quoteFetch<ServicingDraft<T>>(command.url, { method: command.method, body: command.body,
    headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf.requestToken, 'Idempotency-Key': command.key, 'If-Match': command.etag,
      ...(command.fence ? { 'X-Edit-Lease': command.fence } : {}) } });
  if (!validQuoteEtag(result.etag) || !result.data?.id || !result.data.proposal) throw new Error('The saved result could not be confirmed. Retry this action.');
  return { data: result.data, etag: result.etag };
}

export async function sendServicingRating(command: ServicingCommand) {
  const { data: csrf } = await quoteFetch<{ requestToken: string }>('/api/v1/auth/csrf');
  const result = await quoteFetch<import('./servicing-rating').ServicingRateReceipt>(command.url, { method: command.method, body: command.body,
    headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf.requestToken, 'Idempotency-Key': command.key, 'If-Match': command.etag,
      ...(command.fence ? { 'X-Edit-Lease': command.fence } : {}) } });
  if (!validQuoteEtag(result.etag) || result.data?.state !== 'queued' || !result.data.id || !result.data.jobId || result.data.draftEtag !== result.etag)
    throw new Error('The rating request could not be confirmed. Retry this action.');
  return result;
}
