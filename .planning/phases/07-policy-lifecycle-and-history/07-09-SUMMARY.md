---
phase: 07-policy-lifecycle-and-history
plan: '09'
status: complete
completed: 2026-09-19
requirements: [POL-04, POL-09]
requirements_completed: []
---

# 07-09 — Signed financial components and locked posting periods

The adjustment/accounting foundation in commit44b9e02 is now completed by the cancellation decision and issue work in07-14. Original positive and negative components retain identities, UTC intervals, original parties and settlement snapshots. Returns use exact London calendar-day SQL calculation and enforce one return per original component, complete movement set and balanced sealed posting. Retained fees are not charged again; cash paid remains zero.

The earlier measured adjustment, accounting-period locking/fallback and immutable-history evidence is retained in07-09-PROGRESS.md. Cancellation was correctly deferred until13/14 supplied real approved decision provenance; no fake quote-rating cycle was introduced.

New closure evidence is the18-case clean gate in .local/phase7-14-clean-gate (4unit/14SQL), especially guards-sql and adjusted-sql. Both actual products issue an adjustment and then a cancellation against that posted ledger. Negative Combined premium movements produce positive reversals, absolute returns remain bounded by originals, all original component bytes remain unchanged, and signed totals match the retained preview. Post-calculation SQL mutations of party, amount, interval and original lineage fail atomically. Exactly-once and competing issue/cancellation races preserve one additional journal.

See07-14-SUMMARY.md for exact report counts, historical failures, migrations, browser/contract validation and127 preserved demo hashes. These reports overlap with07-14 and must not be counted twice. No sales-funnel changes or actual money movement. Phase-wide requirements remain subject to07-15/16 completion.
