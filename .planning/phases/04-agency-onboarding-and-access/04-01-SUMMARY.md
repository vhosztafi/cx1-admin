---
phase: 04-agency-onboarding-and-access
plan: '01'
subsystem: agency-contracts
tags: [openapi, sql-server, agency, permissions, invitations]
requires: [03-06]
provides: [source-complete-agency-contracts, agency-storage-and-security-protocols]
affects: [04-02, 04-03, 04-04, 04-05, 04-06, 04-07, 04-08]
requirements-completed: []
completed: 2026-09-14
duration: 20min
key-files:
  created: [scripts/openapi-agencies.mjs, tests/agency-contracts.test.mjs, contracts/examples/agency-option-targets.json, docs/design/AGENCY-SOURCE-COVERAGE.md]
  modified: [scripts/generate-openapi.mjs, contracts/openapi.json, contracts/examples/agency-option-mapping.json, docs/design/DATA-MODEL.md, docs/design/PERMISSIONS.md, docs/design/LIFECYCLE.md, docs/design/reviewed-api-controls.json, docs/design/api-control-map.json, docs/design/ACCEPTANCE-MATRIX.md, tests/api-contracts.test.mjs]
---

# 04-01 — Agency contracts and data protocols

Both planned tasks complete as a contract/design slice. Production commit: **b9d1091e** (`feat(04-01): define source-complete agency contracts and storage protocols`). No runtime route, migration, storage behavior or business requirement is claimed implemented by this plan.

The final agency generator module preserves existing operation identities and adds strict draft/checklist, evidence file/check, user/invitation, proposal/terms, notification, permission and safe sharing contracts. All 24 fixed wizard option families have source-exact labels and distinct typed values, including territory UK/GB/NI, portal-only correspondence, unchecked/confirmed/restricted arranging permission, ownership state and commercial mode/value pairs. Dynamic manager/product discovery uses stored IDs. Source review includes aguser/invite role dialogs and implied actions/reads, with later finance/insurance/task owners explicitly recorded.

Draft answers remain optional and bounded, while verified evidence and activation remain server-owned. Full terms require selected mode values and reject invalid money/basis points. Product write DTOs exclude row/agency/terms IDs. State commands create pending requests with ID-only 202 responses and independent decisions. Staged invitations have no issuance/expiry/delivery, issued invitation validity is independent from notification delivery, and secret reveal is development-only, internal, CSRF-protected and uncached. Sharing DTOs exclude internal evidence, rule identifiers, hidden counts and fabricated balances.

Canonical DATA-MODEL/PERMISSIONS/LIFECYCLE now specify records, keys/FKs, bounds/checks, immutable versions, scope, last-admin protection, real-time 14-day expiry, protected delivery, token consumption transaction and Agency -> ordered users -> children lock order. Existing Phase 3 link/match paths must join that order in 04-06. Proposal creation deliberately does not advance the agency base. Distribution eligibility does not publish rating definitions. Source-mapped agency user/permission actions now use dedicated agency routes instead of internal identity administration.

## Verification

- `node scripts/generate-openapi.mjs`: 320 operations generated.
- `node scripts/build-api-control-map.mjs`: 949/949 reviewed controls, complete.
- `node --test tests/agency-contracts.test.mjs`: 11 passing agency contract tests.
- `node scripts/validate-contracts.mjs`: OpenAPI lint valid; **74 tests passed, zero failures/skips**; 949 controls, 5 conditional rules, 320 operations. Final log `.local/phase4-contract-validation.log`.
- `git diff --check`: passed (line-ending normalization notices only).
- Inline review corrected a duplicate inherited manager query and removed internal eligibility-setting IDs from safe product projections. Strict compilation caught and corrected conditional-schema required-field declarations.

Backend/browser tests were not rerun: this slice changes contracts and design only, with no deployed behavior. SQL authorization, persistence, race/rollback, notification replay and browser flows remain required in their owning implementation plans. No human UAT, hosted CI or Docker verification inferred.

## Deviations and continuation

Added a separate source-coverage document and option-target fixture, and updated the existing API contract test to validate all source choices. These are necessary to make the requested source completeness reviewable. The broad generic PLAN done clauses about stored state and browser checks apply when runtime implementation arrives; they cannot be satisfied by schema tests and are explicitly retained for 04-02 through 04-08.

Next: **04-02**, persistent agency draft rules/storage/API and six-step list/detail/wizard. Preserve existing Agency IDs and seed data, keep frontend-code read-only, use generated owned SQL test databases, and follow the Phase 4 reviewed UI specification. AGY-01 through AGY-05 remain pending overall; no phase completion claimed.
