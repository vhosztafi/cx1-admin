---
phase: 08-commercial-combined-back-office
plan: '11'
status: complete
subsystem: commercial-servicing-capture
requires: [08-10]
provides: [typed-commercial-adjustment-proposals, persisted-commercial-editors, commercial-proposed-slices, safe-editor-recovery]
affects: [08-12, 08-13, 08-14, 08-16]
requirements-completed: []
completed: 2026-09-20
---

# 08-11 — Typed commercial adjustment drafts

Implementation commit: `663cf1e`.

Commercial policies now create and resume persisted adjustment drafts with nine typed editor groups, saved issued/proposed comparisons and deterministic effective slices. Saving a draft leaves issued cover, exposure, financial records and documents unchanged.

## Delivered

- `CommercialServicingProposalRules.Assess` dispatches from the immutable product and reuses closed capture validation, canonical proposal input and London date rules. Ten prefixed kinds cover location/property/business/BI/liability/wage/loss/cover/insured/declarations. Stable subject ownership, duplicate/deleted/reused identities, cross-product input and client transfer are checked; replacement explicitly clears omitted values without confusing zero/false with unknown.
- Shared `ServicingDraftService` requires the current commercial base, retains current scope before replay and preserves ETag/lease fencing. The editor reports commercial catalogue pins and explicitly serializes the schema-required nullable question ID. Context includes trusted product code for UI dispatch. No migration is needed; revisions use existing append-only JSON storage.
- `CommercialServicingWorkspace`, `CommercialChangeEditors` and `commercial-servicing.ts` reuse commercial capture fields/dialogs. Policy history opens actual adjustment creation/resumption. Matching saved comparisons, revision-pinned local edits, lease recovery, read-generation fencing and exact command retries prevent silent overwrite or duplicate revision creation after an uncertain response.
- Generated servicing/OpenAPI contracts document the closed product union. `SERVICING-CONTRACTS.md`, `08-11-DECISIONS.md` and `08-11-REVIEW.md` retain final behavior and evidence. All four owned policy controls point to the actual persisted flow; source denominators and future ownership remain unchanged.
- Commercial rating remains explicitly unavailable until08-12 installs its own pipeline and actual draft exposure projection. Renewal/cancellation remain08-13/14. Both Motor Trade editor routes and service workflows are retained. No sales-funnel changes or live demo database/process/key changes were made.

## Verification

Strict `.local/phase8-11-verified` verifies83 unique passing backend cases, including5 real-SQL scenarios, with no skips:

-78 units:15 commercial projections,29 commercial capture and34 retained servicing cases; `.local/phase8-11-projection-unit-final/unit.trx`.
-4 current SQL cases: commercial persistence/lease/ETag/current replay scope plus retained Motor Trade storage and HTTP; `.local/phase8-11-sql-current/sql.trx`,2m9s.
-1 full browser/SQL case: `.local/phase8-11-browser-c54ee05a-c398-4b7f-aaf5-846372586b8a/sql.trx`,6m34s. Actual response contracts, nine editor groups, stable location/wage/loss add-edit-remove, explicit clearing, exact stored proposal/slices, negative HTTP cases, competing revision and revoked lease recovery, committed lost response followed by denied and successful exact retry. Issued snapshot/hash and full district observation stay equal. Artifacts `.local/browser-evidence/commercial-capture/CoverMGA_Test_bc19a7bc68ac437db1ec591c70399963/`; desktop/390px screenshots inspected.
-389 root,170 web,71 contract and4 source-ledger checks pass. OpenAPI lint retains its56 existing warnings. Backend build has zero warnings/errors; final production web build/typecheck and lint pass. Detailed log paths are in the review. `git diff --check` is clean.

## Self-Check: PASSED

Implementation and evidence artifacts exist and current-source strict accounting is positive. No unresolved HIGH/CRITICAL review finding. Human/assistive UAT, hosted CI and Docker execution are not claimed. Continue08-12 under the approved sequential autonomous workflow.
