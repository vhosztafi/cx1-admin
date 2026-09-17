import type { ConditionDefinition, UnderwritingBlocker } from './underwriting-api.ts';
import { quoteFetch, validQuoteEtag, type PendingQuoteCommand } from './quotes.ts';
export type CapacityExtension = { dimension: string; maximumAmount?: string; minimumAge?: number; maximumAge?: number; questionId?: string; permitted?: true };
export type CapacityMessage = { id: string; submissionId: string; submissionHash: string; direction: 'outbound' | 'inbound'; provenance: string; body: string; recordedAt: string; receivedAt?: string; recordedByLabel: string; applicationState: string; providerUnderwriter?: string; providerReference?: string; outcome?: string; authorisedLimits?: CapacityExtension[]; conditions?: ConditionDefinition[]; validFrom?: string; validTo?: string; evidenceAssociationId?: string };
export type CapacityView = { id: string; quoteId: string; cycleId: string; revisionId: string; referralId: string; providerId: string; providerLabel: string; etag: string; quoteEtag: string; state: string; current: boolean; reason: string; raisedAt: string; raisedByLabel: string; ruleCode: string; dimension: string; assignedUserLabel?: string; currentSubmissionId?: string; submissionHash?: string; submittedAt?: string; responseDueAt?: string; serviceStandard?: string; jobId?: string; currentResponseId?: string; scenarios: { id: string; label: string; version: number }[]; messages: CapacityMessage[]; blockers: UnderwritingBlocker[]; binderContext: { code: string; label: string; requested: string; binderLimit: string }[]; conflictCount: number; attemptHistory: { number: number; startedAt: string; endedAt?: string; outcome: string; errorCode?: string }[]; capabilities: { canSend: boolean; canRecordResponse: boolean; canRevise: boolean } };
export function capacityDimension(ruleCode: string, dimension: string): string {
  return ({ 'cover-stock-custody': 'stock-limit', 'cover-road-risks': 'vehicle-limit', 'cover-tools-equipment': 'tools-limit', 'cover-premises': 'premises-limit' } as Record<string, string>)[ruleCode] ?? dimension;
}
export function capacityExtension(ruleCode: string, dimension: string, fields: { maximumAmount?: string; minimumAge?: string; maximumAge?: string }): CapacityExtension {
  const mapped = capacityDimension(ruleCode, dimension);
  if (['premium-limit','stock-limit','vehicle-limit','tools-limit','premises-limit'].includes(mapped)) {
    if (!/^(0|[1-9]\d{0,12})\.\d{2}$/.test(fields.maximumAmount ?? '') || fields.maximumAmount === '0.00') throw new Error('Enter a positive GBP limit with two decimal places.');
    return { dimension: mapped, maximumAmount: fields.maximumAmount };
  }
  if (mapped === 'driver-age') {
    const min = Number(fields.minimumAge), max = Number(fields.maximumAge);
    if (!Number.isInteger(min) || !Number.isInteger(max) || min < 16 || max > 100 || min > max) throw new Error('Enter an age range between 16 and 100.');
    return { dimension: mapped, minimumAge: min, maximumAge: max };
  }
  if (mapped === 'trade-restriction') return { dimension: mapped, questionId: ruleCode, permitted: true };
  throw new Error('This referral needs a query, decline or an explicit risk revision. It has no supported limit extension.');
}
export function capacityInstant(value: string): string {
  const date = new Date(value); if (!value || !Number.isFinite(date.getTime())) throw new Error('Enter an actual response date and time.'); return date.toISOString();
}
export async function sendCapacityRecovery(command: PendingQuoteCommand, jobId: string, csrf: string, kind: 'capacity-escalation' | 'quote-delivery' = 'capacity-escalation'): Promise<void> {
  if (!csrf || command.url !== `/api/v1/jobs/${jobId}/retry`) throw new Error(`Reload the original ${kind === 'capacity-escalation' ? 'capacity' : 'delivery'} job before recovery.`);
  const result = await quoteFetch<{ id: string; kind: string; state: string }>(command.url, { method: 'POST', body: command.body,
    headers: { 'Content-Type': 'application/json', 'X-CSRF-Token': csrf, 'Idempotency-Key': command.key, 'If-Match': command.etag! } });
  if (result.data.id !== jobId || result.data.kind !== kind || result.data.state !== 'pending' || !validQuoteEtag(result.etag))
    throw new Error('The recovery outcome could not be confirmed. Retry the same action.');
}

