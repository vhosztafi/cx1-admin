---
phase: 10
status: partial
verified: 2026-09-25
score: 36/38 source bindings fully implemented; 2 partial
---

# Phase 10 goal verification

Phase 10's goal is to reconcile issued policy movements through saved finance workflows. The current-source automated gate and retained-demo preservation/restart checks passed. Two original source obligations remain partial, and retained refund/payment restart readback has no legitimate saved row; this report therefore does not declare the entire phase complete.

## Goal-backward evidence

| Roadmap success criterion | Evidence | Disposition |
| --- | --- | --- |
| Statements and ledger balances reconcile with policy issue, adjustment, cancellation and allocation movements | Plans 10-02 through 10-04 saved scoped postings, exact statements, cash receipt and application records. The final unfiltered native SQL gate passed 576/576; the retained statement download matched its saved SHA-256 after restart. | Automated proof passed. |
| Receipts cannot be overallocated; refund authority and duplicate-payment checks hold under retries | Plans 10-04, 10-07 and 10-08 covered residual locks, collected same-payee entitlement, independent approvals, accepted-provider recovery and one local cash application with native SQL. The retained demo currently has no refund/payment saved example to read back after restart. | Backend automated proof exists; retained refund/payment restart proof remains unproven. |
| Invalid bordereaux cannot submit; corrections, exclusions, CSV export and demo submission persist; closed-period corrections remain auditable | Plans 10-09 through 10-12 saved exact member/version/hash, rejected invalid versions, local deterministic submission, historical CSV bytes, close blockers and later correction. The final finance browser collector passed 21 checks and the submitted historical CSV matched its saved SHA-256 after restart. | Automated proof passed. |

## Current-source gate

The final gate uses the exact current-source isolated Next build at `apps/backoffice/.local/next-phase10-acceptance/operational-source-manifest.json` (BUILD_ID `3z_baOqKegNZGAEm8txvZ`). All 270 manifest source hashes matched the working tree after build and during the native run. The backend test runner rebuilt its current Debug projects before testing.

| Gate | Result | Evidence |
| --- | --- | --- |
| 1. Unfiltered backend unit | 1,394 passed, 0 skipped | `.local/phase10-final/unit/vilmo_DESKTOP-SCF19PJ_2026-09-24_18_28_01_net10.0.trx` |
| 2. Unfiltered native SQL integration | 576 passed, 0 failed/skipped, real SQL Server; 6h33m | `.local/phase10-final/sql/vilmo_DESKTOP-SCF19PJ_2026-09-25_05_47_14_net10.0.trx` |
| 3. Strict TRX inventory | 1,970 unique passed, 511 real SQL, 0 skips; threshold 1,887/485 | `.local/phase10-final/`; `scripts/assert-test-results.ps1` exit 0 |
| 4. Root Node tests | 451 passed, 0 skipped | `.local/phase10-final/pnpm-test.log` |
| 5. Frontend Node tests | 214 passed, 0 skipped | `.local/phase10-final/web-test.log` |
| 6. Frontend lint | Exit 0 | `.local/phase10-final/web-lint.log` |
| 7. Frontend typecheck | Exit 0 | `.local/phase10-final/web-typecheck.log` |
| 8. Isolated production build | Exit 0, current source, isolated `.local-next-phase10-final` | `.local/phase10-final/web-build.log` |
| 9. Saved finance browser | 21 live checks passed, 0 failed | `.local/phase10-final/finance-browser.log`; `.local/phase10-12-browser/browser-report.json` |
| 10. Operational browser aggregate | 10 collectors and retained-demo stage passed | `.local/phase10-final/operational-suite.log`; `.local/operational-suite/2026-09-25T11-37-39-509Z/report.json` |

The first native attempt was interrupted after browser cases selected an older isolated Next build. It produced no acceptance TRX; its diagnostic paths and four observed failures are retained in `.local/phase10-attempts/stale-build/report.json`. After building the current-source manifest, all six affected policy-history, claims and communication browser-backed SQL cases passed with zero skips in `.local/phase10-attempts/current-browser-filter/vilmo_DESKTOP-SCF19PJ_2026-09-24_20_17_02_net10.0.trx`. The focused rerun is diagnostic and is not added to the final unique-case total.

The next unfiltered run exposed two old test fixtures before completion and was stopped without an acceptance TRX. The agency-response test assumed its Phase 9 migration was still the latest; the statement test inserted unbound correction postings that the Phase 10 source guard correctly rejects. The failure inventory is in `.local/phase10-attempts/known-invalid-full/report.json`. The agency test now locates its historical migration and checks its exact retained-data downgrade guard; the statement test now creates a real receipt and allocation after the saved statement cutoff. Both repaired native SQL cases passed, 2/2 with zero skips, in `.local/phase10-attempts/fixture-filter/vilmo_DESKTOP-SCF19PJ_2026-09-25_00_10_53_net10.0.trx`. These diagnostic runs are excluded from the final case total.

A third unfiltered attempt was stopped without an acceptance TRX after a renewal browser collector lost an in-flight route during page teardown. Its focused current-source rerun passed all four renewal variants with zero skips in `.local/phase10-attempts/renewal-proxy-final/vilmo_DESKTOP-SCF19PJ_2026-09-25_02_31_38_net10.0.trx`. The eight other generic API proxy collectors now track their own in-flight handlers and detach routes only after those handlers settle. An initial communication family check exposed Playwright `unrouteAll(wait)` stalling after all 22 commercial assertions; the first bounded diagnostic also exposed a route double-handle caused by detaching before a pending callback finished. The corrected commercial case passed 1/1 with zero skips in `.local/phase10-attempts/communication-drain3/communication-drain3.trx`. These are tool and collector diagnostics, not final gate cases.

A fourth unfiltered attempt reached further browser and native cases, then exposed one stale agency-permission test count: the matrix has nine rows since the saved `statement-download` permission was added, while its old assertion expected eight. This was a test-fixture failure; the product matrix already contained the new permission. The owned runner was stopped without an acceptance TRX. After updating the assertion and checking the statement row explicitly, the affected native case passed 1/1 and the adjacent permission integration family passed 3/3, both with zero skips (`.local/phase10-attempts/permission-fixture/permission-fixture.trx`, `.local/phase10-attempts/permission-family/permission-family.trx`). The other integration tests and contract snapshots were searched for the same fixed permission count; none remained. A fresh comparison of all 270 isolated frontend manifest hashes still matched current app source before restarting the full gate.

The successful 576-case unfiltered SQL TRX predates a subsequent **test-fixture-only** correction. Two servicing migration tests used `GetMigrations().SkipLast(1)`, which had drifted from their historical servicing upgrade to the latest Finance migration. They now pin their intended pre-draft and pre-rating boundaries, verify that retained MID/template data prevents a destructive downgrade, and check the current issued graph. The affected native SQL filter passed 3/3, zero skips, at `.local/phase10-attempts/servicing-boundary-fix/vilmo_DESKTOP-SCF19PJ_2026-09-25_12_23_11_net10.0.trx`. The full SQL result used the earlier fixture revision; no product source changed after that full run. We do not add these three focused cases to its denominator.

The first root Node pass exposed an outdated finance-contract assertion and a strict TRX test fixture that lacked discovered case definitions. After repairing both and adding malformed/duplicate/missing-definition negative cases, the focused parser and contract filter passed 13/13. A later root Node pass exposed a genuine OpenAPI/control-map mismatch: bordereau `anyOf` branches did not locally define required fields, and three UI control mappings named nonexistent operations. Contract and map corrections passed 47/47 focused checks, then the full 451-case root gate passed. These failed attempts are diagnostics, not accepted results.

The strict TRX checker was changed to stream all XML, compare every executed test ID and name with its discovered definition and entry, reject duplicate IDs, failures and skips, and detect malformed XML after counters. A current 1,400-case unit/focused smoke passed; six corrupted TRX variants were rejected (`.local/phase10-attempts/strict-harness-negative/report.json`). It also parsed the prior 601,710,623-byte Phase 9 TRX without loading it as one XML document (`.local/phase10-attempts/strict-harness-large.log`). Those parser checks validate the tool only and do not count as Phase 10 tests. Four legitimate xUnit theory rows share truncated display names; unique test IDs plus exact definition/name/entry links are the canonical case identity.

## Finance source bindings

The original `pAccounting()` ledger has 24 direct controls, 3 inherited controls and 11 implied obligations. `.local/phase10-final/source-bindings.json` compares all 38 distinct IDs with `10-12-BINDINGS.md`: no ID is unmapped. The Phase 10 saved browser report at `.local/phase10-12-browser/browser-report.json` passed 21 checks, including distinct second-row mapping correction, third-row reasoned exclusion, revalidation, exact CSV hash, submission and historical download, scoped policy/agency links, mobile, 200% zoom and keyboard checks.

Two bindings remain partial: `CTL-74e24a6ada25` cannot display a trustworthy pending refund aggregate without a scoped refund list API, and `FIN-IMPLIED-overview-metrics` cannot display monthly pro-rata earned premium without a pinned coverage earning schedule. The overview does not invent either figure.

## Retained demo, restart and limitations

Two additive `--initialize-demo` runs preserved the exact business fingerprint `B488B4F61F832F23721FA3EB1C0A29BE1B4306822EEDB720490A7C98E27F9E8C`: 97,198 rows, 198 tables and 215 retained files before and after each run. Migration count 104 and head `20260924152124_FinancePeriodCloseCorrections` stayed fixed. Evidence: `.local/phase10-final/preservation/report.json`.

After restarting only the owned API/web processes on 5093/3193, seven exact saved readbacks passed in `.local/phase10-final/restart-readback.json`: agency `f3b98024-18e2-463e-96fb-e64b25161f16`, linked policy `d03ca274-cae2-4f1e-a7b5-5c333cc3af91` and transaction, receipt `00296b11-6f65-4e8a-83f2-7cd38221d650` with allocation history, statement `8d471358-8275-41db-aa33-a3a29d29ac5a`, batch `cccc317f-919b-454d-baf2-ce7e7f205bf1`, submission `9c470762-53c9-4351-bd34-fb2427697c0d`, and period review. Statement bytes matched saved SHA-256 `AC8BA91A36B20272A92E4211D1D51657DE94BC90E8B14555A4B0E9BE387BFE7F`; historical submitted CSV bytes matched `5E790ABB965F639ED0787B170E36AADE25292CAE1498B44C6108EFE07ADF912F`. The owned previews were stopped; the existing 3100 preview was preserved.

The retained database has **zero** `RefundRequest` and `FinanceRefundPayment` rows, before and after additive initialization. Refund/payment restart readback is therefore unproven; no row was fabricated. Focused native SQL in 10-07/10-08 covers entitlement, approval, provider timeout/retry, role revocation and one local cash application, but does not change that retained-demo limitation.

No real bank transfer, insurer submission or external provider call is claimed. Human business and assistive-technology UAT, hosted CI and deployment remain outside this local automated gate. Final requirement and phase status must reflect the two source-binding gaps and any pending or failing final evidence.

## Verdict

**Partial.** The ten current-source automated gates and preserved retained-demo restart passed. The 38 source obligations have 36 full and two partial bindings: pending refund overview count and pinned monthly earned premium. Retained refund/payment restart readback is additionally unproven because the saved demo contains no such row. Keep phase and affected requirements open until these gaps have saved-data proof; root owns the central tracking decision. No unresolved HIGH/CRITICAL finding emerged from this final-gate review.
