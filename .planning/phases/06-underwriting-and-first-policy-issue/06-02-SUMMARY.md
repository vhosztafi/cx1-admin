---
phase: 06-underwriting-and-first-policy-issue
plan: '02'
status: complete
completed: 2026-09-16
implementation_commit: 625bf90
requirements_completed: []
---

# 06-02 — Underwriting rules and additive storage

Implemented the three plan tasks in core/domain scope. Commit `625bf90` contains closed runtime configuration validation, exact rating rules, independent authority/section/source predicates, three hash partitions, explicit refresh/proof-stage gates, configuration/grant/cycle/rating/submission/referral records and additive SQL migration/seed.

## Delivered behavior

- GBP decimal factors, independent claims/valeting/young-driver/NCB adjustments, selected tools, approved agency minimum override and commission; London civil annual/short-period arithmetic including leap years, partial days and daylight saving. Out-of-range input/amounts fail.
- Every authority dimension is required. Internal profiles cannot expand binder dimensions; salvage/breaking remains explicit. Source UW-09/UW-22 and claims/convictions create independent review needs. Requested sections require explicit selected/not-selected choices, valid product/amounts and current unique premises.
- Pricing identity includes retained quote/revision/owner/config pins. Stable item order is canonical. Terms and assurance have separate hashes; proof review changes assurance without a circular pricing/terms invalidation. Refresh requires current same-product editable context and never authorises an in-place history rewrite.
- SQL composite keys constrain retained revision ownership and rating work/rule/hash/attempt identity. Unique sequence/operation identity, checked JSON/intervals/exact totals and immutable provenance/history triggers protect direct writes. Configuration retirement and grant revocation cannot be silently reversed.
- One-time fictional seed publishes two additional Motor Trade product versions,2rating rules,2binders,4authority profiles and4internal grants. It preserves old versions, passwords, approved agency terms and operator retirement/revocation. No runtime endpoint was opened by this slice.

## Migration gate

Generated and inspected migration `20260916184737_UnderwritingCoreStorage`, designer and snapshot. SQL review at `.local/phase6-02-migration.sql`: new nullable quote pointer, new tables/keys/checks/triggers; legacy quote-state check deliberately expanded. No table/data reset or historical snapshot rewrite.

Fresh SQL and retained Phase5 upgrade tests passed. Applied with documented initialize-demo to `CoverMGA_Demo`; migration history verified. Before/after `.local/phase6-02-preservation-before.txt` and `-after.txt` are byte-identical counts/SHA256 evidence:258quotes,1619revisions,16evidence files,38associations,9agency terms,3original product versions and25credentials. Initialization log `.local/phase6-02-demo-initialize.log`. No browser gate preceded migration application.

## Verification

- Red tests first identified missing rating/authority implementation; focused rules passed after implementation.
- Full `dotnet test backend/BackOffice.slnx --no-restore --logger trx`:696passed (582unit +114integration/API),0failed/skipped. Integration duration10m37s. Fresh results `.local/phase6-02-backend-20260916-final`; `assert-test-results.ps1` passed696minimum/87realSQL scenarios.
- Final pure refresh/proof-stage addition was tested separately, then the entire unit cohort reran:583passed,0failed/skipped in `.local/phase6-02-unit-final`. The initial full run preceded that independent helper addition; results are retained separately rather than merged or double-counted.
- New SQL family2/2 passed independently in `.local/phase6-02-storage-first`; full regression also includes both tests and the final guards.
- Source checks6/6 at `.local/phase6-02-source-check.log`; corrected input-map explanation to source MTS-06-Q24 `licence.issuedOn` rather than optional `testDate`.
- `git diff --check` passed; sales-funnel directory unchanged.

## Path refinements and next owner

Small configuration/hash/lifecycle modules keep pure responsibilities separate. QuoteCaptureShape already embeds generated schemas, so its source needed no redundant modification; the changed schema resources are compiled and tested. SQL guard code is a partial migration helper, preserving the generated migration/designer/snapshot.

06-03 owns trusted proposal-to-facts projection, held current identity/grant/applicability checks before replay, scenario selection, real command/worker results and explicit published-version adoption. Core predicates alone do not authorise rating/approval/issue. Decisions/proof/capacity/terms/policy storage remain the later schema slices. UWR01–07 remain pending through full Phase6 verification. Human UAT, hostedCI and Docker remain unperformed.
