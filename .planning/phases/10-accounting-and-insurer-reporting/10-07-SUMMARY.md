---
phase: 10-accounting-and-insurer-reporting
plan: '07'
subsystem: finance-refunds
tags: [finance, refunds, approval, sql-server, concurrency]
requires: [10-02, 10-04, 10-06]
provides: [posted-credit-refund-entitlement, collected-cash-reservations, independent-versioned-approvals]
affects: [10-08, 10-11, 10-12, 10-13]
tech-stack:
  added: []
  patterns: [posted-credit-source-binding, serializable-cash-reservation, immutable-approval-rule]
key-files:
  created:
    - backend/src/BackOffice.Infrastructure/Persistence/FinanceRefundRecords.cs
    - backend/src/BackOffice.Infrastructure/Persistence/FinanceRefundModel.cs
    - backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260924134034_FinanceRefundAuthority.cs
    - backend/src/BackOffice.Infrastructure/Persistence/Migrations/FinanceRefundMigrationSql.cs
    - backend/src/BackOffice.Infrastructure/Finance/FinanceRefundService.cs
    - backend/src/BackOffice.Api/FinanceRefundEndpoints.cs
    - backend/tests/BackOffice.IntegrationTests/FinanceRefundRealSqlTests.cs
  modified:
    - backend/src/BackOffice.Infrastructure/Finance/FinanceReceiptService.cs
    - backend/src/BackOffice.Api/Program.cs
    - contracts/openapi.json
    - docs/design/FINANCE-CONTRACTS.md
decisions:
  - Only a posted negative cancellation obligation and its Journal can source a request; a credit by itself is not payable cash.
  - Active reservations bind exact same-payee collected allocations and block allocation reversal until audited rejection.
  - Refund requests pin immutable finance-refund-rule-v1, while each decision checks current finance role and amount authority before idempotency replay.
requirements: [FIN-05]
requirements-completed: []
metrics:
  completed: 2026-09-24
  duration: approximately 1 hour
  tasks: 2
  tests: 8 unit, 1 native SQL, 2 API contract
---

# Phase 10 Plan 07: Refund entitlement and approval summary

A posted cancellation credit can be requested only against collected, same-payee cash left after unpaid debt and prior claims; approval is independently audited and does not pay cash.

## What changed

- `RefundRequest` pins agency, policy, posted cancellation credit obligation, debtor/payee, GBP amount, reason, requester and immutable approval rule. `RefundCashReservation` binds exact collected `Allocation` IDs and amounts; `RefundDecision` retains independent actor, decision, current authority limit, rule and reason. The seeded demo rule needs one approver through £250.00 and two distinct approvers above it; the internal finance role has a current seeded £10,000.00 decision limit.
- `FinanceRefundService.RequestAsync` uses serializable command execution and source row locks. It applies earlier posted credits to outstanding debt in Journal posting order, caps entitlement by remaining credit and collected cash, deducts active requests, validates each source's receipt, payer, policy and debtor, and saves request plus reservations atomically. Rejection releases the active claim while preserving all saved evidence. `DecideAsync` requires a strong If-Match version, forbids requester self approval and repeat actors, rechecks current role and limit before replay, and records the versioned independent decision chain.
- SQL checks reject unposted/wrong-debtor credit requests, unprivileged direct request inserts, wrong-payee or over-residual cash reservations, forged decisions, mutable rules/evidence and reserved allocation reversals. The service also rejects reversal of an allocation used by an active refund. No refund payment or `FinancePosting` is created here.
- Runtime routes are `POST /api/v1/finance/credits/{id}/refunds`, `GET /api/v1/finance/refunds/{id}`, and `POST /api/v1/finance/refunds/{id}/decisions`. Writes require current finance capabilities, CSRF, bounded JSON and idempotency keys; decisions require If-Match. Detail returns saved credit, allocation/receipt provenance, pinned rule, decision audit and ETag. OpenAPI and generated TypeScript describe these shapes.

## Verification

| Check | Result | Evidence |
| --- | --- | --- |
| Failing-first unit proof | Compile failed before `FinanceRefundMath` existed | `.local/phase10-07-unit-red` |
| Unit filter | 8 passed, 0 failed, 0 skipped | `.local/phase10-07-unit-final/phase10-07-refund-final.trx` |
| Fresh native SQL filter | 1 passed, 0 failed, 0 skipped; disposable migrated SQL Server database | `.local/phase10-07-sql-authority/phase10-07-refund-authority.trx` |
| API contract tests | 2 passed, 0 failed | `node --test tests/finance-refund-contracts.test.mjs` |
| API build | 0 warnings, 0 errors | `dotnet build backend/src/BackOffice.Api/BackOffice.Api.csproj --no-restore` |
| OpenAPI lint | Valid, 104 existing warnings | `node scripts/lint-openapi.mjs` |
| Diff and migration model | Whitespace check clean; `HasPendingModelChanges()` false in native SQL test | `git diff --cached --check`; native SQL test |

The native case uses a real issued policy and posted cancellation credit. One penny of collected cash still leaves the invoice unpaid and rejects a refund. After collecting the invoice, two distinct-key requests race for the remaining entitlement: exactly one saves, and further claims fail. The case checks source reservations, the £250 two-approver branch, different independent actors, self-approval rejection, current amount limit, rule immutability, wrong debtor/payee and unprivileged direct SQL requests, forged pending request/source overcap/decision rejection, service and direct SQL allocation reversal denial, audited rejection and re-request, changed If-Match under the same key conflict, and denial of an original-key replay after role revocation. It asserts no refund cash posting. Existing allocation SQL already requires a posted invoice with the matching payer, and the new reservation guard rechecks the saved receipt assignment.

## Review and handoff

- T10-07's unsupported-payment threat is closed for request and approval: negative insurance credit alone is insufficient, and a requester cannot approve their own refund in service or direct SQL. Distinct-key competition and source over-reservation are checked under the same persisted residual evidence.
- Plan 10-08 must queue and pay only an approved exact request, pin one deterministic local payment operation, recover durable provider outcomes and add the paid refund posting/source-binding and bank reconciliation source support. Approval here is an unpaid state. No real bank or provider call occurred.
- FIN-05 stays pending because payment execution, downstream UI and phase verification are separate plans. The current internal `finance` role supplies `finance-read`, `finance-refund-request` and `finance-refund-approve`; a standalone refund-only role was not introduced. Existing design-only singular `/decision` OpenAPI paths remain separate from this runtime plural `/decisions` route.
- Retained demo database, file root, keys and existing preview were untouched. Preview-generated `apps/backoffice/AGENTS.md` and `CLAUDE.md`, inherited `next-env.d.ts`, `tsconfig.json` and `.idea/` were not staged.

## Deviations from Plan

**[Rule 2 - SQL source and reversal integrity]** Plan 10-07 described collected cash reservation but the existing allocation reversal path could release its source after approval. Added service and SQL reversal denial for active reservations, direct SQL source/credit/authority guards, and native forgery tests. Commit: `2138245`.

**[Rule 1 - Idempotency version intent]** The decision command's replay intent now includes the exact If-Match bytes. A changed version under the same key produces a command key conflict, while current authority is still checked before exact replay. Commit: `2138245`.

**[Rule 1 - Historical credit order]** Earlier-credit calculation uses Journal posting time and obligation ID consistently in service and SQL, so late posting cannot change which credit first offsets debt. Commit: `2138245`.

## Threat Flags

| Flag | File | Description |
| --- | --- | --- |
| threat_flag: finance-write-endpoints | `backend/src/BackOffice.Api/FinanceRefundEndpoints.cs` | New refund request/decision routes enforce current scoped authority, replay keys and strong version on decisions. |
| threat_flag: financial-evidence-schema | `backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260924134034_FinanceRefundAuthority.cs` | New credit/cash reservation and approval evidence has SQL source, residual, actor and immutability guards. |

## Self-Check: PASSED

Commit `2138245`, the listed implementation files and final native SQL TRX exist. The implementation commit deleted no tracked files. Only this plan's implementation/contracts/tests were staged; central STATE, ROADMAP and VALIDATION remain with the phase orchestrator.
