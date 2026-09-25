---
phase: 10-accounting-and-insurer-reporting
plan: '14'
subsystem: finance
tags: [refunds, agency-scope, browser]
requires:
  - phase: 10-accounting-and-insurer-reporting
    provides: Saved refund decisions and accounting workspace
provides:
  - Agency-scoped saved refund list, filtered count, approval progress and direct ID navigation
  - Browser readback of retained saved count and link
affects: [phase-10-verification, finance-demo]
key-files:
  modified:
    - backend/src/BackOffice.Infrastructure/Finance/FinanceRefundService.cs
    - backend/src/BackOffice.Api/FinanceRefundEndpoints.cs
    - apps/backoffice/components/finance/overview.tsx
    - apps/backoffice/components/finance/refunds.tsx
    - contracts/openapi.json
    - scripts/verify-finance-workspace-browser.mjs
requirements-completed: []
completed: 2026-09-25
---

# Phase 10 Plan 14: Saved Refund Attention

The new refund page authorizes the selected agency before a SQL-scoped count and page. It filters saved request states, orders by request time and ID, and returns exact IDs with approval progress. The overview displays the pending total and links the first saved ID; the Refunds tab has a paged queue. Agency changes and failed reads clear old count and detail state.

Focused native SQL refund tests passed 1/1 with zero skips, including pending, rejected and approved transitions, invalid state, unknown agency and revoked finance authority. API and integration builds passed. Frontend tests passed 214/214; typecheck and lint passed; strict API contract tests passed 45/45. The live finance workspace collector passed 22 checks against the current preview, including an API-matched saved refund total and direct-link binding (`.local/phase10-12-browser/browser-report.json`).

The retained demo still has zero refund requests. Positive pending-to-approved browser count and retained payment restart readback are assigned to 10-18; the source binding remains partial until that proof is captured. The existing port 3100 preview and retained records were preserved.
