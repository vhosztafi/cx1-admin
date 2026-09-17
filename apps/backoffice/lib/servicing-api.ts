import { quoteFetch, validQuoteEtag } from './quotes';

export type ServicingProposal = { schemaVersion: '1.0'; baseVersionId: string; reason: string; requestedBy: { kind: string; name?: string };
  commonEffectiveIntent: { localDate: string; localTime: string; timeZone: 'Europe/London'; utcOffsetMinutes?: 0 | 60 }; changes: Record<string, unknown>[] };
export type ServicingDraft = { id: string; policyId: string; baseTermId: string; baseVersionId: string; revisionId: string; kind: string; state: string;
  proposal: ServicingProposal; createdAt: string; updatedAt: string;
  lease: null | { id: string; holderId: string; generation: number; leaseToken: string; expiresAt: string; active: boolean } };
export type ServicingCommand = Readonly<{ url: string; method: 'POST' | 'PUT' | 'DELETE'; body?: string; etag: string; key: string; fence?: string }>;
export function servicingCommand(url: string, method: ServicingCommand['method'], etag: string, body?: unknown, fence?: string): ServicingCommand {
  if (!validQuoteEtag(etag)) throw new Error('Reload the saved draft before continuing.');
  return Object.freeze({ url, method, etag, body: body === undefined ? undefined : JSON.stringify(body), key: crypto.randomUUID(), fence });
}
export async function sendServicing(command: ServicingCommand) {
  const { data: csrf } = await quoteFetch<{ requestToken: string }>('/api/v1/auth/csrf');
  const result = await quoteFetch<ServicingDraft>(command.url, { method: command.method, body: command.body,
    headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf.requestToken, 'Idempotency-Key': command.key, 'If-Match': command.etag,
      ...(command.fence ? { 'X-Edit-Lease': command.fence } : {}) } });
  if (!validQuoteEtag(result.etag) || !result.data?.id || !result.data.proposal) throw new Error('The saved result could not be confirmed. Retry this action.');
  return { data: result.data, etag: result.etag };
}
