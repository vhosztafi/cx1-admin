---
phase: 08-commercial-combined-back-office
plan: '14'
status: complete
subsystem: commercial-cancellation
requires: [08-13]
provides: [commercial-cancellation-review-and-issue, effective-dated-zero-exposure-release, commercial-cancellation-browser]
affects: [08-15, 08-16, 09]
requirements-completed: []
key-files:
  created:
    - backend/src/BackOffice.Infrastructure/Policies/CommercialUnderwritingCancellationSeed.cs
    - backend/src/BackOffice.Infrastructure/Policies/CommercialCancellationExposure.cs
    - backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260920172609_CommercialCancellationIssue.cs
    - scripts/verify-commercial-cancellation-browser.mjs
completed: 2026-09-20
---

# 08-14 — Commercial cancellation review and dated release

Implementation commit: a2e372c.

Commercial cancellation uses reviewed notice/evidence, an exact financial preview and current independent approval before atomic issue. Original cover and exposure remain until the effective instant; the cancellation then contributes zero exposure in the original book.

## Delivered

- Trusted product-selected commercial-cancellation-review settings retain the existing fictional demo-servicing-1 arithmetic/reason rules and separate current senior/requester checks. Missing-only CommercialUnderwritingCancellationSeed preserves all versions and grants no user authority.
- CancellationIssueSnapshot and PolicySnapshotShape support the closed issued-commercial-cancellation-1 schema. It retains exact insured/risk/cover/premium/term and cancellation preview/approval/decision provenance. Generated API unions expose CommercialCancellationPolicyView with its cancellation IDs and commercialExposureDecisionId.
- CancellationIssueService acquires the common exposure fence before source locks. CommercialExposureService.RecordCancellation appends an empty original-book exposure header and exact22-field commercial-cancellation-exposure-decision-1, together with the cancellation version, balanced posted obligation and durable consequences. A reduction does not require additional-capacity permission. Cash paid remains zero.
- Migration20260920172609_CommercialCancellationIssue extends only expected inherited clauses and validates the closed release source, hashes, current grant and complete transaction graph. It refuses downgrade once commercial cancellation history exists. Unused migration reversal/reapply preserves published configuration.
- Commercial consequences include notice and task-close, plus certificate-withdrawal only for selected EL; no MID. The existing notice worker persists demo delivery. Commercial UI exposes the shared cancellation controls, exact issued receipt, signed credit and history; the closed view omits obsolete draft-readiness warnings.
- Actual issued-commercial cancellation now rejects renewal preview and creation, closing the dependent08-13 acceptance case. Later issued renewal prevents cancellation of its prior term.

## Boundaries and review

CC-05 retains Phase9 ownership for generic document and incident operations. Certificate/task requests remain visibly pending until their processors exist; demo notice delivery is real persisted demo state. No external provider messages, sales-funnel changes or live demo migrations/restarts occurred. Human/assistive UAT, hostedCI and Docker are not claimed.

Snapshot RED8 and temporal SQL RED are retained in08-14-DECISIONS.md. SQL Server normalizes stored trigger declaration whitespace; the migration now replaces the declaration prefix while checking each exact prior clause. Desktop/mobile receipt review found and corrected the obsolete draft-readiness warning after issue.

## Verification

Strict .local/phase8-14-verified:1081 distinct passing backend cases including10 real SQL, zero skips. Final SQL9 pass in .local/phase8-14-final-sql2/sql.trx; browser1 pass in .local/phase8-14-browser-2/sql.trx. They cover over-limit reduction, selected/unselected EL, late rollback and malformed-release SQL refusal, exact replay/current authority, notice and independent approval revocation, stale preview, actual later renewal, cancellation-to-renewal refusal, unused migration roundtrip and retained-history downgrade refusal, plus both retained Motor Trade products. Complete desktop1480/mobile390 receipt panels inspected in .local/browser-evidence/commercial-capture/CoverMGA_Test_d98fa2aba90549b99ba564de6a005240.

Root389, web172, contracts71 and full unit1071 pass. Final frontend production build/typecheck/lint pass using isolated .local/next-phase8-14-final. Current integration build has zero warnings/errors. Source ledger4 checks pass; CC-CANCELLATION-RELEASE retains08-14 ownership and is sql-and-browser-verified. git diff --check is clean.

## Self-Check

PASSED. Implementation committed, named artifacts exist, source ownership is preserved and all required14 automated evidence is accepted.
