import { validQuoteEtag, type PendingQuoteCommand, type QuoteProposal, type QuoteObject } from './quotes.ts';
import type { ConditionDefinition, Referral, ProofRequirement, UnderwritingEvidence } from './underwriting-api.ts';

export const conditionLabels: Record<string, string> = { 'provide-driver-proof': 'Driver proof', 'provide-premises-security': 'Premises security proof', 'provide-trading-history': 'Trading history proof', 'overnight-security': 'Overnight security warranty (W-07)', 'named-drivers-only': 'Named drivers only', 'any-driver-minimum-licence': 'Minimum licence experience', 'revise-stock-limit': 'Revise stock limit', 'revise-vehicle-limit': 'Revise vehicle limit' };
export const validUnderwritingId = (id: string) => /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(id) && !/^0{8}-0{4}-0{4}-0{4}-0{12}$/.test(id);
export function riskItems(proposal: QuoteProposal, kind: string): QuoteObject[] {
  const items = proposal.risk?.[kind]; return Array.isArray(items) ? items.filter((x): x is QuoteObject => !!x && typeof x === 'object' && !Array.isArray(x) && typeof x.id === 'string') : [];
}
export function riskTargetLabel(proposal: QuoteProposal, id?: string): string {
  if (!id) return '';
  for (const kind of ['drivers','premises','vehicles']) {
    const items = riskItems(proposal, kind), index = items.findIndex(item => item.id === id); if (index < 0) continue;
    const item = items[index];
    if (kind === 'drivers') return `Driver ${index + 1} · ${item.firstName ?? ''} ${item.surname ?? ''}`.trim();
    if (kind === 'vehicles') return `Vehicle ${index + 1} · ${item.registration ?? 'Registration not recorded'}`;
    return `Premises ${index + 1}`;
  }
  return 'Historical risk target';
}
export function conditionFromForm(code: string, input: { targetId?: string; driverIds?: string[]; minimumYears?: string; maximumAmount?: string; requirementCode?: string }, proposal: QuoteProposal): ConditionDefinition {
  const target = (kind: string) => { if (!input.targetId || !riskItems(proposal, kind).some(x => x.id === input.targetId)) throw new Error('Select a current saved risk item.'); return input.targetId; };
  const maximum = () => { if (!input.maximumAmount || !/^(0|[1-9]\d{0,12})\.\d{2}$/.test(input.maximumAmount) || /^0\.00$/.test(input.maximumAmount)) throw new Error('Enter a positive GBP amount with two decimal places.'); return input.maximumAmount; };
  switch (code) {
    case 'provide-trading-history': return { code };
    case 'provide-driver-proof': if (!['photocard-both-sides', 'driving-record'].includes(input.requirementCode ?? '')) throw new Error('Choose a driver proof purpose.'); return { code, driverId: target('drivers'), requirementCode: input.requirementCode };
    case 'provide-premises-security': return { code, premisesId: target('premises') };
    case 'overnight-security': return { code, premisesId: target('premises'), wordingVersion: '1' };
    case 'named-drivers-only': {
      const ids = input.driverIds ?? []; if (!ids.length || new Set(ids).size !== ids.length || ids.some(id => !riskItems(proposal, 'drivers').some(x => x.id === id))) throw new Error('Select current named drivers.');
      return { code, driverIds: [...ids], wordingVersion: '1' };
    }
    case 'any-driver-minimum-licence': { const years = Number(input.minimumYears); if (!Number.isInteger(years) || years < 1 || years > 80) throw new Error('Enter 1–80 complete years.'); return { code, minimumYears: years, wordingVersion: '1' }; }
    case 'revise-stock-limit': return { code, maximumAmount: maximum() };
    case 'revise-vehicle-limit': return { code, vehicleId: target('vehicles'), maximumAmount: maximum() };
    default: throw new Error('This condition is not available for the current terms.');
  }
}
export function underwritingWrite(quoteId: string, path: string, etag: string, body: object, upload?: File): PendingQuoteCommand {
  if (!validUnderwritingId(quoteId) || !validQuoteEtag(etag) || !path.startsWith('/api/v1/')) throw new Error('Reload the quote before this action.');
  return Object.freeze({ method: 'POST', url: path, etag, body: JSON.stringify(body), key: crypto.randomUUID(), expectedId: quoteId, ...(upload ? { upload } : {}) });
}
export function referralDecisionCommand(quoteId: string, cycleId: string, etag: string, rows: Pick<Referral, 'id' | 'etag' | 'cycleId' | 'state'>[], selected: string[], outcome: string, reason: string, conditions: ConditionDefinition[] = [], question?: string): PendingQuoteCommand {
  if (!validUnderwritingId(cycleId) || !selected.length || selected.length > 50 || new Set(selected).size !== selected.length) throw new Error('Select 1–50 current referrals.');
  if (!['approve','approve-with-conditions','query','decline','reopen'].includes(outcome) || !reason.trim() || reason.length > 2000) throw new Error('Select a decision and enter a reason.');
  const conditional = outcome === 'approve-with-conditions' || outcome === 'query';
  if (conditional && (!conditions.length || conditions.length > 20) || !conditional && conditions.length) throw new Error('Review the conditions for this decision.');
  if (outcome === 'query' && (!question?.trim() || question.length > 2000 || conditions.some(x => !x.code.startsWith('provide-')))) throw new Error('Enter the question and documentary requirements.');
  const decisions = selected.map(id => {
    const row = rows.find(x => x.id === id);
    if (!row || row.cycleId !== cycleId || row.state === 'superseded' || !validQuoteEtag(row.etag)) throw new Error('A selected referral is no longer current. Reload before deciding.');
    return { referralId: id, etag: row.etag, outcome, reason, ...(conditional ? { conditions } : {}), ...(outcome === 'query' ? { question } : {}) };
  });
  return underwritingWrite(quoteId, `/api/v1/quotes/${quoteId}/referral-decisions`, etag, { cycleId, decisions });
}
export function proofMatches(evidence: UnderwritingEvidence, purpose: ProofRequirement, cycleId: string): boolean {
  return evidence.cycleId === cycleId && evidence.requirementCode === purpose.code && evidence.conditionId === purpose.conditionId && evidence.riskItemId === purpose.riskItemId && evidence.termsVersionId === purpose.termsVersionId && evidence.capacitySubmissionId === purpose.capacitySubmissionId && evidence.inputFingerprint === purpose.inputFingerprint && evidence.screeningState === 'accepted' && evidence.reviewState === 'accepted' && !evidence.withdrawn;
}
