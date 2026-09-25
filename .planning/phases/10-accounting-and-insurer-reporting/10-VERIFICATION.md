---
phase: 10-accounting-and-insurer-reporting
verified: 2026-09-25T11:54:07Z
status: gaps_found
score: 14/16 must-haves verified
overrides_applied: 0
gaps:
  - truth: "Every finance source tab/action and inherited entry opens the correct saved record; no mock figure or toast-only finance effect remains."
    status: partial
    reason: "The source refund-attention row has no saved pending count or direct saved-record target; the overview only links to manual ID entry."
    artifacts:
      - path: apps/backoffice/components/finance/overview.tsx
        issue: "Refund approval row contains explanatory text and a generic refunds link, without a scoped saved count or refund ID."
      - path: backend/src/BackOffice.Api/FinanceRefundEndpoints.cs
        issue: "Only request, detail and decision routes exist; no scoped list/count route."
    missing:
      - "Add an authorised, agency-scoped pending refund list/count from saved requests and bind the attention row to its saved IDs."
  - truth: "Every finance source tab/action and inherited entry opens the correct saved record; no mock figure or toast-only finance effect remains."
    status: partial
    reason: "The Phase 10 overview omits the source's monthly pro-rata earned premium because no versioned coverage earning schedule feeds it."
    artifacts:
      - path: apps/backoffice/components/finance/overview.tsx
        issue: "Written, cash, balance and ageing values are displayed; earned premium is explicitly unavailable."
      - path: backend/src/BackOffice.Infrastructure/Finance/FinanceLedgerService.cs
        issue: "The ledger exposes posted money, not a pinned monthly coverage earning basis."
    missing:
      - "Persist or derive a pinned coverage earning schedule with dates and exact amounts, then display earned premium for the selected period with its basis."
  - truth: "Full current-source backend/frontend/browser and retained-demo restart prove reconciled finance journeys; phase status reflects actual evidence only."
    status: partial
    reason: "The retained demo contains zero RefundRequest and FinanceRefundPayment rows, so refund approval/payment outcome and retry cannot be read back after the required restart."
    artifacts:
      - path: .local/phase10-final/preservation/report.json
        issue: "Both before and after snapshots report zero refund requests and zero refund payments."
      - path: scripts/verify-finance-restart-readback.mjs
        issue: "The seven readbacks cover agency, policy, receipt, statement, bordereau, submission and period, but no refund/payment."
    missing:
      - "Use a legitimate saved refund and deterministic demo payment in the retained scenario, then verify exact refund, approval, payment, provider operation, cash posting and retry state after owned restart without resetting preserved data."
---

# Phase 10: Accounting and insurer reporting verification

**Phase goal:** Reconcile policy movements through finance workflows.
**Verified:** 2026-09-25T11:54:07Z
**Status:** gaps_found
**Mode:** Initial goal-backward verification of the current implementation. The prior report had an interim `partial` status and no structured `gaps:` section.

## Goal achievement

The three roadmap success criteria have strong current-source automated evidence. The Phase 10 source contract and plan 10-12 add two overview obligations that are observably absent. Plan 10-13 also required a retained refund/payment restart readback that was not performed because the retained database has no legitimate row. The phase goal is therefore not fully achieved, despite the successful numbered acceptance gate.

### Observable truths

| # | Must-have truth | Status | Code and behavior evidence |
| --- | --- | --- | --- |
| R1 | Statements and ledger balances reconcile with policy issue, MTA, cancellation and allocation movements. | VERIFIED | `FinanceLedgerService.Load` joins posted insurance journals/obligations and finance postings; `FinanceStatementService.GenerateAsync` fixes one source cutoff and persists opening, movements, closing and exact bytes. Native SQL gate passed 576/576; retained statement hash survived restart. |
| R2 | Receipts cannot be overallocated; refund authority and duplicate-payment checks hold under retries. | VERIFIED | `FinanceReceiptService.AllocateAsync` locks receipt/invoice residuals and rejects excess. `FinanceRefundService` checks collected same-payee credit, reservations and independent approval; `FinancePaymentService` uses one saved operation/work path. Native SQL payment fail-once and duplicate-apply case verifies one paid cash posting. This proves backend behavior, not retained-demo refund restart. |
| R3 | Invalid bordereaux cannot submit; corrections, exclusions, CSV export and demo submission persist; closed-period corrections remain auditable. | VERIFIED | `FinanceSubmissionService.QueueAsync` validates exact version, hash, bytes and zero validation issues before saving work; `FinanceBordereauService` creates immutable successor versions; `FinancePeriodService.PostCorrectionAsync` links a new posting to a closed source. Live browser passed 21 checks; submitted historical CSV hash survived restart. |
| P01 | Every original finance identity and inherited caller has a requirement, contract and planned runtime proof. | VERIFIED | `finance-source-ledger.json` lists 24 direct, 3 inherited and 11 implied IDs; all 38 are in `10-12-BINDINGS.md`. Independent `node --test tests/finance-source.test.mjs tests/finance-contracts.test.mjs` passed 7/7. Coverage mapping is complete even though two bindings are partial. |
| P02 | Scoped ledger counts each posted source once, includes historic first issues and holds an open period for new issue. | VERIFIED | `FinanceLedgerService`, issue writer, accounting-period SQL guards and `FinanceLedgerRealSqlTests` are substantive and wired through `MapFinanceLedger`. Full native SQL gate includes the family. |
| P03 | Statements obey opening plus signed movements equals closing with immutable cutoff/bytes and current download authority. | VERIFIED | `FinanceStatementMath.Reconcile`, persisted `FinanceStatementVersion`, route authorization and exact saved download are implemented; restart readback matched SHA-256 `AC8BA91A36B20272A92E4211D1D51657DE94BC90E8B14555A4B0E9BE387BFE7F`. |
| P04 | Receipt and invoice residuals cannot go negative under concurrent commands and every cash change has durable source/reversal audit. | VERIFIED | Ordered `UPDLOCK,HOLDLOCK` invoice reads, serializable command boundary, source-bound finance postings and reversal rows exist; `FinanceReceiptRealSqlTests` cover races and reversals. |
| P05 | Receipt/account pages navigate saved identities and show allocation/residual state after conflict. | VERIFIED | `FinanceWorkspace` routes saved IDs to `FinanceReceipts`/`FinanceAccounts`; client reads scoped API data and retains a command key for uncertain retries. Live browser opens the saved receipt and account; dedicated earlier plan browser evidence covers allocation/reversal. |
| P06 | Bank evidence can be matched or explained without dropping imports or inventing a balancing entry. | VERIFIED | `FinanceReconciliationService` and guarded routes store bank lines, matches, exclusions, target variances and reversals; native SQL cases cover distinct same-value imports and mismatches. |
| P07 | Only collected, unreserved credit becomes a refund, with independent versioned approval. | VERIFIED | `FinanceRefundService.RequestAsync` calculates eligible credit against invoice debt, collected allocations and reservations; `DecideAsync` checks current scope, approval authority, distinct actor and row version. Native SQL refund family exists and passed in the unfiltered run. |
| P08 | A retry or crash cannot pay the same refund twice, and the user sees the saved outcome. | VERIFIED | `FinancePaymentService` persists work/operation, `FinancePaymentWorker` applies one result, and the payment detail route/UI expose queued, failed and paid states. Native SQL fail-once case checks one provider operation and one refund cash posting after duplicate apply. |
| P09 | A versioned bordereau has stable source rows, validation and CSV bytes; corrections/exclusions are auditable. | VERIFIED | `FinanceBordereauService` snapshots journals, creates successor versions, stores CSV bytes/hash and reasoned member changes. Browser corrected the second failed member, excluded the third and downloaded an exact valid file. |
| P10 | Only a validated exact version can be demo submitted; retries do not create another submission. | VERIFIED | `FinanceSubmissionService.QueueAsync` checks current/saved version and exact hash before queuing one durable work item; SQL uniqueness and native cases cover replay. Browser saved one submission. |
| P11 | Closed periods reject late mutations; later adjustments are separately posted and linked. | VERIFIED | `FinancePeriodService` reviews blockers, closes with held SQL state and posts a linked correction in a new open period. Native SQL tests include pending payment close denial and direct mutation guards. |
| P12 | Every source tab/action and inherited entry reaches saved data, with no mock figures or toast-only effects. | FAILED | Seven tabs and inherited links are wired, but `FinanceOverview` lacks saved pending refund count/direct row target and the monthly earned premium required by `10-UI-SPEC.md` and the source ledger. Two of 38 bindings remain partial. |
| P13 | Full current-source gate and retained-demo restart prove finance journeys with honest phase status. | FAILED | Ten gate stages and seven retained readbacks passed, but the plan explicitly calls for refund/payment retry readback. Preservation report shows zero `RefundRequest` and `FinanceRefundPayment` rows. The previous report honestly disclosed this but used a noncanonical `partial` status. |

**Score:** 14/16 truths verified. No verification override exists or was applied.

## Required artifacts and wiring

| Plan | Artifact check | Actual wiring and data flow |
| --- | --- | --- |
| 10-01 | Contract, source ledger and contract tests exist and are substantive. | The source IDs resolve to 38 binding rows; 7/7 focused contract/source tests pass. |
| 10-02 | `FinanceRecords.cs`, `FinanceModel.cs`, `AccountingPeriods.cs` exist. | Registered EF model/period guards feed `FinanceLedgerService`; `Program.cs:307` maps ledger endpoints. SQL journals and postings, not static arrays, produce rows. |
| 10-03 | Statement service, records and endpoints exist. | `Program.cs:308` maps generation/detail/download; service reads the ledger and persists snapshot bytes/hash. |
| 10-04 | Cash records, receipt service and endpoints exist. | `Program.cs:311` maps receipt/assignment/allocation/reversal; SQL receipt and allocation rows feed detail/residual views. |
| 10-05 | Finance API, receipt and account components exist. | `FinanceWorkspace` renders both; `finance-api.ts` fetches real API responses and uses saved IDs. |
| 10-06 | Bank records, reconciliation service and endpoints exist. | `Program.cs:312` maps bank line and reconciliation routes; UI loads saved lines/results. |
| 10-07 | Refund records, service and endpoints exist. | `Program.cs:313` maps request/detail/decision and SQL data flow. The missing list/count is a separate Phase 10 source gap. |
| 10-08 | Payment service, records and endpoints exist. | `Program.cs:314` maps queue/detail/resume; dispatcher/worker use durable outbox and provider-operation rows. |
| 10-09 | Bordereau records, service and endpoints exist. | `Program.cs:309` maps generation, member changes, validation and exact download; SQL source journals feed immutable versions. |
| 10-10 | Submission service, records and endpoints exist. | `Program.cs:310` maps exact-version submit/detail; outbox and adapter attempts feed saved status. |
| 10-11 | Period service, endpoints and correction records exist. | `Program.cs:315` maps close/correction; SQL period, source and correction rows feed reviews. |
| 10-12 | Planned `accounting-workspace.tsx` was renamed to substantive `workspace.tsx`; route and reconciliation components exist. | `(workspace)/[section]/page.tsx:18-20` renders the real workspace. Seven imported tab components read saved API data. Overview refund count and earned premium data flows remain absent. |
| 10-13 | Verification, validation and state files exist, but this report records incomplete acceptance. | Restart script verifies seven real persisted identities; its refund/payment branch is absent. Central state/requirements are owned by the orchestrator and should not be marked complete from this report. |

Plan key links to `FINANCE-CONTRACTS.md` are design traceability links, not literal imports. Runtime links were checked from the workspace to API fetch/command helpers, `Program.cs` route registration to services, and each service to EF/SQL saved records. No finance service is an orphan. The missing refund list/count and earned schedule are data-flow failures, not merely missing display labels.

### Key link verification

| From | To | Mechanism | Status |
| --- | --- | --- | --- |
| Accounting route | Finance workspace | `(workspace)/[section]/page.tsx:18-20` renders `FinanceWorkspace` under the finance role | WIRED |
| Finance workspace | Seven tab components | Direct imports and conditional rendering in `workspace.tsx:7-43` | WIRED |
| Tab components | API endpoints | `finance-api.ts` and `finance-workspace-api.ts` fetch/command functions consume responses and save URL identities | WIRED |
| API endpoints | SQL-backed services | `Program.cs:307-315` maps every finance endpoint family; DI registers services at lines 83-91 | WIRED |
| Services | Posted insurance/cash/bank/refund/batch data | EF queries and serializable command transactions return stored rows and exact bytes | WIRED |
| Overview refund attention | Saved pending refund rows | No authorised list/count API, no saved row ID at call site | NOT WIRED |
| Overview earned premium | Pinned earning schedule | No coverage earning source exists; the UI states the figure is unavailable | NOT WIRED |

### Dynamic data-flow trace

| Artifact | Rendered variable | Upstream source | Status |
| --- | --- | --- | --- |
| `FinanceOverview` | `rows`, `account`, `cash`, `periods` | `ledgerAll`, `accountSummary`, `receiptList`, `/finance/periods`; SQL saved rows | FLOWING for written/cash/balance/ageing |
| `FinanceReceipts` and `FinanceAccounts` | Selected receipt, allocation, statement and account | Agency-scoped list/detail endpoints backed by receipts, allocations and statement versions | FLOWING |
| `FinanceBordereaux` | Batch/version/member/submission state | Saved batch, member, CSV and outbox/adapter records | FLOWING |
| `FinanceRefunds` | Selected refund/payment | Saved detail endpoints for an entered or newly created ID | FLOWING for detail; no discovery/count feed |
| `PolicyFinance` | `page.items` | `/api/v1/policies/{id}/finance` uses policy-scoped ledger SQL reads | FLOWING |
| Overview pending refund and earned premium | No state/data variable | No scoped refund aggregate or earning schedule source | DISCONNECTED |

## Current-source acceptance evidence

| Check | Result | Evidence |
| --- | --- | --- |
| Unfiltered backend unit | 1,394 passed, zero failed/skipped | `.local/phase10-final/unit/vilmo_DESKTOP-SCF19PJ_2026-09-24_18_28_01_net10.0.trx`; independent TRX `Counters` check |
| Unfiltered native SQL integration | 576 passed, zero failed/skipped, 6h33m | `.local/phase10-final/sql/vilmo_DESKTOP-SCF19PJ_2026-09-25_05_47_14_net10.0.trx`; independent TRX `Counters` check |
| Strict discovered/executed inventory | 1,970 unique cases and 511 real SQL; thresholds 1,887/485 | `scripts/assert-test-results.ps1` acceptance result recorded in `10-13-SUMMARY.md`; checker also rejects six malformed/stale/duplicate fixture variants |
| Root and frontend Node | 451/451 and 214/214, zero skips | `.local/phase10-final/pnpm-test.log`, `web-test.log`; summary counters inspected |
| Lint, typecheck, isolated production build | All exit zero | `.local/phase10-final/web-lint.log`, `web-typecheck.log`, `web-build.log`; logs inspected |
| Finance browser | 21 saved-record checks, zero errors | `.local/phase10-12-browser/browser-report.json`; `.local/phase10-final/finance-browser.log` |
| Operational browser aggregate | Ten collectors plus retained-demo stage passed | `.local/operational-suite/2026-09-25T11-37-39-509Z/report.json` |
| Focused read-only spot checks during this verification | Finance source/contract 7/7; workspace 5/5 | `node --test tests/finance-source.test.mjs tests/finance-contracts.test.mjs`; `node --test apps/backoffice/tests/finance-workspace.test.mjs` |
| Conventional/declared shell probes | None declared or found for this phase | No `probe-*.sh` path in Phase 10 plans/summaries or `scripts/` |

The final SQL gate used a current-source isolated Next build manifest (`apps/backoffice/.local/next-phase10-acceptance/operational-source-manifest.json`, BUILD_ID `3z_baOqKegNZGAEm8txvZ`), with 270 matching source hashes. Earlier interrupted runs and focused fixture diagnostics are documented in `10-13-SUMMARY.md` and are excluded from the 1,970 denominator. A later three-case servicing *test-fixture-only* repair passed separately; the successful full SQL TRX predates that repair and no product source changed afterward.

## Retained-demo data-flow trace

Two additive `--initialize-demo` runs preserved fingerprint `B488B4F61F832F23721FA3EB1C0A29BE1B4306822EEDB720490A7C98E27F9E8C`, 97,198 business rows, 198 tables and 215 retained files each time; migration count 104 and head `20260924152124_FinancePeriodCloseCorrections` stayed fixed (`.local/phase10-final/preservation/report.json`). After the owned API/web restart, seven readbacks in `.local/phase10-final/restart-readback.json` confirmed agency, policy transaction, receipt/allocation, statement, bordereau, submission and period. Statement SHA-256 was `AC8BA91A36B20272A92E4211D1D51657DE94BC90E8B14555A4B0E9BE387BFE7F`; submitted historical CSV SHA-256 was `5E790ABB965F639ED0787B170E36AADE25292CAE1498B44C6108EFE07ADF912F`.

The same preservation report records **zero refund requests and zero refund payments** before and after both runs. The finance browser's refund check at `scripts/verify-finance-workspace-browser.mjs:176-177` only confirms the credit-ID input exists; it does not submit, approve, pay or read back a refund. The native SQL cases do prove refund/payment service invariants, but they are isolated fixtures and cannot substitute for the retained restart proof required by plan 10-13.

## Requirements coverage

All Phase 10 requirement IDs in `REQUIREMENTS.md` are claimed by plan frontmatter; there is no orphaned Phase 10 requirement. Plan 10-01, 10-12 and 10-13 claim FIN-01 through FIN-08 and POL-01, with narrower owning plans in between.

| Requirement | Code-level status | Remaining phase evidence |
| --- | --- | --- |
| FIN-01 ledger/agency balances | Verified by real SQL ledger/account and linked policy UI. | Overview's earned-premium source obligation is still missing. |
| FIN-02 period statements | Verified with persisted equation, immutable bytes and exact restart hash. | None identified. |
| FIN-03 receipts/allocations | Verified with real SQL residual/race/reversal tests and saved UI reads. | None identified. |
| FIN-04 bank reconciliation | Verified with saved import, match, variance, reversal and audit paths. | None identified. |
| FIN-05 refund approval/payment | Service invariants verified by real SQL, including retries and one posting. | Retained refund/payment restart readback missing; overview pending count missing. |
| FIN-06 bordereaux | Verified with versioned SQL rows, validation, correction/exclusion and hashed CSV. | None identified. |
| FIN-07 demo submission | Verified with exact valid version, one durable submission and retained historical readback. | No real external submission claimed. |
| FIN-08 period close/correction | Verified with SQL close guards and separate linked correction. | None identified. |
| POL-01 linked policy finance contribution | `PolicyFinance` fetches `/api/v1/policies/{id}/finance` under current policy/finance scope and links to the saved agency account. | Compound cross-phase requirement should remain under central review until the whole policy record checklist is signed off. |

## Anti-pattern and disconfirmation check

No unreferenced `TBD`, `FIXME` or `XXX` marker was found in the touched finance services, endpoints, workspace components or final-gate scripts. Intentional `return null` branches represent absent optional lookups or unowned work leases; they do not feed a permanent empty finance view. No unresolved HIGH/CRITICAL source change is evident from this review.

The misleading positive check is the browser's refund row: it records a passing label after finding an input, without executing the refund lifecycle. An uncovered path in the retained scenario is provider payment outcome after restart. The UI requirements are also partial even though `finance-workspace.test.mjs` says every control has a *recorded* binding: that test checks mapping presence, and the mapping itself labels two bindings `partial`.

Phase 12 defines general dashboard/report measures, and Phase 13 defines final acceptance, but neither explicitly takes ownership of the Phase 10 accounting overview's two named source obligations. Phase 13 also says it cannot compensate for missing phase-level tests. No item is deferred under the roadmap filter.

## Human verification

Business review of money labels, assistive-technology interaction and the complete real user refund journey remain appropriate in Phase 13. The current `gaps_found` status is already determined by observable missing implementation and retained data proof; human review is not being used to turn those failures into `UNCERTAIN`.

## Gaps summary

The backend ledger, cash, refund, reconciliation, bordereau, submission and period services are substantive, wired and covered by the final native SQL gate. To close Phase 10, provide a scoped refund list/count and direct saved-record attention target; add an exact, versioned monthly earning basis and earned-premium display; then run a legitimate retained refund/payment scenario through approval, deterministic demo provider outcome and owned restart readback. Preserve the existing demo fingerprint and document any additive data changes explicitly.

---

_Verified: 2026-09-25T11:54:07Z_
_Verifier: gsd-verifier_
