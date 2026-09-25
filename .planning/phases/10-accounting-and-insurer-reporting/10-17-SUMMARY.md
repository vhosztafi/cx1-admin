---
phase: 10-accounting-and-insurer-reporting
plan: '17'
subsystem: finance
tags: [earned-premium, agency-scope, browser]
provides:
  - Scoped earned-premium period API with source/month drilldown and pinned cutoff
  - Overview earned-premium measure and saved basis disclosure
requirements-completed: []
completed: 2026-09-25
---

# Phase 10 Plan 17: Earned Premium Read Model

The new `GET /finance/agencies/{agencyId}/earned-premium?periodId=...` route authorizes the current finance reader before loading any agency source. It validates every expected month, signed pence, source hash, agency and component identity against the pinned schedule. Its total reconciles to source/month rows. Open periods capture one read timestamp; closed periods use their saved `SourceCutoff`. Partial boundary months are apportioned from the pinned slice and UTC overlap. A missing or altered schedule fails visibly rather than producing a false zero.

The accounting overview fetches that scoped response per agency and period, clears stale values, and shows exact earned premium, period, as-of timestamp, basis and a source/month disclosure. The OpenAPI route and generated TypeScript model are updated. A native SQL case passed with zero skips for first-issue reconciliation, unknown agency, tamper refusal and current role revocation. Two servicing SQL cases passed with zero skips for positive and negative adjustments and rollback. Six unit earning math cases, 45 strict API contract tests, 214 frontend tests, typecheck and lint passed. The live saved finance workspace collector passed 23 checks, including nonzero earned premium, component drilldown and reload (`.local/phase10-12-browser/browser-report.json`).

The isolated SQL fixture's period-close trigger correctly rejected a synthetic closed-period setup without genuine close evidence. The closed cutoff branch is implemented, but a legitimate closed-period regression remains to be exercised in the final gate or later accepted data. The source binding remains partial until final review.
