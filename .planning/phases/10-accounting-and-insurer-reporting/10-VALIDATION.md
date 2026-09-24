---
phase: 10
slug: accounting-and-insurer-reporting
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-09-24
---

# Phase 10 validation strategy

## Test infrastructure

| Property | Value |
| --- | --- |
| Framework | .NET unit and native SQL integration, Node tests, live browser collectors |
| Config | `backend/tests/BackOffice.UnitTests`, `backend/tests/BackOffice.IntegrationTests`, `apps/backoffice/package.json`, root `package.json` |
| Quick feedback | Focused `dotnet test ... --filter FullyQualifiedName~Finance...` or `node --test apps/backoffice/tests/finance*.test.mjs`; each filter must discover nonzero cases |
| Full gate | Unfiltered backend unit and integration, strict TRX inventory, `pnpm test`, `pnpm web:test`, lint/typecheck/build, finance and retained browser suites |
| Runtime | Focused minutes; full native SQL can take several hours (Phase 9 baseline 4+ hours). Run once after final product change. |

## Sampling

After each implementation slice, run its unit and SQL negatives plus frontend tests if changed. After interface groups, run the saved-record browser journey. Before phase verification, run one full current-source gate. Do not treat mock-only tests, filtered zero-case runs, skipped SQL tests or stale TRX as evidence. Preserve report paths and source/assembly hashes. Use `scripts/assert-test-results.ps1` in a child PowerShell because it exits its process.

## Per-plan verification map

| Plan | Requirements | Threat | Mandatory automated family | State |
| --- | --- | --- | --- | --- |
| 10-01 | FIN-01..08, POL-01 | T10-01 | Finance contract/source inventory Node tests | Pending |
| 10-02 | FIN-01, FIN-08 | T10-02 | FinanceLedger unit + native SQL posting/legacy/period tests | Pending |
| 10-03 | FIN-01, FIN-02, POL-01 | T10-03 | FinanceStatement unit + native SQL equation/download tests | Pending |
| 10-04 | FIN-03 | T10-04 | FinanceReceipt unit + native SQL concurrent allocation/reversal tests | Pending |
| 10-05 | FIN-03, FIN-01 | T10-05 | Receipt UI tests + saved browser flow | Pending |
| 10-06 | FIN-04 | T10-06 | FinanceReconciliation unit + native SQL import/match/exclusion tests | Pending |
| 10-07 | FIN-05 | T10-07 | FinanceRefund unit + native SQL entitlement/authority/reservation tests | Pending |
| 10-08 | FIN-05 | T10-08 | FinancePayment native SQL uncertain/retry/crash tests | Pending |
| 10-09 | FIN-06 | T10-09 | FinanceBordereau native SQL version/validation/CSV tests | Pending |
| 10-10 | FIN-07 | T10-10 | FinanceSubmission native SQL adapter/replay tests | Pending |
| 10-11 | FIN-08 | T10-11 | FinancePeriod native SQL close/post race/correction tests | Pending |
| 10-12 | FIN-01..08, POL-01 | T10-12 | Finance UI component and saved-record browser tab/source tests | Pending |
| 10-13 | FIN-01..08, POL-01 | T10-13 | Full current-source gate, retained initialization/restart, goal-backward verification | Pending |

## Required negative and edge cases

- Legacy first issue without period ID remains visible once; new issue/close race serializes; later correction posts only to open period.
- Agency terms changed after issue, backdated effective movement, London midnight/DST, empty statement window with nonzero opening, credit closing and aggregate overflow.
- Two distinct keys race one receipt or invoice residual; crossed allocations use deterministic lock order; duplicate reversal fails; foreign or unidentified payer cannot allocate.
- Identical bank date/reference/amount with distinct import IDs survives; exact reimport is idempotent; split match, opposite sign/currency, matched exclusion and repeated reversal fail safely.
- Unpaid invoice100/credit100 pays zero; paid100/credit30 pays at most30; paid10/credit100 pays at most10 after offset; competing requests cannot reserve twice.
- Payment queued is not paid; approval revocation, definite rejection, transport uncertainty, response loss, changed intent, same-key retry and acknowledged-provider/local-apply crash are tested.
- Invalid batch cannot submit; correction leaves contractual money unchanged; excluded rows retain reasons; source changes after snapshot do not rewrite bytes; CSV quotes/newlines/formula prefixes and numeric signs survive.
- Scope/authorization is checked before replay, list count, statement/batch download and provider execution; adapter is deterministic and local only.

## Manual and final evidence

Visual review at desktop, 390px and 200% zoom checks totals, focus, validation, uncertainty and horizontal table scrolling. Human business and assistive-technology UAT remain for Phase 13. At closeout, compare exact original finance source identities and inherited links to per-binding evidence; record any unresolved source item instead of reducing the denominator. Final verification checks root/backend/frontend builds, nonzero full SQL with no skips, saved finance browser flows and unchanged retained demo database/file/key identities across two additive initializations and a verified owned preview restart.

## Sign-off

- [x] Every planned implementation slice has a named automated family.
- [x] High-risk finance cases and current-scope replay are assigned.
- [ ] Tests, SQL constraints and real saved-record flows pass.
- [ ] Full current-source gate and preserved demo readback pass.
- [ ] Set `nyquist_compliant: true` and `wave_0_complete: true` only after actual evidence.
