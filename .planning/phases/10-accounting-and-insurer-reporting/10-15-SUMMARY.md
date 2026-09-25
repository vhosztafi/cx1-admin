---
phase: 10-accounting-and-insurer-reporting
plan: '15'
subsystem: finance
tags: [earned-premium, sql-server, immutable-schedule]
provides:
  - Additive FinanceEarningSlice model and migration
  - Exact signed London-month earning allocation and source hash verification
requirements-completed: []
completed: 2026-09-25
---

# Phase 10 Plan 15: Pinned Monthly Earning Schedule

Each posted `premium` component can now produce immutable monthly slices from its saved signed pence and half-open UTC coverage interval. London month boundaries account for daylight saving. Integer division allocates every month except the last; the final month receives the exact remainder. Each row pins the component, obligation, transaction, agency, policy, coverage, posting timestamp and source hash. Repeated materialization validates the saved rows and rejects a mismatch. No journal is changed.

Six focused unit cases passed with zero skips, covering leap February, London spring clock change, positive and negative amounts, final-penny allocation, invalid coverage and source-hash change. A focused native SQL case passed with zero skips and verified first issue, legacy backfill, idempotency and altered-slice refusal. The additive migration was generated as `20260925160442_FinanceEarningSlices`.

Retained migration/backfill preservation is recorded with plan 10-16.
