---
phase: 06-underwriting-and-first-policy-issue
plan: '03'
status: complete
completed: 2026-09-16
implementation_commit: b3c642d
requirements_completed: []
---

# 06-03 — Persistent rating and revision lifecycle

Implemented in `b3c642d`. Six API routes now support rate, submit, return to draft,
explicit published-version refresh, assessment and exact rating reads. Current
stored identity, subject ownership and applicable configuration precede receipt
replay. Servicing can request a price without underwriting decision authority.

## Delivered behavior

- Trusted source projection uses actual licence issue date, named/any-driver
  declarations, reference values, activities, selected sections and term. Missing
  facts remain missing; proof review is separate from pricing readiness.
- Atomic capture closure, immutable input/cycle and outbox work; fictional durable
  provider success, rejection, transient failure and timeout-after-success.
  Lease, attempt, current owner/revision and configuration checks prevent stale
  application while preserving results. Rejection is a failed rating, not a human
  underwriting decline. Bounded recovery requires both business and retry access.
- Independent source/binder/authority referrals; submission retains routing and
  reason. Return, clone and withdrawal preserve history. Explicit refresh appends
  a revision with currently approved commercial terms and published product pins.
- Runtime configuration and scenario seed preserve revocations and existing
  approved terms. No additional schema migration was needed after06-02.

## Verification

- Full backend: **727 passed = 601 unit + 126 integration/API**, zero failures or
  skips. Integration duration11m17s. Fresh TRX directory
  `.local/phase6-03-backend-20260916-final`; log
  `.local/phase6-03-backend-final.log`. `assert-test-results.ps1` verified727
  cases including **99 real SQL scenarios**, with those exact minimums.
- SQL tests include request/save race, immutable closure, exact replay, expired
  lease, requester revocation, rule retirement, six-attempt budget recovery,
  provider rejection, lost successful response, explicit refresh, submission,
  clean clone and return. Actual HTTP tests cover roles, CSRF, ETags, strict JSON,
  owned reads and safe failures. Worker scenarios share `UnderwritingRuntimeTests`
  through partial `QuoteRatingTests.cs`, rather than a duplicate worker fixture.
- The earlier full run exposed a retry fixture's fixed ten-minute assumption;
  the fixture now uses persisted NextAttemptAt. Whole-term checks exposed a
  refresh fixture starting before new agency terms; its inception was corrected.
  The final full run contains both fixes and all final production changes.
- All **315 contract/source tests** passed, zero skips, in
  `.local/phase6-03-contracts-verified.log`. OpenAPI lint passed with10 existing
  unused-component warnings in `.local/phase6-03-openapi-lint-verified.log`.
  Six implemented routes are marked `phase-6-03-implemented`; future routes remain
  pending. Contract command coverage includes both statuses.
- Preserved demo initialized without reset. Before/after files
  `.local/phase6-03-preservation-before.txt` and `-after.txt` are identical:
  258 quotes,1619 revisions,16 evidence files,38 associations,9 agency terms,
  3 original product versions and25 credentials, with retained hashes unchanged.
  Initialization log `.local/phase6-03-initialize.log`.
- `git diff --check` passed; `frontend-code` unchanged. No real provider,
  transmission, payment, deployment, hosted CI, Docker or human UAT is claimed.

## Source coverage and next owner

Backend supports source re-rate placements CTL-0b1d8505538d/CTL-831d06857ca3,
clone CTL-47ad572ff959/CTL-7f2b0fedb3a8 and withdrawal
CTL-32b4f87c9518/CTL-807425c1bbac. Their visual acceptance belongs to06-04.

06-04 owns overview/actions, requested cover controls and browser evidence,
including scoped persisted job/history/refresh-option projections identified in
06-04-INTEGRATION-NOTES. Its recovery tests were drafted while this regression
ran; their initial missing-module failure is recorded, not claimed passing.
06-05 owns the typed any-driver minimum-licence warranty and its applicability;
unknown experience currently remains an independent referral. Later proof,
decision, capacity, terms, acceptance and issue capabilities remain closed.
Compound requirements remain pending until06-14.
