---
phase: 08-commercial-combined-back-office
plan: '02'
status: complete
subsystem: capture-api-storage
tags: [commercial-combined, dotnet, sql-server, readiness, versioned-configuration]
requires: [08-01]
provides: [cc-capture-boundary, cc-readiness, cc-persistent-revisions, additive-cc-product-publication]
affects: [08-03, 08-04, 08-05, 08-06]
requirements-completed: []
completed: 2026-09-19
---

#08-02 — Persistent Commercial Combined capture

Implementation commit: `f36d124`. Contract prerequisite: `46c9b24`.

Commercial Combined can now be created, saved, reloaded and assessed through the existing scoped quote APIs and immutable SQL revisions. Its closed schema, question/reference versions and readiness are separate from Motor Trade. No completed business requirement is claimed before the UI and later underwriting workflows.

## Delivered

- `CommercialCaptureRules`: matching trusted pins, default incomplete draft, bounded canonical input, bundled CC schema, empty/duplicate risk IDs, duplicate scoped answers, loss-location ownership, UK postcode syntax and no motor registration projection.
- `CommercialCaptureReadiness`:109 source questions and item subjects, explicit unknown/missing facts, selected EL/BI/contract works, location SI/MEL/contents/activity consistency, business/date/loss details and exact adverse health dropdown meanings. Incomplete proposals remain saveable.
- `CommercialCaptureSeed`: published CC product v2, separate bundled question/reference family and appended capture/distribution settings under the existing initializer transaction. Immutable marker prevents re-grants; revoked/malformed settings and old versions remain unchanged. Approved agency terms are not extended.
- Shared APIs add CC capture and discovery through an explicit closed OpenAPI union. Existing HTTP parser and QuoteService/QuoteRevision storage already provide the required bounds, current authority, exact receipts, stale checks and immutable history, so they needed no replacement or migration. `QuoteCaptureShape` gains a bundled resource; eligibility checks the CC family and published product metadata.
- CC `canAttachEvidence` stays false and underwriting reports `canRate=false` with an unavailable-product blocker until08-05/06. No Motor Trade proof is demanded from CC.
- Source extraction preserves a source-hash-bound evidence overlay. It labels backend validation and API persistence separately from pending UI control evidence; it does not overwrite original source IDs/owners.

## Acceptance evidence

| Check | Result |
|---|---|
| Full current unit suite |906 passed,0 skipped/failed;23 CC cases — `.local/phase8-02-final/unit.trx` |
| Native SQL/API and retained MT checks |9 passed,0 skipped/failed;6 CC scenarios plus3 MT scenarios — `.local/phase8-02-final/sql.trx` |
| Strict unique TRX accounting |915 successful cases including9 real-SQL scenarios — `scripts/assert-test-results.ps1` |
| Full source/contract suite |376 passed,0 skipped/failed — `.local/phase8-02-source-final.log` |
| Final evidence-ledger checks |4 passed; repeated extraction preserves exact generated evidence — `.local/phase8-02-source-evidence.log` |
| OpenAPI |422 operations, exit0 — `.local/phase8-02-openapi.log` |
| Review |Passed —08-02-REVIEW.md; no unresolved HIGH/CRITICAL finding |

SQL exercises create/save/reload/revisions, exact retry,412 stale write, invalid Motor Trade/foreign loss input, immutable row counts, forged agency/client HTTP fields, current user/client revocation before receipt replay, CC discovery, downstream unavailability, repeated initialization and operator revocation. Existing MT API/capture/catalogue behavior remains covered. Native tests use only uniquely owned CoverMGA_Test databases; the live demo database, preview and persistent keys were untouched.

RED evidence includes9 unsupported capture tests, missing readiness, the native CC product-selection failure, the OpenAPI union case, empty risk identity and7 adverse health dropdown cases. Initial compilation/resource-loading issues were corrected before accepted GREEN runs. Final runtime evidence supersedes earlier intermediate outputs.

## Refinements and limits

The existing generic HTTP input and SQL revision model required no new transport class/table/migration. The source-scoped readiness class and additive initializer flag were added as feature-local outputs. No agency access is granted by schema publication. Full prototype browser capture is explicitly08-03/04; actual CC rating/referral/proof, issue/exposure and servicing remain later plans. Human UAT, hosted CI and Docker are not claimed. CC-01..05 remain incomplete at this intermediate slice.

## Next plan

Execute08-03 automatically: explicit CC wizard dispatch, proposer/history/loss fields and real desktop/390px browser save/readback. Preserve the existing MT editor and frontend-code reference.08-04 completes the remaining CC stages and interaction-driven readiness refinements.

## Self-Check: PASSED

Implementation commit exists, new named artifacts are present, final result files contain the stated successful unique cases, source identities are retained and the next plan has its required contracts and services.
