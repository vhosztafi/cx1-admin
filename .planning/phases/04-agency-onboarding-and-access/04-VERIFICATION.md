---
phase: 04-agency-onboarding-and-access
status: passed
verified: 2026-09-15
---

# Phase 4 goal verification

**Goal achieved within its approved phase boundary:** agencies can be onboarded and their sharing boundaries enforced. All eight plans are complete. Milestone-wide AGY-03/04 remain partial for the explicitly assigned later insurance/task/finance records.

| Goal / requirement | Result and direct evidence |
|---|---|
| Incomplete agencies cannot activate; complete agencies can | Pass / AGY-01. Strict draft/complete DTOs, immutable evidence/checks and readiness rules; actual six-stage browser save/resume and independently approved lifecycle with real uploads/checks/staged user. Actual activation response-loss replay commits exactly once. |
| Users, invitations and access remain durable and scoped | Pass / AGY-02. SQL invitation issue/acceptance/resend/revoke/expiry/concurrency, last-admin rules, suspension/reactivation and current-authority tests. Real invitation acceptance and cookie login; deep-link/internal API denial; foreign-scope IDs/counts/cursors and revoked authority rejected. |
| Products, terms, access and activity are persisted | Phase scope passed / AGY-03 partial. Immutable approved versions, current/scheduled/history, product grants, exact commission/credit values, permission history and actual activity. Accounts balances/statements/exports await Phase 10. |
| Sharing reflects only allowed records | Phase scope passed / AGY-04 partial. Common safe internal preview/external context and own-agency queries. Two-agency accepted-cookie tests verify isolation and field restrictions. Actual quotes/policies/tasks await Phases 5/6/9. |
| Activation/invitations create durable, truthful notifications | Pass / AGY-05. Atomic queued work and protected payloads, lease/retry/deduplication/restart tests, persisted delivered/rejected/exhausted outcomes, and visible queued versus delivered states. No real external delivery. |
| State survives restart | Pass. Successful lifecycle agency AG-0000062, b47f31ad-0403-4b51-a615-009fd151f54c. Fifteen SQL data-set hashes unchanged after API/Next restart at 2026-09-15T06:57:36Z; fresh login, persisted agency state, both exact terms versions and Accounts reload pass. |

## Final automated evidence

- `.local/phase4-acceptance-final`: 329 passing backend cases (265 unit, 64 integration), including 57 real-SQL scenarios, zero skips. Fresh run against the unchanged verified Debug build; result gate passed.
- `node scripts/validate-contracts.mjs`: OpenAPI valid, 81 contract/design cases pass, 949 controls and 328 operations mapped. Mapping is milestone coverage, not implementation of every future operation.
- `pnpm web:test`: 30 cases pass; lint and TypeScript checks pass. Production build from the unchanged application revision was used for every browser and restart check.
- `.local/agency-suite/2026-09-15T06-53-51-892Z/report.json`: all 20 stages passed in one sequential no-reset run. Six retained foundation/client/contact/support/match journeys plus fourteen agency journeys. Exact initial SQL lifecycle invariants passed before dependent tests added invitations.
- Terms display/proposal/review are explicitly intercepted presentation/recovery fixtures. Real activation/terms publication is independently proven by the lifecycle browser; the two evidence types are not conflated.
- `verify-agency-restart-storage.ps1` Capture/Verify and `verify-agency-restart-browser.mjs` passed after identified process restart. SQL and existing records were not reset.
- CI YAML parses; Windows minimum 329/57 and Linux minimum 327/55 preserve the two Windows-only scenarios. Gate self-tests reject skipped, failed, absent SQL/report and undercounted results.
- Source/security and six-pillar rendered reviews: 04-REVIEW.md and 04-UI-REVIEW.md. No blocking finding remains. Owned previews stopped; no sales-funnel edits.

## Limits and retained obligations

Human business/assistive-technology UAT, hosted CI and optional Docker runtime remain unperformed. The dense mobile history refinement is recorded in the UI review. Actual quotes/policies/tasks/balances/statements/bordereaux retain owners in ACCEPTANCE-BACKLOG.md and must be rechecked in Phase 13. A product distribution grant is not rating readiness; due obligations are not completed tasks. This phase does not complete the milestone.
