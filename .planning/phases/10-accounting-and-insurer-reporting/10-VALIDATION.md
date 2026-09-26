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
| 10-01 | FIN-01..08, POL-01 | T10-01 | Finance contract/source inventory Node tests | 7/7 green 2026-09-24; contract evidence only |
| 10-02 | FIN-01, FIN-08 | T10-02 | FinanceLedger unit + native SQL posting/legacy/period tests | 11/11 unit, 5/5 native SQL and 47/47 API contract passed 2026-09-24; see 10-02-SUMMARY.md |
| 10-03 | FIN-01, FIN-02, POL-01 | T10-03 | FinanceStatement unit + native SQL equation/download tests | 20/20 focused unit and permission, 1/1 native SQL, 50/50 API contract passed 2026-09-24; see 10-03-SUMMARY.md |
| 10-04 | FIN-03 | T10-04 | FinanceReceipt unit + native SQL concurrent allocation/reversal tests | 11/11 unit, 4/4 native SQL and 2/2 API contract passed 2026-09-24; see 10-04-SUMMARY.md |
| 10-05 | FIN-03, FIN-01 | T10-05 | Receipt UI tests + saved browser flow | 5/5 frontend unit and 10/10 live browser checks passed 2026-09-24; ESLint and TypeScript green; see 10-05-SUMMARY.md |
| 10-06 | FIN-04 | T10-06 | FinanceReconciliation unit + native SQL import/match/exclusion tests | 9/9 unit, 5/5 native SQL and 2/2 API contract passed 2026-09-24; see 10-06-SUMMARY.md |
| 10-07 | FIN-05 | T10-07 | FinanceRefund unit + native SQL entitlement/authority/reservation tests | 8/8 unit, 1/1 native SQL and 2/2 API contract passed 2026-09-24; see 10-07-SUMMARY.md |
| 10-08 | FIN-05 | T10-08 | FinancePayment native SQL uncertain/retry/crash tests | 3/3 native SQL, 1/1 affected resume authority and 1/1 accepted outcome reruns, 5/5 API contract passed 2026-09-24; see 10-08-SUMMARY.md |
| 10-09 | FIN-06 | T10-09 | FinanceBordereau native SQL version/validation/CSV tests | 8/8 unit, 2/2 native SQL and 2/2 API contract passed 2026-09-24; see 10-09-SUMMARY.md |
| 10-10 | FIN-07 | T10-10 | FinanceSubmission native SQL adapter/replay tests | 2/2 native SQL, 1/1 changed-intent rerun and 4/4 API contract passed 2026-09-24; see 10-10-SUMMARY.md |
| 10-11 | FIN-08 | T10-11 | FinancePeriod native SQL close/post race/correction tests | 6/6 unit, 3/3 native SQL, 2/2 API contract and 8/8 affected SQL regression passed 2026-09-24; see 10-11-SUMMARY.md |
| 10-12 | FIN-01..08, POL-01 | T10-12 | Finance UI component and saved-record browser tab/source tests | 5/5 frontend and 21/21 live browser passed 2026-09-24; TypeScript, lint, build green; earned premium/refund count remain partial; see 10-12-SUMMARY.md and 10-12-BINDINGS.md |
| 10-13 | FIN-01..08, POL-01 | T10-13 | Full current-source gate, retained initialization/restart, goal-backward verification | Ten gates passed: 1,970 unique backend cases (511 real SQL), 451 root Node, 214 frontend Node, finance browser 21/21, operational suite 10 collectors; two additive inits unchanged and restart readback 7/7. Goal verifier found three gaps; see 10-VERIFICATION.md. |
| 10-14 | FIN-05 | T10-14 | Native SQL scoped pending refund list/count, authority/revocation, frontend queue and saved-ID browser | Focused native SQL 1/1, 214 frontend, 45 contract and 22 live browser checks passed; positive pending-to-approved UI transition is in `.local/phase10-gap18-refund/journey.json`. |
| 10-15 | FIN-01, FIN-08 | T10-15 | Exact signed monthly earning math, additive migration/backfill and native SQL source/hash tests | Six focused unit cases and one native SQL source/backfill/tamper case passed; migration `20260925160442_FinanceEarningSlices` is additive. |
| 10-16 | FIN-01, FIN-08 | T10-16 | Atomic issue/MTA/cancellation schedule writes and two-run retained backfill preservation | First issue and cancellation native SQL 2/2, two MTA SQL cases 2/2; retained backfill 72 sources/879 slices, second run zero; 98,042 original rows and 215 files preserved, then 98,921 rows and 215 files on repeat. |
| 10-17 | FIN-01, FIN-08 | T10-17 | Scoped period earned-premium API, closed cutoff, frontend/live browser basis | Native SQL read/authority/tamper 1/1, 45 API contract, 214 frontend and 23 live browser checks passed; a synthetic closed-period test was rejected by the genuine close trigger, so closed-cutoff read still needs a legitimate closed-period run. |
| 10-18 | FIN-01, FIN-05, FIN-08, POL-01 | T10-18 | Legitimate retained refund/payment restart, fresh full current-source acceptance and independent verifier | Authorized retained journey and SQL readback passed with one operation and posting; 10 saved restart readbacks passed after owned 5095/3193 process restart, two additive initializations preserved 100,635 rows and 215 files, root 451/451, web 214/214, lint/typecheck/build, live finance browser 25/25 and ten operational collectors passed. Unfiltered native SQL 577/577 and strict TRX 1,977 unique cases including 512 real SQL passed with zero skips. |

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
- [x] Tests, SQL constraints and real saved-record flows pass for the implemented journeys.
- [x] Prior full current-source gate and seven preserved demo readbacks passed; a legitimate retained refund/payment now exists and its fresh restart readback passed 10/10.
- [ ] Set `nyquist_compliant: true` and `wave_0_complete: true` only after actual evidence.
