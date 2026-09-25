---
phase: 10-accounting-and-insurer-reporting
plan: '13'
subsystem: testing
tags: [finance, sql-server, trx, playwright, nextjs, retained-demo]
requires:
  - phase: 10-accounting-and-insurer-reporting
    provides: Saved ledger, receipt, reconciliation, refund, bordereau, submission, period close and accounting workspace slices from plans 10-02 through 10-12
provides:
  - Strict current-source Phase 10 acceptance inventory and goal-backward verification
  - Retained demo preservation and exact saved readback after owned preview restart
  - Honest source-binding and remaining-gap handoff for central tracking
affects: [phase-10-verification, phase-13-uat, finance-demo]
tech-stack:
  added: []
  patterns:
    - Streamed TRX identity validation against discovered definitions and entries
    - Bounded browser proxy teardown that exposes active errors and preserves in-flight route work
    - Additive demo fingerprint with exact saved byte/hash readback
key-files:
  created:
    - .planning/phases/10-accounting-and-insurer-reporting/10-VERIFICATION.md
    - scripts/browser-route-drain.mjs
    - scripts/verify-finance-preservation.ps1
    - scripts/verify-finance-restart-readback.mjs
  modified:
    - docs/DEMO.md
    - scripts/assert-test-results.ps1
    - contracts/openapi.json
    - docs/design/api-control-map.json
    - backend/tests/BackOffice.IntegrationTests/FinanceStatementRealSqlTests.cs
key-decisions:
  - Keep Phase 10 verification partial while the pending refund overview count and pinned monthly earned premium lack saved-source bindings.
  - Keep retained refund/payment restart readback unproven because the preserved demo contains zero legitimate refund/payment rows.
  - Count only the successful unfiltered current-source SQL TRX in the acceptance denominator; focused fixture corrections remain separately identified.
patterns-established:
  - An acceptance gate must verify discovered/executed identities, zero skips, real SQL cases, fresh source hashes and saved bytes.
requirements-completed: []
duration: ~19h elapsed across long native SQL attempts
completed: 2026-09-25
---

# Phase 10 Plan 13: Finance Acceptance Summary

**A current-source finance gate passed 1,970 unique backend cases and all frontend/browser checks while retained demo data and exact statement/CSV bytes survived additive initialization and preview restart.**

## Outcome

The ten numbered gates passed. The unfiltered backend unit run passed 1,394/1,394 with zero skips, and the fresh real SQL integration run passed 576/576 with zero skips in 6h33m. The strict TRX checker counted 1,970 unique passed cases, including 511 real SQL scenarios, above the required 1,887/485 thresholds. Root Node tests passed 451/451; frontend Node tests passed 214/214; frontend lint, typecheck and isolated production build exited zero. The saved finance browser collector passed 21 checks, and the operational aggregate passed all ten collectors plus its retained-demo stage.

Two additive `--initialize-demo` calls preserved an identical fingerprint across 97,198 business rows, 198 tables and 215 retained files, with 104 migrations and head `20260924152124_FinancePeriodCloseCorrections`. Seven exact saved readbacks passed after restarting only the owned API/web previews on ports 5093/3193. Statement bytes matched saved SHA-256 `AC8BA91A36B20272A92E4211D1D51657DE94BC90E8B14555A4B0E9BE387BFE7F`; submitted historical bordereau CSV bytes matched `5E790ABB965F639ED0787B170E36AADE25292CAE1498B44C6108EFE07ADF912F`. The existing port 3100 preview and retained keys/files remained untouched.

The original finance source ledger has 24 direct, 3 inherited and 11 implied obligations. All 38 IDs have a binding, but only 36 are full: `CTL-74e24a6ada25` lacks a scoped refund list/count API, and `FIN-IMPLIED-overview-metrics` lacks a pinned earning schedule for monthly pro-rata earned premium. The overview supplies no invented figures. The retained demo has zero `RefundRequest` and zero `FinanceRefundPayment` rows, so refund/payment restart readback remains unproven despite focused native SQL fault/retry evidence. Phase status and affected requirements should remain open; parent owns central tracking.

## Evidence

| Gate | Fresh result |
| --- | --- |
| Backend unit | 1,394 passed, 0 skipped; `.local/phase10-final/unit/vilmo_DESKTOP-SCF19PJ_2026-09-24_18_28_01_net10.0.trx` |
| Unfiltered native SQL | 576 passed, 0 skipped; `.local/phase10-final/sql/vilmo_DESKTOP-SCF19PJ_2026-09-25_05_47_14_net10.0.trx` |
| Strict TRX | 1,970 unique passed, 511 real SQL; `.local/phase10-final/` |
| Root Node | 451 passed; `.local/phase10-final/pnpm-test.log` |
| Frontend Node | 214 passed; `.local/phase10-final/web-test.log` |
| Lint / typecheck / isolated build | Exit 0 each; `.local/phase10-final/web-lint.log`, `web-typecheck.log`, `web-build.log` |
| Finance browser | 21 checks; `.local/phase10-final/finance-browser.log`, `.local/phase10-12-browser/browser-report.json` |
| Operational browser | Ten collectors and retained-demo stage; `.local/operational-suite/2026-09-25T11-37-39-509Z/report.json` |
| Additive preservation | Two identical fingerprints; `.local/phase10-final/preservation/report.json` |
| Restart readback | 7/7, exact statement and CSV hashes; `.local/phase10-final/restart-readback.json` |

The current-source isolated browser manifest is `apps/backoffice/.local/next-phase10-acceptance/operational-source-manifest.json`, BUILD_ID `3z_baOqKegNZGAEm8txvZ`; 270 app source hashes matched the tree during the SQL gate. The saved finance browser script was named `scripts/verify-finance-workspace-browser.mjs` in the repository; this is the necessary path refinement from plan text `verify-finance-browser.mjs`. It used authorized local APIs and the deterministic demo insurer adapter. No real bank, payment or insurer call occurred.

## Tasks and commits

1. **10-13-01 — Run and record full finance acceptance:** `3ad329f` records the gate harness/fixture corrections, demo instructions and goal-backward `10-VERIFICATION.md`.
2. **10-13-02 — Review, document and commit this slice:** this summary is committed separately after self-check.

## Deviations and failed attempts

- **[Rule 3 - Blocking setup]** The first native run selected stale isolated Next assets. It was stopped without an acceptance TRX. A current-source manifest was rebuilt and six affected browser-backed SQL cases passed; diagnostic report: `.local/phase10-attempts/stale-build/report.json`.
- **[Rule 1 - Stale fixtures]** The next native run found a Phase 9 migration-head assertion and a statement fixture inserting forged correction postings. The production SQL source guard correctly rejected the latter. The fixtures now use a historical migration identity and a genuine saved receipt/allocation; focused SQL 2/2 passed at `.local/phase10-attempts/fixture-filter/`.
- **[Rule 3 - Browser collector teardown]** A renewal proxy route was cancelled during teardown; eight other generic proxies had the same risk. Route work is tracked and drained before detachment, with bounded diagnostics and active-error checks. Renewal focused SQL 4/4 and communication 1/1 passed at `.local/phase10-attempts/renewal-proxy-final/` and `.local/phase10-attempts/communication-drain3/`.
- **[Rule 1 - Stale permission fixture]** A matrix assertion expected eight permissions after `statement-download` made nine. The focused case passed 1/1 and adjacent family 3/3 at `.local/phase10-attempts/permission-fixture/` and `.local/phase10-attempts/permission-family/`.
- **[Rule 3 - Large TRX gate]** Loading a large TRX as one XML document exhausted memory. The checker now streams all XML, verifies unique run/case IDs against discovered definitions and entries, timestamps, counters and zero skips, and rejects truncated/malformed reports. Six corrupt variants were rejected at `.local/phase10-attempts/strict-harness-negative/report.json`.
- **[Rule 1 - Contract fixtures]** Root Node gates found a stale finance contract assertion and outdated TRX fixture, then a bordereau OpenAPI `anyOf`/UI operation-map mismatch. Focused 13/13 and 47/47 passed before the final 451/451 root run.
- **[Rule 1 - Historical servicing fixture]** After the successful 576-case SQL run, two tests were found using `GetMigrations().SkipLast(1)` and no longer targeting their stated servicing-era upgrade. They now pin pre-draft/pre-rating boundaries and retain the MID/template downgrade guard. Focused native SQL 3/3, zero skips, passed at `.local/phase10-attempts/servicing-boundary-fix/vilmo_DESKTOP-SCF19PJ_2026-09-25_12_23_11_net10.0.trx`. The unfiltered TRX used the earlier fixture revision; product source was unchanged. These three focused cases are not added to its count.

All failed full-run attempts were retained as diagnostics and excluded from the final denominator. The final unfiltered native run passed before the post-gate historical fixture refinement; a documentation-only change did not trigger another multi-hour gate.

## Security and correctness review

The strict checker rejects false acceptance from malformed, duplicated, stale, skipped or missing discovered/executed cases. Browser proxy errors during active work remain failures; only teardown cancellation after the collector's saved-state assertions is suppressed. Additive initialization, an unchanged row/file fingerprint and exact saved hashes guard against demo reset or fabricated readback. The contract fix aligned existing operation IDs and documented the actual correction command; it did not add a public endpoint or broaden authority. No unresolved HIGH/CRITICAL issue was found in this slice.

## Known limitations and handoff

- The pending refund overview aggregate and earned premium metric require saved, scoped source data before their source controls can be called complete.
- The retained database contains no legitimate refund/payment row for restart readback. Native SQL fault/retry proof exists, but retained persistence of that branch is not claimed.
- Human business and assistive-technology UAT, hosted CI and deployment remain outside this local gate. No real external money or insurer transfer was performed.
- Parent owns `.planning/STATE.md`, `ROADMAP.md`, `REQUIREMENTS.md`, `10-VALIDATION.md` and independent verification. Do not mark Phase 10 or every plan-frontmatter requirement complete from this partial report.
- Inherited dirty `apps/backoffice/next-env.d.ts`, `tsconfig.json`, `.idea/` and preview-generated `AGENTS.md`/`CLAUDE.md` were left unstaged. Owned preview processes were stopped; the pre-existing 3100 preview was preserved.

## Self-Check: PASSED

`10-VERIFICATION.md`, this summary and the named SQL, preservation, restart and operational reports exist. Commit `3ad329f` exists. `git diff --check` reported no error, and the task commit deleted no tracked file. Its staged file list contained no inherited preview/config or generated guide file.
