---
phase: 08-commercial-combined-back-office
plan: '12'
status: complete
subsystem: commercial-adjustment-issue
requires: [08-11]
provides: [commercial-servicing-underwriting, atomic-commercial-adjustment-issue, dated-servicing-exposure]
affects: [08-13, 08-14, 08-16]
requirements-completed: []
key-files:
  created:
    - backend/src/BackOffice.Application/Policies/CommercialServicingRatingRules.cs
    - backend/tests/BackOffice.IntegrationTests/CommercialServicingIssueTests.cs
completed: 2026-09-20
---

# 08-12 — Commercial adjustment underwriting and atomic issue

Implementation commit: `5226707`.

Commercial adjustment drafts now complete rating, reviewed evidence, internal and carrier approval, signed terms, fictional delivery, acceptance and atomic issue. Dated immutable policy versions, exposure decisions, signed financial postings and product-specific document requests are stored together.

## Delivered

- `CommercialServicingRatingRules.Rate` prices complete cumulative commercial risks and feeds shared civil-day incremental arithmetic. Independent positive/zero/negative and multi-date goldens prove one GBP25 fee and balanced postings. The separate `commercial-servicing-rating` configuration is pinned with product configuration.
- `CommercialServicingEvidenceRules`, `CommercialServicingReferralRules` and commercial capacity-response dispatch preserve subject/date/fingerprint provenance. Existing draft APIs and shared UI now support actual commercial decisions through acceptance. The internal cycle format is `commercial-servicing-rating-input-1`; the issued schema is `issued-commercial-servicing-1`.
- `CommercialExposureService.AssessServicing` and `RecordServicing` hold the common exposure fence and assess every affected interval against the full book, binder and actual current issuing grant. Each version retains its source hash and complete `commercial-servicing-exposure-decision-1` manifest. Current authority precedes receipt replay; stale proof/acceptance/base and expired/revoked grants cannot create another effect.
- Additive migrations `20260920092037_CommercialServicingRating`, `20260920093910_CommercialServicingDecisions` and `20260920095821_CommercialServicingAtomicIssue` install closed commercial source/decision/atomic graph guards. The live demo was not migrated or reseeded.
- `ServicingIssueSnapshot` retains actual commercial sections and cumulative term charges; postings represent the signed transaction movement. Document selection requests a schedule and statement plus an employers-liability certificate when selected, with no commercial MID intent. `PolicyReadService` reports the selected version's effective date.
- `CommercialServicingWorkspace` connects saved exposure and shared decision/issue panels. Editor reads use single-flight refresh. Immutable proposal and carrier-response parsing is reused only within one held request; live authority, response selection, conditions and proof remain reassessed. Generated schemas/OpenAPI and design contracts describe the final behavior.
- Existing four adjustment controls retain08-11 ownership and add08-12 persisted-issue evidence. CC-03/04 remain compound through renewal/cancellation; CC-05 retains Phase9 ownership. Review records a minor closed-history acceptance wording follow-up for08-16 and the excluded, unreproduced Browser6 timeout. No speculative locking changes were made.

## Verification

Strict `.local/phase8-12-verified` confirms242 distinct passing backend cases, including9 real SQL scenarios, with no skips:225 focused units,3 document units,4 issue/template SQL cases,3 commercial stale-proof/retained Motor Trade SQL cases,6 temporal cases including1SQL, and1 full browser/SQL case.

- Browser `.local/phase8-12-browser-7/sql.trx` passed in17m40s. Actual UI capture, original issue, nine adjustment editors, proof/carrier/internal review, delivery, exact acceptance instant and adjustment issue pass. Generated response validation, unchanged prior snapshot/hash, new exposure decision, GBP25 fee and no MID are asserted. Desktop1480px/mobile390px receipt screenshots were inspected in `.local/browser-evidence/commercial-capture/CoverMGA_Test_e92d801adf384495b6f111726f993854/`.
- Supplementary overlapping dated SQL `.local/phase8-12-readback-sql/sql.trx` passed in5m21s: exact before/at district sums, second-version effective date, decision-tamper rollback and current issuing-grant replay. It is not double-counted.
- 389 root,172 web,71 contract and4 source-ledger checks pass. Current backend build has zero warnings/errors; production web build/typecheck and final lint pass. OpenAPI lint passes with70 documented warnings. Exact reports and final paths are in08-12-REVIEW.md. `git diff --check` is clean.

## Self-Check: PASSED

Implementation and evidence artifacts exist; strict accounting and actual saved browser readback are positive. No unresolved HIGH/CRITICAL finding. Human/assistive UAT, hosted CI and Docker execution are not claimed. Sales-funnel source, live demo processes/data and persistent keys are preserved. Continue08-13 under the approved sequential autonomous workflow.
