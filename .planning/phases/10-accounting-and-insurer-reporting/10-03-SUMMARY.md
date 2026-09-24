---
phase: 10-accounting-and-insurer-reporting
plan: '03'
subsystem: finance-statements
tags: [finance, statements, sql-server, immutable-snapshot, authorization]
requires: [10-02, phase-7-policy-posting]
provides: [reconciled-agency-statement-versions, exact-statement-csv, current-broker-statement-grant]
affects: [10-04, 10-05, 10-11, 10-12, 10-13]
tech-stack:
  added: []
  patterns: [serializable-posted-source-cutoff, saved-source-hash-and-bytes, current-grant-before-read]
key-files:
  created:
    - backend/src/BackOffice.Infrastructure/Finance/FinanceStatementService.cs
    - backend/src/BackOffice.Infrastructure/Finance/FinanceStatementMath.cs
    - backend/src/BackOffice.Infrastructure/Persistence/FinanceStatementRecords.cs
    - backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260924091815_FinanceStatementVersions.cs
    - backend/src/BackOffice.Api/FinanceStatementEndpoints.cs
    - backend/tests/BackOffice.IntegrationTests/FinanceStatementRealSqlTests.cs
    - backend/tests/BackOffice.UnitTests/FinanceStatementTests.cs
  modified:
    - backend/src/BackOffice.Infrastructure/Finance/FinanceLedgerService.cs
    - backend/src/BackOffice.Infrastructure/Persistence/FinanceModel.cs
    - backend/src/BackOffice.Infrastructure/Persistence/AgencyPermissionModel.cs
    - backend/src/BackOffice.Api/Program.cs
    - contracts/openapi.json
decisions:
  - Statement windows use posting date [from,to), independently of annual accounting periods, and one UTC posted-source cutoff.
  - Agency statements sum only agency-debtor movements; relationship debtors stay separate even within the same agency and policy.
  - Due ageing is saved per charge from its historical terms and statement end date; no paid or residual status is inferred before allocations exist.
  - Command receipts retain only statement ID and version; exact CSV bytes and full ordered source snapshot live in the immutable statement version.
  - Internal finance generation and current broker own-agency statement download have separate permission paths.
requirements: [FIN-01, FIN-02, POL-01]
requirements-completed: []
metrics:
  completed: 2026-09-24
  duration: approximately 42 minutes
  tasks: 2
  tests: 20 focused unit, 1 native SQL, 50 API contract
---

# Phase 10 Plan 03: Reconciled agency statement versions

Statement generation now seals a reconciled, posted-source agency view with its input hash and exact CSV bytes. A later terms version, late posted correction or permission revocation does not rewrite a historical version or grant continued access to its download.

## What changed

- `FinanceStatementService.GenerateAsync(ActorContext, Guid agencyId, DateOnly from, DateOnly to, string key, Guid correlationId, CancellationToken)` uses `SqlCommandBoundary.ExecuteAuthorizedAsync` with serializable isolation. Authorization and an agency `UPDLOCK,HOLDLOCK` precede receipt replay and version selection. The service reads posted insurance journals and additive finance postings with one UTC cutoff; it includes agency-debtor source rows before `to`, including rows before `from` for opening. It orders by posting date, posted instant and source key, uses checked pence arithmetic, then saves opening + debits - credits = closing, running balances, source IDs/hash, per-source historical terms ID and due date, exact CSV bytes/hash, creator and cutoff. The command receipt contains only `{id,version}`.
- `FinanceStatementVersion` is an append-only table in migration `20260924091815_FinanceStatementVersions`. It has agency and optional same-agency terms FK, unique `(AgencyId, From, To, Version)`, JSON/hash/window/equation checks and `TR_FinanceStatementVersion_Immutable` rejecting update/delete. Downloads read the saved `ContentBytes` and verify its SHA-256; they never re-render from current sources.
- `FinanceStatementMath.Reconcile` calculates a from-inclusive/to-exclusive statement, including a nonzero opening and a signed credit closing. Positive source charges carry `AgeDaysAtEnd` and `PastDueAtEnd` based on saved due date and the last included day. These fields describe source ageing, not an unpaid balance or allocation status.
- `FinanceStatementService.ListAsync(ActorContext, Guid agencyId, int page, int pageSize, CancellationToken)`, `DetailAsync` and `DownloadAsync` recheck current stored identity, agency ownership and permission inside serializable transactions before count, rows or bytes. Internal staff need `finance-read`; generation additionally needs `statement-generate`. Brokers need active own-agency scope plus a live `statement-download` grant. The existing permission request/independent decision/revocation provenance now accepts that distinct grant; the permission matrix reports its current availability. Broker authorization failures return a scoped 403.
- `FinanceStatementEndpoints` registers `POST/GET /api/v1/finance/agencies/{agencyId}/statements`, `GET /api/v1/finance/statements/{id}` and `GET /api/v1/finance/statements/{id}/download`. Generation requires CSRF through the existing API middleware, `Idempotency-Key`, a bounded two-date JSON body and current finance authority. Reads use `Cache-Control: no-store`; download serves the saved CSV version. The agency account route remains the 10-02 `GET /api/v1/finance/accounts/{agencyId}`. OpenAPI, generated TypeScript and the agency permission client type reflect these routes and grant.

## Verification

| Check | Result | Evidence |
| --- | --- | --- |
| Statement and agency-permission unit filters | 20 executed, 20 passed, 0 skipped; FinanceStatement itself 3 | `.local/phase10-03-unit-final2/vilmo_DESKTOP-SCF19PJ_2026-09-24_10_41_32_net10.0.trx` |
| Fresh native SQL RealSqlFinanceStatement filter | 1 executed, 1 passed, 0 skipped; fresh migrated SQL Server database | `.local/phase10-03-sql-final3/vilmo_DESKTOP-SCF19PJ_2026-09-24_10_45_27_net10.0.trx` |
| Statement API contract tests | 3 passed, 0 skipped | `node --test tests/finance-statement-contracts.test.mjs` |
| Existing API and ledger contract tests | 47 passed, 0 skipped | `node --test tests/api-contracts.test.mjs tests/finance-ledger-contracts.test.mjs` |
| OpenAPI generation and lint | 481 operations generated; valid with 104 retained warnings | `node scripts/generate-openapi.mjs`; `node scripts/lint-openapi.mjs` |
| Frontend TypeScript check | Passed | `pnpm --dir apps/backoffice typecheck` |
| Staged whitespace and deletion review | Passed; no file deletions | `git diff --cached --check`; `git diff --diff-filter=D HEAD~1 HEAD` |

The native SQL case verifies pinned 30-day due dates after a separately published seven-day terms version; empty period with nonzero opening and opening-source ageing; a later posted backdated correction excluded from v1 and included in v2; v1 bytes/hash unchanged; a same-agency relationship-debtor posting excluded from agency balance; concurrent distinct-key versions 3 and 4; SQL update rejection; replay identity; and broker access before grant, after independent approval and after revocation. Unit cases cover negative credit closing, checked aggregate overflow and London DST posting-date window selection.

## Source and security review

- T10-03: statement membership uses held `PostedAt <= SourceCutoff` and saved `PostingDate`, never effective date. Historical `IssueFinancialObligation.TermsSnapshotJson` supplies each insurance due date; later AgencyTermsVersion changes cannot alter it. Both list count and page materialization run after current scope and grant checks in one transaction. No receipt stores statement rows or CSV bytes.
- The CSV fields are generated from bounded source keys, dates, hashes and canonical decimal strings; no free-form text is written to spreadsheet cells. SQL rejects updates/deletes to statement versions and mismatched byte hashes. The original insurance journal/line guards and retained first-issue rows were not changed.

## Limitations and downstream handoff

- Cash, allocations and payment status belong to 10-04/10-05. `PastDueAtEnd` indicates that a positive source charge has passed its historical due date; it does not claim an unpaid invoice residual. `FinanceStatementSource` retains those due facts for opening sources even when there are no period rows.
- The Phase 10-12 finance UI will need to call these routes and expose statement-grant requests/downloads. Existing agency screens still carry their pre-finance unavailable text, and the current permission request UI only offers bordereau. No browser flow is claimed here. The full suite and retained demo restart are Phase 10-13 gates.
- No real bank, payment or insurer call was added. FIN-01/FIN-02/POL-01 remain subject to downstream UI, allocation and final verification; no requirement is marked complete by this slice alone.

## Deviations from Plan

None. The current broker grant constraint, safe receipt shape and list paging are necessary parts of the planned current authorization and saved-statement contract.

## Issues encountered

The first published-terms SQL fixture attempts were rejected by existing provenance guards until the review timestamp preceded the version creation time and every product effective date matched the new terms date. The corrected fresh migrated SQL run passed.

## Threat Flags

| Flag | File | Description |
| --- | --- | --- |
| threat_flag: scoped-network-read | `backend/src/BackOffice.Api/FinanceStatementEndpoints.cs` | New version detail/download routes require current finance or own-agency grant inside the service. |
| threat_flag: immutable-finance-schema | `backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260924091815_FinanceStatementVersions.cs` | New retained snapshot and CSV bytes table has hash, reconciliation and immutable guards. |

## Task commits

1. `0b09236` — `feat(10-03): seal reconciled agency statement versions`
2. Documentation and self-check commit follows this summary.

## Self-Check: PASSED

All five named service/API/test artifacts and this summary exist on disk. Implementation commit `0b09236` is present in Git history. Only the inherited `next-env.d.ts`, `tsconfig.json` and `.idea/` work remain outside the plan commit.
