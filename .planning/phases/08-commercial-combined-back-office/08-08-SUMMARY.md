---
phase: 08-commercial-combined-back-office
plan: '08'
status: complete
subsystem: commercial-exposure
requires: [08-07]
provides: [immutable-exposure-books, dated-limit-publications, complete-source-projections, temporal-capacity-evaluator]
affects: [08-09, 08-10, 08-12, 08-13, 08-14, 08-15]
requirements-completed: []
completed: 2026-09-20
---

# 08-08 — Immutable dated commercial exposure

Implementation commit: `0f0fdc6`.

Commercial Combined has stable exposure books across binder versions, immutable dated limit publications, complete source-linked version/location projections and a pure effective/known-time capacity evaluator. Atomic policy issue remains closed until08-09 integrates these records with the source, finance, documents and replay receipt.

## Delivered

- CommercialExposureBook, CommercialExposureBinder, CommercialExposureLimitVersion, CommercialExposureVersion and CommercialExposureLocationRecord configured by ConfigureCommercialExposure. New migration20260920045506_CommercialExposureStorage, designer and current snapshot add five tables without changing historical migrations. Compound source links, verified temporal metadata/hash, canonical districts and append-only guards preserve history.
- CommercialExposureProjection.AppendAsync requires a held transaction. The header INSERT validates all location identities/districts/property amounts and materializes the complete child set atomically. ReadAsync/ LimitsAsync read immutable exposure tables; no mutable foreign Policy join. A rollback leaves neither header nor children. Forged cancellation metadata is rejected.
- CommercialExposureRules.Snapshot/Assess use active term/version winners, complete own-policy replacement intervals, every relevant effective/known/limit boundary, property-only arithmetic and distinct-policy counts. Scheduled/expired/cancelled winners contribute zero. Closed missing/ambiguous/exceeded blockers pin selected limit ID/hash and expose proposed/other/resulting totals and headroom.
- Explicit SupersedesLimitId supports immutable same-scope publication changes while preserving prior knowledge and outside-interval applicability. Missing or competing applicable limits block. CommercialExposureSeed adds missing stable book/binder mappings and a fictional £40m default; repeated initialization preserves configured publications. Later binder versions do not silently extend limit validity.
- docs/design/COMMERCIAL-EXPOSURE.md defines records, operations, publication JSON, selection and downstream API ownership. Source display rows retain pending08-10 status with storage evidence only; denominator166/109/60/298/7 unchanged.

## Verification

Strict `.local/phase8-08-final` accounting passes24 unique backend cases, including4 real-SQL scenarios, no skips:

-19 pure unit cases: `.local/phase8-08-expanded-unit/unit.trx`. Scheduled/current/expired/cancelled exposure, future MTA, early renewal, same-time sequence/slice selection, backdated district move, distinct-policy totals, complete future own overlays, zero property, dated limits/replacement expiry/ambiguity and malformed inputs.
-4 integration-project cases: `.local/phase8-08-final-sql/sql.trx`,1m34s. Independent parity with PolicyTemporalSelector at effective/known boundaries and adjacent ticks; real accepted-commercial source/child/hash/date/order guards, rollback, duplicate/orphan/mutation rejection, successor-binder same-book readback and lower dated publication; retained MT policy-storage rejection and accepted-era migration preserving quote bytes/credentials.
-1 additional real-SQL publication case: `.local/phase8-08-limit-sql/sql.trx`,28s. Final canonical default/district checks, wrong replacement scope, unknown parent, wrong-product book, held transaction and missing-only initialization. Final model has no pending changes. This final narrow constraint tightening leaves earlier valid projection and MT paths unchanged.
- Backend build zero warnings/errors: `.local/phase8-08-final-build.log`. Four source-ledger tests pass: `.local/phase8-08-source-tests.log`. git diff --check passed. No frontend/public API changed, so no repeated browser/build suite was needed.

Initial missing-implementation unit RED was followed by15 then19 passing cases. Two SQL fixture corrections retained existing protections: transaction CreatedAt must equal ProcessedAt, and successor binder publications must not overlap. Only successful reports enter final accounting. SQL cancellation issuance itself stays with the later cancellation plan; cancellation temporal behavior and forged-source rejection are verified here.

## Review and next ownership

08-08-REVIEW.md passed with no unresolved HIGH/CRITICAL finding. Live demo database, services, immutable history and keys preserved. No sales-funnel edits, real provider/email, human UAT, hosted CI or Docker run. Full phase acceptance/preservation remains08-16.

Continue08-09: common book lock before scoped row locks, current scope before exact receipt replay, complete interval capacity evaluation under the held fence, atomic source/exposure/finance/outbox/document writes, selected-cover CC documents and actual issue browser/API/concurrency tests. CC-03/04 and compound CC-05 remain open until their owning workflows are complete.

## Self-Check: PASSED

Implementation and current-source evidence exist. Strict accounting contains only executed successful cases. Downstream API, display and servicing boundaries remain explicit.
