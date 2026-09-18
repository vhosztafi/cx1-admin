import { validQuoteEtag } from './quotes.ts';
import { currentProof, type ProofAssociation, type ProofPage, type ProofRequirement } from './servicing-proof.ts';
import type { ConditionDefinition } from './underwriting-api';
export type ServicingCondition = { id: string; code: string; kind: string; satisfied: boolean; etag: string; definition: ConditionDefinition; clauses: { effectiveAt: string; wording: string; endorsementCode: string | null; targetIds: string[] }[] };
export type ServicingDecision = { id: string; sequence: number; outcome: string; reason: string; question: string | null; actorId: string; authorityVersionId: string; grantId: string; decidedAt: string; conditions: ConditionDefinition[] };
export type ServicingReferral = { id: string; sequence: number; ruleCode: string; dimension: string; riskItemId: string | null; state: string; etag: string; decisionId: string | null; decisionReady: boolean; conditions: ServicingCondition[]; reason: string; requiredAuthority: unknown; decision: ServicingDecision | null };
export type ServicingReferrals = ProofPage<ServicingReferral> & { draftId: string; cycleId: string; applicable: boolean };

export function decisionRequest(cycleId: string, rows: Pick<ServicingReferral, 'id' | 'etag'>[], selected: string[], outcome: string, reason: string, conditions: ConditionDefinition[], question?: string) {
  if (!selected.length || selected.length > 50 || new Set(selected).size !== selected.length || !['approve','approve-with-conditions','query','decline','reopen'].includes(outcome) || reason.trim().length < 10 || reason.length > 2000)
    throw new Error('Select current referrals and enter a decision reason of at least 10 characters.');
  const conditional = outcome === 'approve-with-conditions' || outcome === 'query';
  if (conditional && (!conditions.length || conditions.length > 20) || outcome === 'query' && (!question || question.trim().length < 10 || question.length > 2000 || conditions.some(x => !['provide-driver-proof','provide-premises-security','provide-trading-history'].includes(x.code))))
    throw new Error('Add typed conditions and a question for a request for information.');
  const decisions = selected.map(referralId => {
    const matches = rows.filter(row => row.id === referralId);
    if (matches.length !== 1 || !validQuoteEtag(matches[0].etag)) throw new Error('The selected referrals have changed. Select them again from the current page.');
    return {referralId, etag:matches[0].etag, outcome, reason, ...(conditional ? {conditions} : {}), ...(outcome === 'query' ? {question} : {})};
  });
  return decisions.length === 1 ? {path:`/referrals/${decisions[0].referralId}/decisions`, body:{cycleId, decision:decisions[0]}} : {path:'/referrals/decisions', body:{cycleId, decisions}};
}

export function conditionProof(condition: ServicingCondition, requirement: ProofRequirement, proof: ProofAssociation, outcome: string): boolean {
  const definition = condition.definition;
  const code = condition.kind === 'warranty' ? 'warranty-acknowledgement' : condition.code === 'provide-trading-history' ? 'trading-history' : condition.code === 'provide-premises-security' ? 'premises-security' : condition.code === 'provide-driver-proof' ? definition.requirementCode : null;
  const target = condition.kind === 'warranty' ? null : definition.driverId ?? definition.premisesId ?? null;
  return !!code && requirement.code === code && requirement.riskItemId === target && condition.clauses.length > 0 &&
    condition.clauses.every(clause => requirement.effectiveDates.some(date => Date.parse(date) === Date.parse(clause.effectiveAt))) &&
    currentProof(proof, requirement) && !!proof.latestReviewId && ['accepted','rejected'].includes(proof.reviewOutcome ?? '') && (outcome === 'rejected' || outcome === 'satisfied' && proof.reviewOutcome === 'accepted');
}
