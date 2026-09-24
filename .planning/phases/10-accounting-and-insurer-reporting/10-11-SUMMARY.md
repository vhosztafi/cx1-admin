---
phase: 10-accounting-and-insurer-reporting
plan: '11'
subsystem: finance-periods
tags: [finance, accounting-period, correction, sql-server, reconciliation]
requires: [10-02, 10-03, 10-06, 10-08, 10-09, 10-10]
provides: [authorized-period-close, sealed-journal-lines, source-bound-later-corrections]
affects: [10-12, 10-13]
tech-stack:
  added: []
  patterns: [period-row-lock-before-close, saved-close-checklist, append-only-linked-correction]
key-files:
  created:
    - backend/src/BackOffice.Infrastructure/Finance/FinancePeriodService.cs
    - backend/src/BackOffice.Infrastructure/Persistence/FinanceCorrectionRecords.cs
    - backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260924152124_FinancePeriodCloseCorrections.cs
    - backend/src/BackOffice.Api/FinancePeriodEndpoints.cs
    - backend/tests/BackOffice.IntegrationTests/FinancePeriodRealSqlTests.cs
  modified:
    - backend/src/BackOffice.Infrastructure/Persistence/Migrations/BackOfficeDbContextModelSnapshot.cs
    - backend/src/BackOffice.Infrastructure/Finance/FinanceReconciliationService.cs
    - contracts/openapi.json
    - docs/design/FINANCE-CONTRACTS.md
decisions:
  - Close requires completed bank and cash reconciliation, no pending or acknowledged-unapplied refund, full-period current statements for active agencies, and valid current bordereaux for active providers.
  - Legacy sealed first issues are assigned to a period by their original London posting date without backfilling the journal.
  - A manual finance journal request posts a balanced linked correction in a later held open period; it never edits insurance JournalLine.
requirements: [FIN-08, FIN-01]
requirements-completed: []
metrics:
  completed: 2026-09-24
  tasks: 2
  tests: 6 unit, 3 native SQL, 2 API contracts, 8 affected native SQL reruns
---

# Phase 10 Plan 11: Sealed periods and linked corrections

Finance can close a reviewed accounting period only when its saved sources, reconciliation, payments and reports are ready; later adjustments post as independently balanced, linked evidence in an open period.

## What changed

- `FinancePeriodService.ListAsync` and `ReviewAsync` expose saved periods and live blockers under current internal finance authority. `CloseAsync` requires the period row version, a reason and an idempotency key, locks the same `AccountingPeriod` row used by issue and cash posting, rechecks blockers, and saves actor, time, source cutoff and JSON checklist. A changed intent under one key conflicts and current authorization precedes replay.
- Close treats a sealed first-issue journal with null period as activity in the period containing its original London `PostedAt`. It requires a statement covering the entire period for every agency with insurance or finance posting, and a valid current bordereau for every provider with insurance activity. Statement freshness includes finance cash/correction postings. Bank value dates and cash posting dates require completed reconciliation; overlapping open reconciliation, a queued credit refund, or a saved accepted but unapplied provider result blocks close.
- `FinanceCorrection` pins an insurance journal or finance posting source, original scope, actor and reason. The controlled command uses exact four-account signed decimal balance, current finance scope, and `AccountingPeriods.HoldAsync` to post its separate `FinancePosting` in a later eligible open period. SQL rejects forged sources, mismatched scope, open original periods, late destination periods and mutable correction evidence. Correction cash is eligible for signed bank reconciliation with the source allowlist changed in service and SQL together.
- SQL close guards reject direct state transitions that lack evidence or have critical blockers and reject reopening. A new posted `JournalLine` update/delete guard prevents modification of sealed insurance detail. Existing range guards continue to reject overlapping or changed accounting period boundaries.
- Runtime routes and generated contracts cover `GET /finance/periods`, `GET /finance/periods/{periodId}`, `POST /finance/periods/{periodId}/close`, `POST /finance/corrections`, `GET /finance/corrections/{id}` and `POST /finance/journals`, which is the same controlled correction path. The existing internal finance role supplies `finance-read`, `finance-period-close` and `finance-correction-post`; policy actors do not gain these rights.

## Verification

| Check | Result | Evidence |
| --- | --- | --- |
| Failing first unit and native SQL gates | Unit compile failed before implementation; native SQL initially failed on pending model changes | `.local/phase10-11-red/phase10-11-red.trx` |
| Final unit filter | 6 passed, 0 failed, 0 skipped | `.local/phase10-11-unit-final/phase10-11-unit-final.trx` |
| Final native SQL period filter | 3 passed, 0 failed, 0 skipped | `.local/phase10-11-payment-final/phase10-11-payment-final.trx` |
| Affected prior-plan native SQL failures rerun | 4 passed, 0 failed, 0 skipped | `.local/phase10-11-regression-fixed/phase10-11-regression-fixed.trx` |
| Shared servicing posting theory scenarios | 4 passed, 0 failed, 0 skipped | `.local/phase10-11-servicing-legacy/phase10-11-servicing-legacy.trx` |
| API contract tests | 2 passed, 0 failed | `node --test tests/finance-period-contracts.test.mjs` |
| API build | 0 warnings, 0 errors | `dotnet build backend/src/BackOffice.Api/BackOffice.Api.csproj --no-restore -v:minimal` |
| OpenAPI and diff | Valid with 104 existing lint warnings; staged whitespace check clean | `node scripts/lint-openapi.mjs`; `git diff --cached --check` |

The native period cases use fresh disposable SQL Server databases and assert the saved close checklist, legacy journal projection, missing and stale reporting, direct SQL wrong actor/overlap/reopen/line edit/forged correction denial, authorized replay, a close that wins an overlapping issue, linked later correction, matched correction cash, and the cash-only statement prerequisite. The payment case proves queued and timeout-after-success acknowledged but unapplied refunds block both service review and direct SQL close. Each native case asserts no pending EF model changes where applicable. No retained demo database, provider, bank, preview process, file root or data-protection key was changed.

## Review and handoff

- T10-11 close/post race and sealed-history threat is covered by the shared period row lock, SQL transition/source guards and direct native negative checks. Corrections do not alter the old Journal, JournalLine, statement version or bordereau version.
- Existing 10-02, servicing and 10-08 SQL fixtures had direct `AccountingPeriod.State` toggles that predate the authorized close policy. Their test-only helper disables the new close trigger around each setup update, restores it in `finally`, and verifies it is enabled before behavior assertions. The prior 10-case regression run was initially 6/10 because an overloaded helper interpolated a Guid as raw SQL; the four affected cases then passed 4/4 after parameterization. The two servicing theory scenarios that use the shared fixture also passed 4/4. The new 10-11 cases never disable the guard.
- 10-12 can present saved period review, close and correction results. 10-13 should run the full native SQL and browser gates. FIN-08 and FIN-01 remain open until downstream UI and phase verification complete. Inherited `next-env.d.ts`, `tsconfig.json`, `.idea/` and preview-generated `AGENTS.md`/`CLAUDE.md` were not staged.

## Deviations from Plan

**[Rule 2 - Sealed JournalLine update/delete guard]** The existing source trigger guarded insertion but did not stop direct edits to a posted line. Added a SQL trigger and native update/delete rejection. Commit: `4862511`.

**[Rule 2 - Legacy and cash-only reporting inclusion]** A period close could otherwise miss legacy null-period first issues or a later finance-only posting. Added London-date legacy projection and statement freshness across both journal and finance postings in service and SQL, with native cases. Commit: `4862511`.

**[Rule 1 - Prior native fixture compatibility]** Direct closed/open toggles in older posting tests became invalid under the new close guard. Scoped and verified trigger restoration preserves those tests' posting focus while the new tests prove the production guard. Commit: `4862511`.

## Threat Flags

| Flag | File | Description |
| --- | --- | --- |
| threat_flag: finance-write-endpoints | `backend/src/BackOffice.Api/FinancePeriodEndpoints.cs` | Close and correction commands enforce current role, version or source scope, replay and reason controls. |
| threat_flag: accounting-schema | `backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260924152124_FinancePeriodCloseCorrections.cs` | New closure evidence, immutable correction records and direct SQL guards cover close and posting trust boundaries. |

## Self-Check: PASSED

Implementation commit `4862511`, created files and every listed TRX report exist. The implementation commit deleted no tracked file and excluded inherited dirty artifacts. Central STATE, ROADMAP and VALIDATION remain with the phase orchestrator.
