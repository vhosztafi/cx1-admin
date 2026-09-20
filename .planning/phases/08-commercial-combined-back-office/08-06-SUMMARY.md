---
phase: 08-commercial-combined-back-office
plan: '06'
status: complete
subsystem: commercial-underwriting
tags: [commercial-combined, referrals, evidence, authority, sql-server, browser]
requires: [08-05]
provides: [cc-source-referrals, cc-typed-proof, cc-internal-decisions, cc-underwriting-tab]
affects: [08-07, 08-09, 08-12, 08-13, 08-15]
requirements-completed: []
completed: 2026-09-20
---

# 08-06 — Commercial Combined referrals, evidence and authority

Implementation commit: `5a7fb44`.

Commercial Combined rating now persists prototype referrals with explicit internal-review, outside-appetite and carrier-required dispositions. The new Underwriting tab shows current grant/binder limits, scoped documentary requirements, decisions, condition resolution and evidence history. No synthetic Motor Trade risk or proof is used. Internal decisions cannot override outside-appetite or binder limits, and carrier/terms/acceptance/issue remain separate downstream features.

## Delivered

- CommercialReferralRules.SourceReferrals(JsonElement) and AssessAuthority(JsonElement, JsonElement, decimal) evaluate all21 source referral branches plus deliberate CC product isolation. Exact location sum/MEL boundaries retain stable location IDs; published limits cover premium, property, liability, BI and contract works. Numeric height, recorded flood zones and construction cannot be suppressed by contradictory separate answers. Conservative demo interpretations are documented in UNDERWRITING-CONTRACTS.md.
- CommercialEvidenceRules.Requirements and Condition implement eleven closed documentary purposes/conditions. Six typed risk groups cover property/location/liability/wage/BI/business. Five separate source groups cover claims experience, electrical inspection, alarm maintenance, safety and conditional structural surveys. Each location's proof is independent; screening, attachment, review, condition satisfaction and approval remain separate.
- Shared held scope and grants dispatch explicitly to Commercial Combined configuration. Current role/scope/grant and current cycle/revision/runtime pins precede receipt replay; exact quote/child versions precede effects. Query, decline, reopen and internal conditional decisions use the existing atomic receipt/history services. Revising a proposal supersedes earlier referrals. Withdrawn proof restores blockers; both resolution and later satisfaction recheck screening.
- Migration20260920024535_CommercialUnderwritingEvidence extends the shared closed purpose/condition checks and adds TR_UnderwritingEvidenceAssociation_CommercialSubject against the cycle's immutable JSON revision. Existing append-only/composite-owner protections remain. No live database migration/reseed or production authority grant was performed.
- CommercialUnderwritingContext reuses the shared confirmed/retry-safe decision/evidence UI through a typed proposal union. Source-specific conditions retain current location/wage targets; MT-only choices are excluded. Read-model blockers retain target IDs, including otherwise identical certificates at different locations. The UI shows pending whole-book exposure rather than fabricated headroom.
- The six08-06 evidence controls and22 owned branch/isolation rows have source evidence. Denominators remain166 capture controls,109 questions,60 policy controls,298 display items and7 supplemental fields. CC-03/04 remain open for carrier/terms/exposure/issue; CC-05 remains partial through Phase9.

## Acceptance evidence

-68 backend units:40 CommercialReferral,20 CommercialRating and8 retained condition/authority tests. Report .local/phase8-06-current-unit/unit.trx.
-8 native SQL cases:normal CC proof/condition flow, outside appetite, binder limit and lower grant; four retained MT cases covering both products' condition withdrawal, bulk rollback/history and independent review. Report .local/phase8-06-source-sql/sql.trx, duration4m41s. The normal CC case independently attaches/reviews the five source-document purposes, rejects cross-purpose satisfaction, resolves then withdraws condition proof, rejects stale/foreign/unscreened inputs, rechecks revocation before replay and rejects old-cycle replay after location removal.
-1 actual Chrome/native SQL case: .local/phase8-06-browser-218ef46a-05e5-43b4-8520-e2f21368f68f/sql.trx, duration1m53s. Final report/screenshots/referrals/assessment: .local/browser-evidence/commercial-capture/CoverMGA_Test_e9474b96ed3f403cb8e1ba2b1d6398e0/report.json. It completes all109 source capture questions, rates through the actual API, validates closed assessment/rating/referral schemas, renders all five source-document labels, persists a location-specific query, reloads at desktop/390px and retains MT creation. Screenshot inspection confirms the retained shell and mobile layout; no horizontal document overflow.
-Strict accounting .local/phase8-06-final:77 unique passing cases,9 real SQL,zero skipped.
-161 frontend tests (.local/phase8-06-current-web-tests.log);380 root/source/contracts tests (.local/phase8-06-source-contracts.log);4 final source-ledger checks (.local/phase8-06-source-evidence.log).
-Current lint,typecheck,build pass: .local/phase8-06-current-lint.log, .local/phase8-06-final-typecheck.log, .local/phase8-06-source-build.log. OpenAPI422 operations valid with57 existing warnings (.local/phase8-06-current-openapi.log). git diff --check passes.

## Review and deviations

Sequential inline execution; no agents. Necessary path refinements include the partial UnderwritingEvidenceReadModel, QuoteConditionResolutionService, shared grant/rating worker/read model, generated condition schemas and shared UI components. No new DI or endpoint family was needed.

RED evidence began with missing rules symbols (.local/phase8-06-red.log). Source review caught generic proof over-grouping and added independent source purposes. Actual response validation caught duplicate certificate blockers and added their location IDs. Browser cleanup exposed background routing races; the harness now closes the browser, drains tracked route promises and ignores only expected closed-route failures during cleanup. All in-test routing errors still fail. Earlier incomplete/intermediate runs are superseded by the final reports above.

08-06-REVIEW.md passed with no unresolved HIGH/CRITICAL finding. Native SQL tests used independently owned CoverMGA_Test databases; the Phase7 live API/web/DB/history/keys and frontend-code were preserved. Human business/assistive UAT,hostedCI and Docker were not performed. Full phase regression/restart/preservation remains08-16.

## Downstream handoff

08-07 must implement CommercialCapacityRules, subject-specific carrier response applicability, current internal approval plus independent proof, CC terms/templates, delivery and separate acceptance. Existing ReadMotorTrade gates remain on those consumers and quote submission; do not simply remove them. Commercial condition parsing currently accepts only the eleven documentary alternatives; signed-statement/carrier/terms definitions need deliberate extension. CapacityAuthority's CC branch currently refuses all source outside-appetite/carrier-required extents. Real whole-book district exposure remains08-08/09, with no issue capability enabled here. Explicit business-demo grants/fixtures remain08-15.

## Self-Check: PASSED

All owning artifacts exist, reviewed implementation is committed, and current-source acceptance evidence above passes.
