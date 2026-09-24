---
phase: 10-accounting-and-insurer-reporting
plan: '08'
subsystem: finance-payments
tags: [finance, refunds, outbox, sql-server, reconciliation]
requires: [10-02, 10-04, 10-06, 10-07, 10-10]
provides: [retry-safe-demo-refund-payment, durable-provider-outcome-recovery, paid-refund-cash-source]
affects: [10-11, 10-12, 10-13]
tech-stack:
  added: []
  patterns: [stable-provider-operation, outcome-before-local-application, source-bound-refund-posting, audited-same-work-resume]
key-files:
  created:
    - backend/src/BackOffice.Infrastructure/Finance/FinancePaymentService.cs
    - backend/src/BackOffice.Infrastructure/Finance/FinancePaymentWorker.cs
    - backend/src/BackOffice.Infrastructure/Persistence/FinancePaymentRecords.cs
    - backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260924141817_FinanceRefundPayments.cs
    - backend/src/BackOffice.Infrastructure/Persistence/Migrations/FinancePaymentMigrationSql.cs
    - backend/src/BackOffice.Api/FinancePaymentEndpoints.cs
    - backend/src/BackOffice.Api/FinancePaymentDispatcher.cs
    - backend/tests/BackOffice.IntegrationTests/FinancePaymentRealSqlTests.cs
  modified:
    - backend/src/BackOffice.Infrastructure/Platform/SqlJobLeases.cs
    - backend/src/BackOffice.Infrastructure/Finance/FinanceReconciliationService.cs
    - backend/src/BackOffice.Api/Program.cs
    - contracts/openapi.json
    - docs/design/FINANCE-CONTRACTS.md
decisions:
  - One approved refund permits one active payment; only a definite provider rejection permits a reasoned linked successor.
  - A saved accepted provider result can complete local posting after submitter role revocation or period close, while a new provider send requires current originator authority.
  - A failed no-result work item resumes with the same payment and operation only after current finance authority, If-Match, key and an audited reason are supplied.
requirements: [FIN-05]
requirements-completed: []
metrics:
  completed: 2026-09-24
  tasks: 2
  tests: 3 native SQL, 5 API contracts, 2 affected native reruns
---

# Phase 10 Plan 08: Retry-safe demo refund payments

An approved and reserved refund now uses one durable local provider operation and creates one cash posting only after a saved accepted outcome; uncertain and rejected outcomes remain visible and recoverable without a second payment.

## What changed

- `FinanceRefundPayment` pins the approved request, credit, original debtor and payee, GBP amount, scenario version, request hash, operation key and outbox work. Queueing requires current finance authority, the request ETag, a reason and an idempotency key. A second key cannot create another active payment; a changed intent under the same key conflicts.
- `FinancePaymentWorker` persists the deterministic demo provider result before applying it locally. The exact saved accepted or rejected result can be recovered after timeout or process restart. An accepted result remains eligible for local application if the original submitter loses their role or the original accounting period closes; the refund posts in the next eligible open period. New provider execution still checks the original submitter's current internal finance role.
- Local acceptance applies one balanced, source-bound `FinancePosting` with negative `CashDelta` and equal positive `DebtorDelta` in a transaction. A definite rejection applies no cash and permits a reviewed, linked successor. The service and SQL guards reject forged provider outcome, mismatched source, premature cash, mutable payment identity and paid/rejected history.
- Failed work with no durable provider result can be resumed under the same payment/work/operation after finance authority is restored. `POST /api/v1/finance/payments/{id}/resume` requires current scope, If-Match, key, CSRF and a review reason, records an audit event, and rejects accepted/rejected saved results. The payment detail distinguishes queued, uncertain, provider acknowledged/application pending, paid, rejected and failed states.
- Paid refund cash is now an eligible signed bank reconciliation source in both service and SQL guards. The migration adds the payment table, checks and trigger bindings; `HasPendingModelChanges()` is false. OpenAPI, generated TypeScript and finance contracts describe the runtime routes.

## Verification

| Check | Result | Evidence |
| --- | --- | --- |
| Fresh native SQL payment filter | 3 passed, 0 failed, 0 skipped | `.local/phase10-08-resume/phase10-08-resume.trx` |
| Affected native SQL resume-authority case | 1 passed, 0 failed, 0 skipped | `.local/phase10-08-resume-auth/phase10-08-resume-auth.trx` |
| Affected native SQL accepted-outcome denial case | 1 passed, 0 failed, 0 skipped | `.local/phase10-08-accepted-resume-denial/phase10-08-accepted-resume-denial.trx` |
| Payment and refund API contracts | 5 passed, 0 failed | `node --test tests/finance-payment-contracts.test.mjs tests/finance-refund-contracts.test.mjs` |
| API build | 0 warnings, 0 errors | `dotnet build backend/src/BackOffice.Api/BackOffice.Api.csproj --no-restore -v:q` |
| OpenAPI lint | Valid, 104 pre-existing warnings | `node scripts/lint-openapi.mjs` |
| Diff and model | Whitespace check clean; native SQL model check false | `git diff --cached --check`; native SQL cases |

The native cases use migrated disposable SQL Server databases and real issued/cancelled policy, collected cash, posted credit, refund approval, outbox lease and provider-operation rows. They prove timeout after saved acceptance, lease exhaustion and recovery on the same operation, crash before local apply, role revocation, closed-period carry-forward, no duplicate posting, source forgery rejection, signed refund bank match, definite rejection and two reviewed linked retries, a fail-once transient, and denied same-key replay after authority revocation. The reviewed resume case retains payment/work identity, records its reason, denies a revoked actor, and denies accepted/rejected outcomes.

## Review and handoff

- T10-08's duplicate-payment threat is closed for the local demo adapter and application: provider effect and cash application are separately durable, and saved acceptance is recovered instead of replaced. No real bank or insurer call occurred.
- 10-11 close must count acknowledged but unapplied refund work and preserve signed reconciliation exceptions. 10-12 can present the saved payment IDs and states. FIN-05 remains pending because downstream UI and phase verification remain.
- Accepted outcomes use the existing work and operation with a payment-specific recovery budget after the initial 6/12/18 attempts. A failed no-result work item requires an explicit, audited resume and cannot silently create a successor. The existing internal `finance` role supplies `finance-read` and `finance-payment-execute`; a standalone execute-only role was not introduced.
- Retained demo DB, keys, files and preview were untouched. Inherited `next-env.d.ts`, `tsconfig.json`, `.idea/` and preview-generated `AGENTS.md`/`CLAUDE.md` were not staged.

## Deviations from Plan

**[Rule 2 - Durable accepted outcome recovery]** A generic six-attempt terminal cap could strand a saved accepted provider result after repeated local apply failures. The payment work now escalates through allowed caps and keeps the exact accepted operation recoverable; rejected results remain terminal. Verified by the native SQL lease-exhaustion case. Commit: `4b9282f`.

**[Rule 2 - Authority-restored same-work resume]** A revoked submitter before any provider outcome left failed work without a safe reviewed path after authority returned. Added a scoped, versioned and audited resume of the same payment/work/operation, with SQL transition checks and native denial/recovery proof. Commit: `4b9282f`.

**[Rule 2 - Paid refund reconciliation binding]** The prior bank-match SQL allowlist accepted receipt cash only. The payment migration and service extend it together for a saved paid refund source with signed residual and native SQL proof. Commit: `4b9282f`.

## Threat Flags

| Flag | File | Description |
| --- | --- | --- |
| threat_flag: finance-write-endpoints | `backend/src/BackOffice.Api/FinancePaymentEndpoints.cs` | Queue and resume routes enforce current authority, CSRF, version and replay controls. |
| threat_flag: payment-evidence-schema | `backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260924141817_FinanceRefundPayments.cs` | New financial/payment evidence and SQL source guards bind provider outcome, posting and reconciliation. |

## Self-Check: PASSED

Implementation commit `4b9282f`, summary, created files and all listed native SQL TRX reports exist. The commit deleted no tracked files. Only 10-08 implementation, contracts, tests and finance contract text were staged; central phase trackers remain with the orchestrator.
