---
phase: 10-accounting-and-insurer-reporting
plan: '16'
subsystem: finance
tags: [earned-premium, posting, retained-demo]
provides:
  - Atomic earning materialization in first issue, servicing and cancellation writers
  - Explicit local demo backfill command with additive migration and idempotent counts
requirements-completed: []
completed: 2026-09-25
---

# Phase 10 Plan 16: Posting and Historic Backfill

First issue, servicing adjustment and cancellation writers call the shared earning scheduler after their journal is posted within the same SQL transaction. The scheduler uses only the posted premium component's saved signed amount and coverage. A repeat validates the source hash and schedule rather than rewriting rows. The dedicated `--backfill-finance-earnings` command is restricted to Development and the verified `CoverMGA_Demo` target.

Two focused native SQL cases passed with zero skips: immediate first-issue slices, simulated legacy backfill, repeat and tamper rejection; and immediate negative cancellation slices with exact signed sum. The API build passed without warnings.

Before retained migration/backfill, `.local/phase10-gap16-preservation/baseline` captured 198 tables and 215 files. The first run inspected 72 posted premium components and inserted 879 slices. Preservation verified 98,042 original rows and 215 files unchanged. A second capture included the new table (199 tables); the second run inspected the same 72 components, found 879 preexisting slices and inserted zero. Preservation verified all 98,921 then-current rows and 215 files unchanged. The demo database, keys and documents were retained.
