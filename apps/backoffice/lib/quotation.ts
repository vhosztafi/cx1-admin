import { validQuoteEtag, type PendingQuoteCommand } from './quotes.ts';
import { proofMatches, validUnderwritingId } from './underwriting-decisions.ts';
import type { UnderwritingAssessment, UnderwritingEvidence } from './underwriting-api.ts';

export function acceptanceInstant(value: string): string {
  const match = /^(\d{4})-(\d{2})-(\d{2})T(\d{2}):(\d{2}):(\d{2})(?:\.\d{1,3})?(Z|[+-]\d{2}:\d{2})$/.exec(value);
  if (!match) throw new Error('Enter a date and time with an explicit UTC offset, for example 2026-09-17T10:30:00+01:00.');
  const [, year, month, day, hour, minute, second] = match;
  const calendar = new Date(Date.UTC(Number(year), Number(month) - 1, Number(day)));
  const instant = new Date(value);
  if (calendar.getUTCFullYear() !== Number(year) || calendar.getUTCMonth() + 1 !== Number(month) || calendar.getUTCDate() !== Number(day) || Number(hour) > 23 || Number(minute) > 59 || Number(second) > 59 || !Number.isFinite(instant.getTime()))
    throw new Error('Enter an actual date and time with an explicit UTC offset.');
  return instant.toISOString();
}
export function acceptanceProofs(evidence: UnderwritingEvidence[], assessment: UnderwritingAssessment): UnderwritingEvidence[] {
  const purpose = assessment.proofRequirements.find(x => x.code === 'acceptance-proof' && x.termsVersionId === assessment.termsVersionId);
  return purpose && assessment.context ? evidence.filter(x => proofMatches(x, purpose, assessment.context!.cycleId)) : [];
}
export function quotationCommand(quoteId: string, action: 'terms/prepare' | 'terms' | 'acceptances', etag: string, body: Record<string, unknown>): PendingQuoteCommand {
  if (!validUnderwritingId(quoteId) || !validQuoteEtag(etag)) throw new Error('Reload the quote before reviewing this action.');
  const fields = action === 'terms/prepare' ? ['cycleId', 'ratingId', 'templateVersionId'] : action === 'terms' ? ['termsVersionId', 'recipientContactIds'] : ['cycleId', 'ratingId', 'termsVersionId', 'termsHash', 'assuranceHash', 'accepterLabel', 'acceptedAt', 'channel', 'evidenceAssociationId'];
  if (!['terms/prepare', 'terms', 'acceptances'].includes(action) || Object.keys(body).length !== fields.length || fields.some(x => !Object.hasOwn(body, x))) throw new Error('Review the complete quotation action.');
  for (const field of fields.filter(x => x.endsWith('Id'))) if (typeof body[field] !== 'string' || !validUnderwritingId(body[field])) throw new Error('Select a saved quotation version and proof.');
  if (action === 'terms') {
    const ids = body.recipientContactIds;
    if (!Array.isArray(ids) || !ids.length || ids.length > 20 || new Set(ids.map(x => typeof x === 'string' ? x.toLowerCase() : x)).size !== ids.length || ids.some(x => typeof x !== 'string' || !validUnderwritingId(x))) throw new Error('Select 1–20 different saved contacts.');
  }
  if (action === 'acceptances') {
    if (![body.termsHash, body.assuranceHash].every(x => typeof x === 'string' && /^[0-9a-f]{64}$/.test(x))) throw new Error('Reload the current terms and proof before recording acceptance.');
    if (typeof body.accepterLabel !== 'string' || !body.accepterLabel.trim() || body.accepterLabel.length > 200 || /[\u0000-\u001f\u007f-\u009f]/.test(body.accepterLabel)) throw new Error('Enter the name of the person who accepted.');
    if (!['email', 'written', 'telephone'].includes(String(body.channel))) throw new Error('Choose the acceptance channel.');
    if (typeof body.acceptedAt !== 'string') throw new Error('Enter the acceptance time.');
    acceptanceInstant(body.acceptedAt);
  }
  return Object.freeze({ method: 'POST', url: `/api/v1/quotes/${quoteId}/${action}`, etag, key: crypto.randomUUID(), body: JSON.stringify(body), expectedId: quoteId });
}
