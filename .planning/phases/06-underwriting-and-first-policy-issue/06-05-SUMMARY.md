---
phase: 06-underwriting-and-first-policy-issue
plan: '05'
status: complete
completed: 2026-09-16
implementation_commit: 4ace720
requirements_completed: []
---

# 06-05 — Proof review, referrals and typed conditions

Implemented in `4ace720`. Active-cycle evidence upload, association, independent
review and withdrawal now persist through scoped strict APIs. Bulk and individual
approve/conditional/query/decline/reopen decisions retain immutable actor, reason,
authority and history. Conditions use the closed catalogue and current risk IDs;
explicit resolution requires exact same-condition reviewed proof. Risk-change
conditions cannot be resolved on the old cycle.

Current grants and all applicable authority dimensions are checked before receipt
replay. Bulk decisions validate all selected versions before effects. UW-22 needs
reviewed trading proof; W-07 retains source security wording. The any-driver
licence warranty affects only its exact missing-licence predicate, with age and
other limits still enforced. Proof/decision changes alter assurance independently
of saved pricing; withdrawal reopens an otherwise resolved conditional approval.

## Storage and path refinements

- Added feature-owned decisions, conditions, resolutions and evidence associations.
  A unified immutable evidence-event stream represents reviews/withdrawals with
  checked kinds and separate latest pointers. This implements the public event
  history contract without a redundant review table.
- Composite ownership, append-only definitions/history, monotonic latest pointers,
  current proof and same-product/version/binder authority guards are in SQL.
- Additive migrations `20260916213503_UnderwritingDecisionStorage` and
  `20260916220625_UnderwritingPreparedTermsFence` applied to CoverMGA_Demo.
  Inspected SQL: `.local/phase6-05-migration-forward.sql`; application evidence:
  `.local/phase6-05-initialize.log` and `phase6-05-applied-migrations.txt`.
- Exact before/after preservation reports match:269quotes,1666revisions,16files,
  38captureassociations,20agencyterms,3originalproductversions,25credentials,
  13cycles,13ratings,12submissions,13referrals,4grants. Historical hashes unchanged.
- Prepared-terms IDs are fenced NULL until06-08 adds real same-cycle ownership.
  Signed-statement conditions reject premature IDs; no fabricated terms records.
- Separate context/read/resolution files keep services manageable. Existing explicit
  ActorContext capabilities and worker dispatcher already cover this slice; no
  redundant capability or worker kind was introduced. Twelve verified routes now
  advertise phase-6-05-implemented; later routes remain pending.
- Assessment now includes exact proof purposes/fingerprints, assuranceHash and
  applied endorsements. Child condition ETags and stored query questions extend
  strict generated projections for the next UI slice.

## Verification

- Fresh full backend: **741 passing cases =606unit+135integration**, including
  **108 real-SQL scenarios**, zero skips. Directory:
  `.local/phase6-05-backend-20260916-first`; log:`.local/phase6-05-backend-first.log`.
  Integration duration13m56s. `assert-test-results.ps1` passed minima741/108.
- Follow-up proof boundary test passed after adding foreign-file and superseded
  cycle assertions: `.local/phase6-05-proof-extra`, log of same stem. This extends
  the test compiled by the full run; production code was unchanged.
- Targeted both-product conditions, SQL fresh/retained upgrade, bulk rollback,
  query/decline/reopen, API CSRF/ETag/strict-input/no-store, role denial, current
  authority before replay, any-driver age and warranty checks all pass.
- Initial fixture failures exposed a hardcoded Road Risks product in the helper,
  missing referral-generating cover, an invalid revocation timestamp, and a reversed
  assertion argument. Corrected before the passing full regression; no skips added.
- **317 contract/source tests** pass after final route status generation:
  `.local/phase6-05-contracts-status.log`. OpenAPI validates with10retained warnings:
  `.local/phase6-05-openapi-lint-final.log`. git diff --check passes.
- No frontend runtime changed in this slice; prior86frontend/both-product browser
  evidence belongs to06-04. No new UI or human UAT pass is claimed.

## Source coverage and remaining work

Reviewed11source control placements depending on06-05 APIs (9quote UI placements
owned06-06 and2later MTA placements). Backend dependencies are verified; their UI
acceptance remains with the mapped owners. W-07 and proof/endorsement projections
are implemented; licence/proof presentation is06-06 and exact terms06-08/09.

Next06-06 integrates Underwriting, evidence, decisions/history and recovery with
actual source-aligned controls. It must add server-calculated requested/actor/binder
authority display projections; optional referral limit fields are not populated
yet. Terms, delivery, acceptance, capacity and policy issue remain later plans.
Compound requirement completion stays with06-14. Human business/assistive-tech UAT,
hostedCI and Docker remain unperformed. Sales funnel unchanged; no demo reset or
real external transmission. No previews remain running.
