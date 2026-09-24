---
phase: 10-accounting-and-insurer-reporting
plan: '04'
subsystem: finance-cash
tags: [finance, receipts, allocations, reversals, sql-server, concurrency]
requires: [10-02, 10-03, phase-7-policy-posting]
provides: [scoped-receipt-cash-facts, payer-assignment-history, residual-safe-allocations, append-only-reversals]
affects: [10-05, 10-06, 10-11, 10-12, 10-13]
tech-stack:
  added: []
  patterns: [serializable-receipt-and-invoice-locks, source-bound-sql-postings, noncash-debtor-reclassification]
key-files:
  created:
    - backend/src/BackOffice.Infrastructure/Finance/FinanceReceiptService.cs
    - backend/src/BackOffice.Infrastructure/Persistence/FinanceCashRecords.cs
    - backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260924103439_FinanceReceiptApplications.cs
    - backend/src/BackOffice.Api/FinanceReceiptEndpoints.cs
    - backend/tests/BackOffice.IntegrationTests/FinanceReceiptRealSqlTests.cs
    - scripts/openapi-finance-receipts.mjs
  modified:
    - backend/src/BackOffice.Infrastructure/Persistence/FinanceModel.cs
    - backend/src/BackOffice.Api/Program.cs
    - contracts/openapi.json
    - docs/design/FINANCE-CONTRACTS.md
decisions:
  - A receipt moves cash into suspense once; allocation and reversal change debtor and suspense balances with zero additional cash movement.
  - Current payer assignment ID is the strong If-Match version; reassignment requires all applications to have been reversed.
  - Receipt/invoice residuals are recomputed under serializable receipt and sorted invoice locks, then backed by SQL insert triggers and uniqueness constraints.
  - Cash writes currently require both finance-cash-write and the existing finance-read identity role; both map to the internal finance role.
requirements: [FIN-03]
requirements-completed: []
metrics:
  completed: 2026-09-24
  duration: approximately 80 minutes
  tasks: 2
  tests: 11 unit, 4 native SQL, 2 API contract
---

# Phase 10 Plan 04: Scoped receipts and append-only applications

Receipt cash, payer identity, invoice applications and reversals now have durable source rows, balanced postings and residual guards that survive concurrent commands.

## What changed

- `Receipt` saves the positive GBP cash fact, agency, original operation identity, reference, held accounting period, posting date and actor. `ReceiptPayerAssignment` saves an ordinal history; `Allocation` saves positive invoice applications or a reversal linked to exactly one original. SQL rejects updates/deletes, duplicate source identities, duplicate reversals and assignments that move active money to another payer.
- `FinanceReceiptService.RecordAsync`, `AssignAsync`, `AllocateAsync` and `ReverseAsync` run through `SqlCommandBoundary.ExecuteAuthorizedAsync` at serializable isolation. The current stored finance identity and agency or relationship scope are checked before idempotent replay. Commands hold agency/relationship scope, then receipt, sorted invoices and open accounting period locks. They recalculate receipt and invoice pence residuals while held. `DetailAsync` and `ListAsync` check current read authority before disclosing data.
- The receipt insert trigger writes one balanced `receipt` posting: `CashDelta=+amount`, `InternalDelta=-amount`. Each saved allocation writes a `receipt-application` posting with `CashDelta=0`, `DebtorDelta=-amount`, `InternalDelta=+amount`; reversal writes the counter-entry. The finance posting source check was extended for those two explicit kinds. A source-binding trigger checks the exact receipt/allocation identity, agency, policy, transaction, period, dates, currency and vector, including null scope mismatches. Direct SQL inserts cannot overdraw a receipt or invoice or forge a source posting.
- Six routes cover `POST/GET /api/v1/finance/agencies/{agencyId}/receipts`, `GET /api/v1/finance/receipts/{id}`, `POST /api/v1/finance/receipts/{id}/payer`, `POST /api/v1/finance/receipts/{id}/allocations` and `POST /api/v1/finance/allocations/{id}/reversals`. Writes have bounded JSON, CSRF through the existing API middleware, idempotency keys and current finance scope; payer changes and allocations require the assignment ID in `If-Match`. Reads set `Cache-Control: no-store`. OpenAPI and generated TypeScript describe the runtime shapes.

## Verification

| Check | Result | Evidence |
| --- | --- | --- |
| Failing-first unit and native SQL compilation | Both failed before implementation on absent receipt math/service types | Initial RED runs during task 10-04-01 |
| Receipt unit filter | 11 passed, 0 failed, 0 skipped | `.local/phase10-04-unit-final/phase10-04-unit.trx` |
| Fresh native SQL receipt filter | 4 passed, 0 failed, 0 skipped on fresh migrated SQL Server databases | `.local/phase10-04-four-case-final/phase10-04-four-case-final.trx` |
| Receipt API contract tests | 2 passed, 0 skipped | `node --test tests/finance-receipt-contracts.test.mjs` |
| OpenAPI generation and lint | 491 operations; valid with 104 retained warnings | `node scripts/generate-openapi.mjs`; `node scripts/lint-openapi.mjs` |
| API build | Passed with 0 warnings | `dotnet build backend/src/BackOffice.Api/BackOffice.Api.csproj --no-restore` |
| Staged diff and deletions | Whitespace check passed; no tracked deletion | `git diff --cached --check`; `git diff --diff-filter=D HEAD~1 HEAD` |

The SQL tests use distinct command keys and connections to race for a receipt's last penny and for one invoice across two receipts. A fourth case creates first-issue and servicing invoices for the same agency, then races two receipts whose allocation arrays name those invoices in opposite orders. One command commits both applications and the other commits none; both invoice residuals and receipt residuals remain nonnegative, and no application duplicates cash. The suite also races an allocation against a reversal, rejects a duplicate reversal, keeps payer history after full reversal, rejects unidentified-payer allocation, wrong debtor/currency and zero amount, and proves replay from a new service instance. A deleted stored finance role makes even the original replay key return 403. Direct SQL over-residual allocation, forged application posting and update of saved receipt/allocation rows fail. The test confirms cash sums once, debtor application and its reversal net to zero, and the EF model has no pending changes.

## Source and security review

- T10-04: receipt and invoice residuals are checked in pence while held with `UPDLOCK,HOLDLOCK`; SQL triggers repeat the residual guard for direct writes. Invoice locks are taken in ID order by the service. The 10-02 posting design was refined with explicit noncash application source kinds, so statement and ledger projections continue to see the receipt cash once.
- The current permission bridge grants `finance-cash-write` to the internal `finance` role, and `FinanceLedgerService.Authorize` also requires `finance-read` for current stored identity and scope. The runtime write routes enforce `finance-cash-write`; a standalone cash-only role is not yet supported.
- `OriginKind='bank-import'` preserves a future import source identity but this plan does not connect to a bank line. A real import/reconciliation workflow belongs to 10-06; no bank or payment provider was called. The legacy design-only receipt URLs remain separately described in OpenAPI where no 10-04 runtime route was added.

## Limitations and downstream handoff

- The native suite proves same-receipt, same-invoice, crossed two-invoice and allocation/reversal contention. The 10-13 gate still owns broader load and retained-demo restart checks; no negative residual, partial posting or double cash result was observed in the focused cases.
- 10-05 should build cash/statement presentation from posted source kinds without treating an application as a second cash receipt. 10-06 should bind any bank-import origin to a saved bank line. 10-12 must wire finance UI flows to these routes. FIN-03 remains subject to downstream UI and final verification and is not marked complete here.

## Deviations from Plan

**[Rule 2 - Critical accounting guard]** The 10-02 posting-kind contract had no allocation kind. This plan added explicit source-bound `receipt-application` and `receipt-application-reversal` kinds with zero cash delta to avoid double counting while preserving balanced debtor reclassification. SQL guards and native tests verify this refinement. Commit: `e4f5ba5`.

**[Rule 1 - SQL null-scope guard]** Review found nullable comparison could accept a null policy/transaction/relationship in a forged posting. The source-binding trigger now rejects null as well as unequal values; final fresh native SQL passed. Commit: `e4f5ba5`.

The crossed-order fixture first used two separate first issues, which correctly belonged to different agencies and failed the same-payer setup assertion before reaching the race. A first issue plus its servicing issue supplied two valid same-agency invoices; the new case and full four-case filter passed. The finance contract text now states that receipt cash enters suspense once and application/reversal postings have zero cash delta. Commit: `311606d`.

## Threat Flags

| Flag | File | Description |
| --- | --- | --- |
| threat_flag: scoped-finance-write | `backend/src/BackOffice.Api/FinanceReceiptEndpoints.cs` | New receipt commands require current finance cash capability, CSRF, idempotency and scope checks. |
| threat_flag: retained-cash-schema | `backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260924103439_FinanceReceiptApplications.cs` | Immutable cash and application rows with source, period, payer, reversal and residual SQL guards. |

## Task commits

1. `e4f5ba5` — `feat(10-04): persist scoped receipts and append-only allocations`.
2. `311606d` — `test(10-04): prove crossed invoice allocation race`.

## Self-Check: PASSED

The summary, service and migration exist; commits `e4f5ba5` and `311606d` are present. No tracked file was deleted by either task commit, and only inherited frontend files and `.idea/` remain outside this plan.
