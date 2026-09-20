---
phase: 08-commercial-combined-back-office
plan: '13'
status: complete
subsystem: commercial-renewal
requires: [08-12]
provides: [owned-commercial-renewal-experience, full-term-commercial-renewal-rating, inception-dated-commercial-renewal-issue]
affects: [08-14, 08-15, 08-16]
requirements-completed: []
key-files:
  created:
    - backend/src/BackOffice.Application/Policies/CommercialRenewalExperienceRules.cs
    - backend/src/BackOffice.Infrastructure/Policies/CommercialRenewalExperienceService.cs
    - backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260920133214_CommercialRenewalPreparation.cs
    - backend/tests/BackOffice.IntegrationTests/CommercialRenewalTests.cs
    - apps/backoffice/components/policies/commercial-renewal-context.tsx
completed: 2026-09-20
---

# 08-13 — Commercial renewal experience, invitation and atomic issue

Implementation commit: f0de861.

Commercial renewal now uses the expiring term-end risk, retains reviewed whole-risk experience, prices the complete new term and issues a separate term with inception-dated exposure. Current cover remains unchanged before inception.

## Delivered

- Separate missing-only commercial-renewal-preparation settings use one fictional GBP45 renewal fee, supported6/12-month terms and existing demo experience loading. Published/adverse assessments are preserved; commercial fair-value evidence and renewal invitation templates are seeded only when absent. Motor Trade renewal remains GBP35 and commercial adjustment GBP25.
- CommercialRenewalExperienceRules.Subjects/Matches bind the exact saved base/revision, canonical risk hash, locations, wages, loss records and selected liability sections in commercial-renewal-subjects-1. Overall claim count, paid/outstanding and earned premium are counted once. A retained positive EL limit does not select EL without its explicit declaration.
- CurrentCommercialSubjects and DemandCommercialExperience connect the typed manifest to saved preparation, uploads, reviews and rating. API parsing uses a closed bounded object. Prior/foreign subjects cannot be saved, reviewed or rated. Missing experience remains an unresolved information requirement and cannot be approved away.
- Migration20260920133214_CommercialRenewalPreparation adds nullable owned revision/manifest columns, a composite FK and immutable source/review/cycle guards. Existing commercial guards admit renewal through exact forward replacements. Down refuses before mutation once commercial renewal drafts or lapse history exist; an unused installation can downgrade/reapply with published identities preserved.
- Commercial-servicing-rating-input-2 retains preparation, fair value and reviewed experience for full-term commercial rating. Format1 remains commercial adjustment; Motor Trade formats retain their canonical null-omission behavior. Structural manifest comparison supports JSON roundtrip without weakening ownership checks.
- Renewal selection includes known scheduled expiring adjustments. Issue assesses a prospective new term ID under the common commercial exposure fence, then uses that exact ID in the atomic writer. Inception creates the new exposure contribution; the original version, term and journal remain immutable. The issued envelope remains issued-commercial-servicing-1. Invitation delivery and acceptance are persisted demo operations; commercial MID intent is empty.
- Commercial policy and draft UI expose renewal preparation, business-wide experience scope, evidence, decisions, invitation, acceptance and receipt. Generated schemas/OpenAPI and SERVICING-CONTRACTS.md describe these stored inputs and outcomes.
- Five read-only policy call sites now explicitly select the existing write:false mode. An actual update-lock convoy led to a SQL regression:24 reader UPDLOCK commands before correction, zero afterward. Source consistency and current authorization remain held; mutation fences are unchanged.

## Boundaries and follow-up

CC-04 remains compound through08-14. Actual commercial-cancellation-to-renewal rejection is assigned to14 because that plan owns commercial cancellation issue; the shared cancelled-term selector is verified here. Phase8 must check that end-to-end evidence before closing the requirement. CC-05 retains Phase9 operational ownership. Human/assistive UAT, hostedCI and Docker execution are not claimed.

Browser attempts and migration RED evidence are recorded in08-13-DECISIONS.md and08-13-REVIEW.md. The renewal browser's overall test allowance is40 minutes because its full capture/new-business/renewal journey contains36 initial proof attachment/review commands before carrier and invitation work. Production15-second client and individual test request limits are unchanged. The shared proof harness exercises one explicit retained-command retry when a successful backend response arrives after the browser loses acknowledgement.

## Verification

Strict .local/phase8-13-verified:1077 distinct passing backend cases, including15 real SQL scenarios, zero skips. This includes1059 unit cases, three temporal selectors and the completed RealSqlCommercialRenewalBrowser. Active-cover and current-authority replay assertions pass in .local/phase8-13-active-final/sql.trx; browser report .local/phase8-13-browser-7/sql.trx. Desktop1480/mobile390 renewal receipts visually inspected in .local/browser-evidence/commercial-capture/CoverMGA_Test_a3c166ca7d4e4e24b25debd467c30c68. Exact retained issue retry, final API readback and persisted SQL graph all pass.

Root389, web172 and contracts71 pass. Production frontend build/typecheck/lint pass using isolated .local/next-phase8-13-final. Final integration build has zero warnings/errors. Source ledger4 checks pass; CC-RENEWAL-BASE retains08-13 ownership and is sql-and-browser-verified. git diff --check is clean.

## Self-Check

PASSED. Implementation committed, named artifacts exist, source ownership is preserved and all required13 automated evidence is accepted.
