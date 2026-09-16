# 06-03 implementation reconnaissance

Updated2026-09-16 during06-03 execution. Implementation is in the working tree;
06-03 is not complete until its full regression/review/source gates pass.

## Implemented and focused evidence

- Trusted proposal projection resolves source licence issuedOn, named/any/mixed
  counts, activity shares, claims, selected sections, actual monetary metadata and
  pinned age/NCB meanings. New underwriting-meanings.json is generated from source
  reference metadata; runtime does not parse user labels or option IDs as amounts.
- Runtime configuration selects exact published/current product, rating, binder
  and baseline authority for the whole term. Agency-approved terms independently
  grant the target version; changed current terms require explicit refresh.
  Servicing can rate without holding underwriting decision authority.
- Rate closes capture and commits immutable cycle/input/outbox atomically.
  Durable fictional provider supports success/reject/fail-once/timeout-after-success,
  recovers one provider operation and applies only current lease/context. Results
  remain history after retirement/supersession. Provider rejection is a failed
  rating, not a human underwriting decline. Independent limit/source referrals persist.
- Submission stores routing and reason. Return-to-draft supersedes applicability
  without rewriting revisions/results; clone creates a clean draft. Withdrawal
  now accepts progressed unbound states. Refresh appends explicit published pins.
- Six underwriting routes, strict inputs/current roles and development dispatcher
  are registered. Assessment/rating reads use current scope. Later decision,
  evidence review, terms/acceptance/issue capabilities remain false for owning plans.
- Job read dispatches quote-rating through subject scope. Existing integration-retry
  capability alone does not grant business access: manual rating recovery also
  requires current quote-rate authority. Queue budget/ETag/history are retained.
- Source condition refinement: any-driver-minimum-licence typed warranty is owned
  by06-05; current unknown any-driver licence experience remains an independent
  referral. Do not fabricate unnamed-driver licence experience.
- Rate and refresh reason contracts now explicitly bound1000characters, matching
  existing Quote.CaptureClosedReason/QuoteRevision.Reason columns; other cycle
  command reasons remain2000. No truncation or historical migration rewrite.

Focused checks:10runtimeconfig unit cases;2runtimeSQL in
.local/phase6-03-runtime-sql-fixed;5provider/replay/lease scenarios in
.local/phase6-03-worker-scenarios;8worker/lifecycle regression cases in
.local/phase6-03-lifecycle-focused;5cases including actual HTTP auth/CSRF/strict
input/read checks in.local/phase6-03-api-first; explicit refresh1case in
.local/phase6-03-refresh-first. Each passed with0skips. API/Infrastructure builds
passed0warnings/errors. New rejection/manual-retry cases are included in the
currently running full suite, not yet claimed passing.

Full gate started at local21:03: .local/phase6-03-backend-20260916-first,
log.local/phase6-03-backend-first.log, exec session20803. Unit599passed;
integration still pending when this note was written. Contract suite running
with node --test tests/*.test.mjs, log.local/phase6-03-contracts-final.log,
session80580. npm is not on PATH; use directnode or configuredpnpm.

## Remaining before summary/commit

- Collect full gate, fix any failures, run assert-test-results with retained floor
  MinimumTests696/MinimumSqlTests87 and record measured totals. Do not confuse
  focused fixture passes with the full suite.
- Review read-model applicability/capabilities, role+config-before-replay,
  queue lock ordering, unsupported job denial, progressed lifecycle/state consumers,
  rejection/recovery/race coverage and strict contract response shapes.
- Any new changes after the full-suite build need targeted reruns and explicit
  evidence; broaden again only if their risk warrants it.
- Initialize preserved CoverMGA_Demo without reset to install runtime settings;
  repeat seed/preservation proof and no secrets in output.06-02 already installed
  core migration; no03schema migration currently needed.
- Reconcile source action ownership; then commit implementation, write evidence
  SUMMARY and updateSTATE/ROADMAP. KeepUWR01–07pending until06-14.
- Continue06-04 after03gate. UI currently still uses draft/withdrawn binary status;
  later policy issue/acceptance/evidence review not implemented yet.

## Original reconnaissance

- Reuse QuoteScope agency -> current IdentitySnapshot -> quote -> client/relationship locking and SqlCommandBoundary.ExecuteAuthorizedAsync. Add explicit capabilities; internal system-admin has no implicit approve/issue grant. Underwriting mutation scope must use UPDLOCK while read projections retain current authorisation.
- QuoteCaptureConfiguration currently allows at most2version IDs; AgencyDistributionRules at most3. Support retained plus newly published versions through explicitly versioned bounded configuration, rather than replacing pinned histories. No silent resurrection of revoked settings. Approved AgencyTermsVersion/AgencyProduct must independently grant the exact target version; refresh must create a new revision using QuoteService.Append and revalidate matching/provenance. The underwriting seed only publishes versions/grants; it never fabricates agency consent.
- QuoteService.CaptureAvailability currently labels any non-draft/closed quote unavailable. Progression must allow only its own held current-cycle closure while retaining agency/client/config errors. QuoteReadiness has separate evidence category; pricing must retain structural/term/matching/vehicle errors and convert missing proof into independent review needs.
- Source MTS-06-Q24 maps to risk.drivers[].licence.issuedOn. The optional testDate is not the licence-held date; corrected explanatory06-INPUT-MAP text. Use source plan1named/2mixed/3any and actual Q02 count/Q03–04 trusted numeric ages.
- QuoteLookupDispatcher is the concrete worker wiring pattern; SqlJobLeases closed allowlist, subject read/retry and terminal handling must explicitly add quote-rating. JobLease requires ScenarioVersionId: new rating work must supply a real typed scenario version. Keep durable provider completion outside application transaction, current-cycle application under held scope, and lost-lease/stale result history.
- QuoteDiscovery and AgencySharingQuotes already project q.State without mapping. UI quote-list only offers draft/withdrawn, quote-receipt renders binary text; plan06-04 must replace this with actual underwriting state. QuoteLifecycleService.Current and CloneTerms currently demand editability; inspect required clone/withdraw behavior deliberately.
- Pure engine/modules are under Application/Underwriting. New firstissue services must invoke current applicability checks, not treat config schema validity or stored flags as permission.
- Permissions contract explicitly allows Servicing to rate/submit without granting referral or issue authority. Select/pin the product's baseline authority/routing configuration independently of the requesting actor; current decision/issue permission still requires that actor's applicable UserAuthorityGrant. Do not accidentally require an underwriting grant just to obtain a price, and do not infer approval from the actor who requested rating.

## Final gate
Completed in b3c642d. Final full727/99SQL, zero skips;315contract tests and OpenAPI lint passed. See06-03-SUMMARY.md for exact evidence and corrected earlier fixture failures. The pending-test and remaining-work paragraphs above are historical checkpoints; implementation and preservation gates are complete.06-04 now owns UI integration.

