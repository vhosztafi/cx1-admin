---
phase: 08-commercial-combined-back-office
status: passed
verified: 2026-09-21T11:54:24Z
score: 3/3 roadmap success criteria
---

# Phase 8 verification

Passed locally. All 16 plans have completed summaries. The complete current backend inventory, commercial aggregate, retained Motor Trade suites, source reconciliation and actual restart checks pass. Verification was performed inline under the approved sequential plan.

| Roadmap criterion | Implemented behavior and evidence |
|---|---|
| Capture and issue a CC property/liability/BI risk | Closed product-specific proposal, 109 scoped questions, two-location/wage/loss editing, real rating/proof/carrier/terms/acceptance and atomic issue. Full commercial capture and issue stages validate actual API schemas and SQL readback. Commercial policy discovery and all nine valid tabs work at desktop and 390px |
| Reproducible assumed rating, authority and location aggregation | Published versioned demo configuration, exact money, owned subjects, independently reviewed proof and current authority; dated whole-book exposure with source-pinned decisions. Current SQL covers scope before replay, lowered/retired grants, stale configuration, competing district capacity, lock timeout and late rollback |
| CC servicing and operational payloads preserve shared invariants | All nine adjustment editor groups, reviewed adjustment issue, whole-risk renewal experience and linked renewal, effective cancellation and immutable history. Exact incident/document projections and applicable EL certificates are tested. Phase 9 owns actual incident logging/rendering/delivery; no MID is emitted for CC |

## Requirement accounting

- CC-01 complete: create/resume internal CC quotes through the shared agency/client/quote lifecycle (08-02/03).
- CC-02 complete: persistent typed property, protections, wages, liability, BI and losses, with scoped questions and exact readback (08-03/04/10/11).
- CC-03 complete: documented demo rating/referrals/authority and effective/known-at postcode aggregation (08-05..08).
- CC-04 complete: first issue, adjustment, renewal, cancellation and immutable selected-version views (08-09..14).
- CC-05 **partial**: 08-15 delivers exact immutable incident/document payloads and queued content. Phase 9 completes incident records, document generation/delivery and operational execution. This compound requirement remains unchecked.

## Blocking gates — all passed

Full backend: `.local/phase8-16-final-strict.log` verifies **1,511 unique passes**, **383 real-SQL scenarios**, zero skips. This is 1,091 unit plus 420 integration cases. `.local/phase8-16-final-strict/inventory-proof.json` records exact equality between the discovered and executed 420-case inventory and the unchanged current assembly hash. Full integration TRX: `.local/phase8-16-full-sequential/sql.trx`.

Commercial aggregate: `.local/commercial-suite/2026-09-21T06-55-30-182Z-e697e685-bbbb-41d1-aa17-2f3212527ab5/report.json`, five stages passed with pinned sources/binaries. Retained underwriting: `.local/underwriting-suite/2026-09-21T06-15-42-927Z/report.json`. Retained servicing: `.local/servicing-suite/2026-09-21T06-23-21-643Z-76069fa0-830a-4fa4-a7b4-82da34e1073f/report.json`, all 17 stages passed; two failed/resume reports retained and completed prefixes verified against unchanged application binaries. Fixture-based terms UI checks remain explicitly distinguished from persisted SQL checks.

Root 399 and frontend 172 pass; lint/typecheck and production builds pass. Exact logs are in 08-16-SUMMARY.md. The original 408/12 baseline and interrupted attempts are excluded. Repeated tests and aggregate overlaps do not inflate the final unique count.

Preservation: `.local/phase8-16-final-initialization/report.json` proves all 139 table fingerprints unchanged across two additive initializations. `.local/phase8-16-final-restart/compare-report.json` proves three policy graphs/12 versions, five pinned commercial exposure readings and original persistent keys unchanged after actual process restart. Retained graph hash: `d816aa0fa8ff6fc38ada5e3c906d3fd799bd70ed47c73206aa64c0cd5dcd062d`. Tests may subsequently append separate owned fictional records; the preservation claim concerns the measured initialization and restart windows.

## Source, security and limitations

08-SOURCE-INVENTORY.json retains every source ID and denominator: 166 capture controls, 109 questions, 60 policy controls, 298 display occurrences, seven supplemental facts and 27 branches. All Phase 8 rows have evidence. The three future policy controls and 34 future display occurrences retain their already-approved Phase 9/10/11 owners. Counts are source coverage, not assertions or test counts.

All plan threats are covered through their completed summaries and final current gates. Current identity, agency/client/product/subject scope and immutable provenance precede replay disclosure. Strict input/CSRF/no-store, ETag/lease fences, foreign-subject rejection, lost-response recovery, rollback and concurrency tests pass. Sign-in cannot natively GET credentials before hydration; browser evidence includes a meaningful failing assertion and passing no-JavaScript/hydrated checks. No unresolved HIGH/CRITICAL finding remains.

Desktop/390px screenshots were inspected, including current adjustment receipts; mobile tables scroll within their container. Automated keyboard, reload and error-recovery checks pass. Human business/assistive-technology UAT, hosted CI and Docker runtime remain unperformed. Native SQL Server 2022 supplies the database evidence.

No sales-funnel changes or real external delivery/payment occurred. Imported AG-DEMO-QUOTES retains its incomplete historical terms unchanged; the supported commercial demo uses approved AG-0000154. Document requests and cancellation consequences remain queued until their Phase 9 handlers exist. Posted credit is not a cash refund. The complete MVP is not yet finished: proceed to Phase 9.
