---
phase: 10-accounting-and-insurer-reporting
plan: '02'
subsystem: finance-ledger
tags: [finance, ledger, accounting-period, sql-server, policy]
requires: [10-01, phase-7-policy-posting]
provides: [scoped-finance-ledger, first-issue-held-period, additive-finance-posting]
affects: [10-03, 10-04, 10-07, 10-09, 10-11, 10-12, 10-13]
tech-stack:
  added: []
  patterns: [single-source-finance-projection, transaction-held-period-lock, immutable-sql-posting]
key-files:
  created:
    - backend/src/BackOffice.Infrastructure/Finance/FinanceLedgerService.cs
    - backend/src/BackOffice.Infrastructure/Finance/FinanceLedgerMath.cs
    - backend/src/BackOffice.Infrastructure/Persistence/FinanceRecords.cs
    - backend/src/BackOffice.Infrastructure/Persistence/FinanceModel.cs
    - backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260924085101_FinanceLedgerFoundation.cs
    - backend/src/BackOffice.Api/FinanceLedgerEndpoints.cs
    - backend/tests/BackOffice.IntegrationTests/FinanceLedgerRealSqlTests.cs
    - backend/tests/BackOffice.UnitTests/FinanceLedgerTests.cs
  modified:
    - backend/src/BackOffice.Infrastructure/Policies/PolicyIssueWriter.cs
    - backend/src/BackOffice.Infrastructure/Persistence/Migrations/BackOfficeDbContextModelSnapshot.cs
    - backend/src/BackOffice.Api/Program.cs
    - backend/src/BackOffice.Application/ActorContext.cs
    - contracts/openapi.json
decisions:
  - Preserve legacy first-issue journals untouched and derive their posting date from original PostedAt in Europe/London.
  - Hold the first eligible open period in the new first-issue transaction before journal sealing.
  - Keep insurance JournalLine source and account guards unchanged; project one posted Journal or one separate FinancePosting per movement.
  - Require current finance-read for full ledger and current policy-read scope for servicing policy-finance access.
metrics:
  completed: 2026-09-24
  duration: approximately 40 minutes
  tasks: 2
  tests: 11 unit, 5 native SQL, 47 API contract
---

# Phase 10 Plan 02: Ledger and posting period foundation

New first-issue journals now pin the earliest eligible open accounting period under the issue transaction. A scoped ledger reads each sealed insurance journal once, includes retained first issues with their original London posting date, and leaves insurance source-component checks intact.

## What changed

- `PolicyIssueWriter.Write` calls `AccountingPeriods.HoldAsync` before creating the first-issue journal. `AccountingPeriodId` and `PostingDate` are saved before its lines and seal. The existing SQL journal trigger validates the chosen open period and holds its row against closure. Historic first-issue journals retain null period/date; the read model projects `PostedAt` to Europe/London without backfill.
- `FinancePosting` is a separate append-only, balanced `decimal(15,2)` book for future non-insurance sources. It carries a unique `(SourceKind, SourceId)` identity, debtor/insurer/cash/internal signed deltas, agency/relationship/policy links, effective and posting dates, currency, reason and held period. Migration `20260924085101_FinanceLedgerFoundation` adds FK, uniqueness, precision and balance checks plus `TR_FinancePosting_Guard`. The trigger rejects cross-agency links, closed/out-of-range periods and update/delete. Allocation is deliberately excluded as a posting kind because it applies an already recorded receipt.
- `FinanceLedgerService` exposes `ListAsync`, `AccountAsync`, `TransactionAsync` and `PolicyAsync`. Insurance rows use the saved obligation, transaction and insurer-payable journal lines; non-insurance rows use distinct FinancePosting IDs. Agency, optional relationship, posting-window filters and total count are applied before page slicing. Results carry signed canonical money strings, source/effective/posting dates, historical terms ID and computed due date from the saved `paymentTermsDays` and processing date. Positive invoice postings remain `outstanding`, credits `credit`; posting alone never claims cash paid.
- Four read routes are registered: `GET /api/v1/finance/ledger`, `/api/v1/finance/accounts/{agencyId}`, `/api/v1/finance/transactions/{transactionId}` and `/api/v1/policies/{policyId}/finance`. Full ledger routes require the new `finance-read` capability and recheck current stored finance identity and agency scope in the read transaction. Policy-finance also permits current `policy-read` staff through `PolicyScope.Hold`; all responses use `Cache-Control: no-store`. OpenAPI and generated TypeScript contracts describe exact response shapes.

## Verification

| Check | Result | Evidence |
| --- | --- | --- |
| FinanceLedger unit filter | 11 executed, 11 passed, 0 skipped | `.local/phase10-02-unit-final/vilmo_DESKTOP-SCF19PJ_2026-09-24_09_48_44_net10.0.trx` |
| Native SQL RealSqlFinanceLedger filter after consolidated migration | 5 executed, 5 passed, 0 skipped | `.local/phase10-02-sql-final/vilmo_DESKTOP-SCF19PJ_2026-09-24_09_52_54_net10.0.trx` |
| API contracts and finance contract tests | 47 passed, 0 skipped | `node --test tests/api-contracts.test.mjs tests/finance-ledger-contracts.test.mjs` |
| API build | Passed, 0 warnings/errors | `dotnet build backend/src/BackOffice.Api/BackOffice.Api.csproj --no-restore` |
| Staged whitespace | Passed | `git diff --cached --check` |

The native SQL tests exercise new issue period pinning and balance, historic null-period projection, closed-period command rollback and retry, an overlapping period-close/issue race, and SQL rejection of cross-agency or closed-period cash postings. They also read the new ledger as finance and reject an underwriter's full-ledger access. `HasPendingModelChanges` is false in the native suite.

## Deviations from plan

- **Rule 2 — correctness:** Added four-component FinancePosting balance and SQL relationship/period/immutability guards to prevent future cash writers from introducing unbalanced or foreign-agency evidence. The plan named the additive posting boundary; exact guard shape was settled during implementation.
- **Rule 1 — generator bug:** Initial OpenAPI generation mutated a shared generic error-response object and created unrelated contract churn. The generator now adds the finance cache header only to each route's success response; regenerated contract tests pass. The implementation commit was amended before summary.
- **Path refinement:** `ActorContext.cs`, `IdentityEndpoints.cs`, `Program.cs`, the unit-test project reference, `scripts/openapi-finance-ledger.mjs`, generated `finance.ts`/OpenAPI and contract tests were necessary to wire the new runtime route and capability. No frontend screen was changed in this slice.

## Source bindings

| Original source | Runtime binding now | Proof and remaining work |
| --- | --- | --- |
| `pAccounting` transaction ledger at `prototype-template.txt:2873` | Scoped `/api/v1/finance/ledger` plus transaction detail, with saved insurance source and dates | Native SQL proves exactly one first-issue row, including legacy; 10-12 still owns screen and each source control. |
| Agency accounts at `prototype-template.txt:2889` | `/api/v1/finance/accounts/{agencyId}` signed account summary | Current finance/agency scope and money conversion implemented; statements, receipts, due ageing and selected-agency UI remain 10-03/10-04/10-12. |
| Closed-period banner and mock `Post journal` at `prototype-template.txt:2827-2830` | New issue period hold and protected FinancePosting table | Native SQL proves close/issue serialization; reasoned correction command and full period close remain 10-11. No mock action is marked covered. |

## Limits and downstream handoff

`FinancePosting` is foundation storage. Receipt, refund and correction source writers and source-table FKs belong to 10-04, 10-07 and 10-11; no cash or provider transfer is claimed here. Paid/allocation status, statement equations, bordereau, period close command and UI remain in later plans. FIN-01, FIN-08 and the compound POL-01 are therefore not marked complete by this slice alone. The policy-finance endpoint currently exposes posted movements and pinned obligation detail; later plans attach allocations, credits and refund outcomes.

The row query materializes one authorized agency's posted movements before page slicing so it can include the London-derived legacy date and exact count. Later reporting work should measure and, if necessary, move this projection into a server-side indexed read model before relying on large production volume. The retained demo database, file root and data-protection keys were not reset.

## Threat flags

| Flag | File | Description |
| --- | --- | --- |
| `threat_flag: finance-read-endpoints` | `FinanceLedgerEndpoints.cs` | New sensitive finance list and detail routes, guarded by current stored scope and no-store responses. |
| `threat_flag: append-only-finance-posting` | `FinanceLedgerFoundation.cs` | New ledger-affecting source table; SQL guard enforces balance, agency links, open period and immutability. Future writers must add FK-backed source ownership. |

## Commit

- `3be0c6e` — `feat(10-02): build scoped ledger and held first issue posting`

## Self-Check: PASSED

All named primary implementation, test, migration and generated-contract files exist. Commit `3be0c6e` is current HEAD; the final native SQL and unit TRX files report nonzero passing cases. `git diff --check` found no whitespace errors.
