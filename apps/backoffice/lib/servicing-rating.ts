export type ServicingRatingSlice = { effectiveAt: string; coverageEndsAt: string; changeIds: string[]; annualPremium: string; annualDelta: string; remainingDays: number; annualDays: number; premium: string; tax: string; brokerCommission: string };
export type ServicingRatingResult = { id: string; outcome: string; completedAt: string; expiresAt: string; resultHash: string; currency: string; baseAnnualPremium: string; premium: string; tax: string; brokerCommission: string; fee: string; grossPayable: string; netDue: string; detailsAvailable: boolean; changeIds: string[]; slices: ServicingRatingSlice[] };
export type ServicingRatingCycle = { id: string; sequence: number; revisionId: string; baseVersionId: string; requestedAt: string; state: string; storedState: string; isCurrent: boolean; applicable: boolean; workId: string; jobState: string; attempts: number; attemptLimit: number; errorCode: string | null; jobEtag: string; nextAttemptAt: string | null; inputHash: string; ruleVersionId: string; agencyTermsVersionId: string; servicingSettingVersionId: string | null; supersededAt: string | null; supersededReason: string | null; result: ServicingRatingResult | null };
export type ServicingRatingHistory = { draftId: string; revisionId: string | null; draftState: string; draftEtag: string; assessedAt: string; currentCycleId: string | null; current: ServicingRatingCycle | null; items: ServicingRatingCycle[]; nextCursor: string | null; blockers: string[] };
export type ServicingRateReceipt = { id: string; draftId: string; revisionId: string; jobId: string; state: 'queued'; draftEtag: string };
export function servicingRatingDisplayState(cycle: ServicingRatingCycle | null, historyEtag: string, draftEtag: string, now: number): string {
  if (!cycle) return 'unrated';
  if (historyEtag !== draftEtag) return 'stale';
  if (cycle.state !== 'rated') return cycle.state;
  if (cycle.result && Date.parse(cycle.result.expiresAt) <= now) return 'expired';
  return cycle.applicable && cycle.result?.detailsAvailable ? 'rated' : 'stale';
}
