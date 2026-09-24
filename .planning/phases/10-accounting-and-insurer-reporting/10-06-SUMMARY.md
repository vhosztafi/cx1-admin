---
phase: 10-accounting-and-insurer-reporting
plan: '06'
subsystem: finance-reconciliation
tags: [finance, bank-import, reconciliation, sql-server, concurrency]
requires: [10-02, 10-04]
provides: [immutable-bank-imports, signed-cash-matches, duplicate-exclusions, visible-bank-and-cash-variances]
affects: [10-08, 10-11, 10-12, 10-13]
tech-stack:
  added: []
  patterns: [serializable-reconciliation-locks, source-bound-sql-guards, completion-cutoff-cash-evidence]
key-files:
  created:
    - backend/src/BackOffice.Infrastructure/Persistence/FinanceBankRecords.cs
    - backend/src/BackOffice.Infrastructure/Persistence/FinanceBankModel.cs
    - backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260924130634_FinanceBankReconciliation.cs
    - backend/src/BackOffice.Infrastructure/Persistence/Migrations/FinanceBankMigrationSql.cs
    - backend/src/BackOffice.Infrastructure/Finance/FinanceReconciliationService.cs
    - backend/src/BackOffice.Api/FinanceReconciliationEndpoints.cs
    - backend/tests/BackOffice.IntegrationTests/FinanceReconciliationRealSqlTests.cs
    - scripts/openapi-finance-reconciliation.mjs
  modified:
    - backend/src/BackOffice.Infrastructure/Finance/FinanceReceiptService.cs
    - backend/src/BackOffice.Api/Program.cs
    - contracts/openapi.json
    - docs/design/FINANCE-CONTRACTS.md
decisions:
  - Bank import identity is agency plus exact source ImportKey; equal bank fields under different keys remain distinct evidence.
  - Phase 10-06 cash matching accepts posted receipt sources only; refund and insurer source kinds require durable source rows and matching SQL guards in their owning plans.
  - Reconciliation completion requires bank value-date and receipt posting-date residuals to be matched or explicitly explained, with reasoned duplicate exclusions for bank lines.
  - Cross-period bank matching is allowed; completed cash-period views retain their residual and exception as of the completion cutoff.
requirements: [FIN-04]
requirements-completed: []
metrics:
  completed: 2026-09-24
  duration: approximately 2 hours
  tasks: 2
  tests: 9 unit, 5 native SQL, 2 API contract
---

# Phase 10 Plan 06: Bank import and reconciliation summary

Exact bank import identity, signed partial matches to posted receipt cash, append-only reversals, duplicate evidence and visible variance explanations now persist under current finance authority and SQL residual guards.

## What changed

- `BankLine` retains source import key, normalized raw JSON/hash, signed GBP amount, value date, reference and actor. Exact key/facts reimport returns the saved line; equal date/reference/amount under another key creates a distinct candidate. A saved `bank-import` receipt origin must match the referenced line's agency, positive amount, currency, date and reference.
- `Reconciliation` defines a nonoverlapping agency window. `ReconciliationMatch` links a bank line to an actual source-bound receipt `FinancePosting`, permits partial/split signed applications and records a unique, append-only reversal. `BankLineExclusion` requires a matching duplicate candidate and comparison evidence; already matched lines cannot be excluded. `ReconciliationVariance` and `ReconciliationTargetVariance` retain exact residual and reason without a synthetic posting.
- `FinanceReconciliationService` runs commands through `SqlCommandBoundary.ExecuteAuthorizedAsync` with serializable transactions and current stored identity/agency checks before replay. It holds reconciliation, bank line and posting rows and recomputes both residuals. SQL triggers independently enforce immutability, window overlap, source scope/sign/currency, target and line residuals, duplicate evidence, valid explanations and completion; direct inserts and updates cannot bypass the core invariants.
- Completion requires each bank line and each posted receipt cash source in the window to be matched, excluded where eligible, or accompanied by a current exact-residual explanation. Detail keeps actual net/absolute bank variance and unmatched cash visible after completion. A receipt posted in one window can match a bank value date in the next; open target residuals use global match history, and completed target views use their saved cutoff. Later matches/reversals are stamped strictly after the source period's completion, backed by a SQL backdating guard.
- Eleven scoped operations cover bank import/list/detail, reconciliation create/detail, match/reverse, duplicate exclusion, bank and cash variance explanation, and completion. `finance-reconcile` is mapped to the existing stored finance role for writes; finance reads use `finance-read`. The endpoints require session authorization, CSRF and idempotency keys for writes, bounded JSON, and no-store responses. OpenAPI and generated TypeScript describe the saved DTOs and exact source IDs.

## Verification

| Check | Result | Evidence |
| --- | --- | --- |
| Failing-first unit proof | Compilation failed before `FinanceReconciliationMath` existed | Initial RED run during task 10-06-01 |
| Unit filter | 9 passed, 0 failed, 0 skipped | `.local/phase10-06-unit-final/vilmo_DESKTOP-SCF19PJ_2026-09-24_14_12_39_net10.0.trx` |
| Fresh native SQL filter | 5 passed, 0 failed, 0 skipped on disposable migrated SQL Server databases | `.local/phase10-06-sql-complete-final/vilmo_DESKTOP-SCF19PJ_2026-09-24_14_22_30_net10.0.trx` |
| API contract tests | 2 passed, 0 failed | `node --test tests/finance-reconciliation-contracts.test.mjs` |
| API build | Passed with 0 warnings and 0 errors | `dotnet build backend/src/BackOffice.Api/BackOffice.Api.csproj --no-restore` |
| OpenAPI generation and lint | 501 operations; valid with 104 pre-existing warnings | `node scripts/generate-openapi.mjs`; `node scripts/lint-openapi.mjs` |
| Diff review | Staged whitespace checks passed; commits deleted no tracked files | `git diff --cached --check`; `git diff --diff-filter=D HEAD~1 HEAD` |

Native SQL tests prove same-field imports under distinct keys survive, exact padded-reference reimport, changed-source conflict, no fake cash posting, overlap and period scope rejection, split matching, over-residual and opposite-sign rejection, matched-line exclusion denial, unique reversal after service restart, direct SQL immutability/forgery rejection, role revocation before original-key replay, and distinct-key concurrency for the final penny. An empty-bank-line window with an unmatched posted receipt rejects completion through both service and direct SQL until its exact residual is explained. A month-boundary case links a later bank value date to earlier posted cash and retains the completed source-period exception after the match. `HasPendingModelChanges()` is false in the native cases. The retained demo database and provider integrations were not used.

## Review and handoff

- T10-06 false duplicate/variance clearing is guarded at the service and SQL layers. An exclusion requires a genuine same-agency/date/reference/amount/currency candidate and cannot target a line with match history. Explanations are immutable facts tied to the actual residual, and completion checks both bank and cash sides without adding a `FinancePosting`.
- Receipt is the only currently source-bound eligible cash kind. Plans 10-08 and 10-11 must extend the service allowlist and SQL trigger together after paid refund/insurer-settlement source rows and source-binding checks exist, with native signed-cash tests. The documented future fee drawing source needs the same treatment in its owning plan.
- Plan 10-11 close must consume the saved unmatched cash and bank exception state. A completed period retains its as-of target evidence when a later bank window matches the receipt. Plan 10-12 should present actual residuals, explanations and duplicate evidence from saved IDs. FIN-04 remains pending downstream UI and phase verification.
- The current internal `finance` role supplies both `finance-reconcile` and `finance-read`; a standalone reconcile-only role was not introduced. Design-only legacy OpenAPI routes outside the runtime 10-06 family remain separate.

## Deviations from Plan

**[Rule 2 - Cash-side completion]** Review found bank-only completion would allow a window with no bank lines but an unmatched saved cash receipt to close silently. Added `ReconciliationTargetVariance`, cash target detail, exact explanation route and SQL completion/posting guards. The fresh native empty-window case proves denial and visible reasoned resolution. Commit: `e3ded5a`.

**[Rule 1 - Cross-period date and cutoff]** Review found a bank value date can follow the receipt posting date, and a fixed test clock can stamp a later match at the prior completion instant. Global target residuals, as-of completed views, monotonic later match/reversal timestamps and direct SQL backdating guard preserve both periods' evidence. The initial five-case run had 4 pass/1 fail at the cutoff assertion; the focused rerun passed 1/1 and the final five-case run passed 5/5. Commit: `fdc25fa`.

**[Rule 2 - Available cash sources]** The plan lists refund, insurer settlement and fee drawing as future eligible types. Their durable paid cash posting/source bindings do not yet exist in this dependency wave. The service and SQL guard therefore permit only receipt source rows and the downstream handoff requires the allowlist and guard to change together. Commit: `e3ded5a`.

## Threat Flags

| Flag | File | Description |
| --- | --- | --- |
| threat_flag: finance-write-endpoints | `backend/src/BackOffice.Api/FinanceReconciliationEndpoints.cs` | New bank import and reconciliation commands require current finance authorization, CSRF and replay keys. |
| threat_flag: financial-evidence-schema | `backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260924130634_FinanceBankReconciliation.cs` | New retained bank, match and variance evidence has source, residual and immutability SQL guards. |

## Self-Check: PASSED

Implementation commit `e3ded5a` and review fix `fdc25fa` exist; listed files and the final native TRX exist. No tracked file was deleted, and only 10-06 files were staged for these commits. Central STATE, ROADMAP and VALIDATION are owned by the phase orchestrator.
