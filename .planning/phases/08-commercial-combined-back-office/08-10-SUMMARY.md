---
phase: 08-commercial-combined-back-office
plan: '10'
status: complete
subsystem: commercial-policy-read
requires: [08-09]
provides: [scoped-commercial-exposure-reads, commercial-policy-tabs, commercial-issued-history, agency-exposure-privacy]
affects: [08-11, 08-12, 08-14, 08-16]
requirements-completed: []
completed: 2026-09-20
---

# 08-10 — Commercial policy views and scoped exposure

Implementation commit: `1b6a4cc`.

The commercial policy record now renders nine tabs from the selected immutable source, with exact property/wage/BI amounts, financial transaction, requested documents and issued history. Three real exposure GET routes provide dated observations with current subject authorization and capability-shaped privacy.

## Delivered

- `CommercialExposureRules.Observe` selects dated issued contributions and coherent limit pins, including zero outside active cover. `CommercialExposureLock.ReadAsync` takes the shared writer resource before retained parent locks. `CommercialExposureReadModel.ReadAsync` returns exact owned source/hash and a complete-book assessment; bound quotes cannot double count themselves.
- `CommercialExposureEndpoints.MapCommercialExposure` and DI implement quote/policy/draft GETs. Both E/K cutoffs or neither are accepted; repeated/unknown/filter parameters and future knowledge are rejected. All responses are non-cacheable. Current internal permission or current agency-sharing scope is checked before disclosure. Agency DTOs omit aggregate totals/counts/limits/pins and foreign identities.
- Closed exposure schemas/OpenAPI document observed/effective/known times, coverage/outcome, source provenance, unavailable states, bounded rows and18-digit monetary aggregates. No migration was needed.
- `CommercialPolicySections`, `CommercialExposurePanel` and the commercial record render actual selected-version data. Exact penny arithmetic computes property totals and largest-location demo MEL. Scoped agency/client/source links and History version selection work. Shared history accepts the explicit commercial snapshot union; Motor Trade is retained.
- All60 source controls are reconciled:50 invalid cross-product fallbacks removed,3 actual navigation controls,7 explicitly owned downstream. The298 display occurrences retain their original ownership and reviewed source mapping, including explicit uncaptured/later-phase boundaries. No sampled balance, employee count or wording author is fabricated.
- `08-10-DECISIONS.md`, `08-10-REVIEW.md`, `08-10-SOURCE-MAPPING.md` and the exposure/permission design docs retain the final contracts and evidence.

## Verification

Strict `.local/phase8-10-final` accounting verifies30 unique passing backend cases, including7 real-SQL scenarios, with no skips:

-23 pure exposure/observation cases: `.local/phase8-10-unit-final/unit.trx`.
-3 commercial read/API SQL cases: `.local/phase8-10-read-sql-v3/sql.trx`,2m39s. Exact E/K source/hash, scheduled/expired contributions, independent-agency whole-book totals, bound-quote parity, actual broker cookies and current revocation, foreign quote/policy/draft404s, agency aggregate privacy, owned draft unavailable state, rejected filters and unchanged assessment.
-3 retained Motor Trade temporal/history SQL cases: `.local/phase8-10-retained-mt-sql/sql.trx`,1m35s.
-1 full SQL-backed browser case: `.local/phase8-10-browser-92b76064-0850-4ad7-931c-4682eff9218c/sql.trx`,4m47s. Artifacts `.local/browser-evidence/commercial-capture/CoverMGA_Test_10e796995f244a168647d8317efa2457/` include report, snapshots, validated exposure observations and inspected screenshots. All nine tabs at1480/390px, exact location/wage/BI amounts, issued history, scheduled/active/expired/no-cover dates, source identity and persisted reload pass without page overflow; retained Motor Trade capture route also passes.
-387 root tests,166 web tests,4 source-ledger checks and71 contract checks pass. OpenAPI validates422 operations with56 existing warnings. Final typecheck, lint and production web build pass; backend build has zero warnings/errors. `git diff --check` is clean.

Earlier diagnostic runs are excluded from strict results. Their fixture and browser date-input issues were corrected before the evidence above.

## Downstream ownership

08-11/12 supplies commercial changed-draft projection and issue. Until then, the scoped draft exposure route explicitly reports projection unavailable with its retained source identity.08-14 owns the issued commercial cancellation browser journey; this plan covers its pure dated selection/label rules. Phase9 owns generation/delivery/incidents, Phase10 cash balances and Phase11 configuration administration. No human/assistive UAT or hosted CI is claimed. The live demo, persistent keys and sales funnel were preserved.

## Self-Check: PASSED

Named implementation, tests, source reconciliation and review artifacts exist. Current-source strict results are positive. No unresolved HIGH/CRITICAL review finding. Continue08-11 under the approved sequential autonomous workflow.
