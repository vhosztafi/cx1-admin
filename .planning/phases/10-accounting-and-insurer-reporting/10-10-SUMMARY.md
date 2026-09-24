---
phase: 10-accounting-and-insurer-reporting
plan: '10'
subsystem: finance-bordereau-submission
tags: [finance, bordereau, sql-server, deterministic-demo-adapter, idempotency]
requires: [10-09, 10-02]
provides: [exact-validated-version-submission, recoverable-demo-provider-outcome, version-specific-submission-history]
affects: [10-12, 10-13]
tech-stack:
  added: []
  patterns: [sealed-version-hash, durable-outbox-and-lease, saved-provider-result-before-local-apply, current-authority-before-replay]
key-files:
  created:
    - backend/src/BackOffice.Infrastructure/Finance/FinanceSubmissionService.cs
    - backend/src/BackOffice.Infrastructure/Finance/FinanceSubmissionWorker.cs
    - backend/src/BackOffice.Infrastructure/Persistence/FinanceSubmissionRecords.cs
    - backend/src/BackOffice.Infrastructure/Persistence/FinanceSubmissionModel.cs
    - backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260924111359_FinanceBordereauSubmissions.cs
    - backend/src/BackOffice.Api/FinanceSubmissionEndpoints.cs
    - backend/src/BackOffice.Api/FinanceSubmissionDispatcher.cs
    - backend/tests/BackOffice.IntegrationTests/FinanceSubmissionRealSqlTests.cs
    - scripts/openapi-finance-submissions.mjs
  modified:
    - backend/src/BackOffice.Infrastructure/Finance/FinanceBordereauService.cs
    - backend/src/BackOffice.Infrastructure/Platform/SqlJobLeases.cs
    - backend/src/BackOffice.Api/Program.cs
    - contracts/openapi.json
decisions:
  - One submission per batch pins the exact valid version, saved CSV SHA-256, scenario version, stable operation key and request hash.
  - A saved exact provider result permits recovery and a linked draft successor even if the submitter later loses finance authority; new provider execution and user replay require current authority.
  - A submitted historical version remains downloadable under current authority after a successor; a superseded unsubmitted draft cannot export.
requirements: [FIN-07]
requirements-completed: []
metrics:
  completed: 2026-09-24
  duration: approximately 75 minutes
  tasks: 2
  tests: 2 fresh native SQL, 4 API contract
---

# Phase 10 Plan 10: Exact bordereau demo submission

An authorized finance user can queue only the exact validated bordereau version and saved CSV hash. A deterministic local adapter records one provider effect per batch; timeout, process restart and local apply retry recover that same saved result without another effect.

## What changed

- `FinanceSubmissionService.QueueAsync(ActorContext, Guid batchId, Guid versionId, string contentHash, string key, Guid correlationId, CancellationToken)` checks current stored internal finance/provider authority before command receipt replay. It locks the batch, verifies the current valid version, zero validation issues, saved bytes and SHA-256, and creates a durable `FinanceBordereauSubmission` plus `OutboxWork` in one serializable command. The request pins a `SettingVersion` scenario and stable operation key. Same-key exact replay returns the original safe receipt; changed intent or another key cannot create a second submission. `DetailAsync` rechecks current authority and exposes version-specific work attempts, provider state and local applied state.
- `FinanceSubmissionWorker.ExecuteProviderAsync` and `ApplyAsync` separate the provider result from local application. The local demo adapter supports success, rejection, fail-once and timeout-after-success without network calls. It saves an exact `DemoProviderOperation` before a simulated lost acknowledgement. Retries and recovered leases read that operation; an exact saved result remains recoverable after a successor draft or submitter role revocation. A new send checks the submitter's current finance role inside the provider transaction. `AdapterAttempt`, `AdapterInbox` and `AdapterQuarantine` retain retry, applied result and changed-duplicate evidence. A rejected provider outcome is terminal but retains its operation and history.
- The migration adds `FinanceBordereauSubmission`, its unique batch/version/work/operation keys and SQL checks for state, provider and applied status. SQL triggers bind submission to its valid version, exact content hash, durable work and scenario, reject retained identity changes/deletion, and prevent a pending unacknowledged submission's batch head from advancing. A durable exact provider result allows a linked successor so recovery is not stranded. The migration seeds the local `success` scenario; later scenario versions remain explicit test/demo data. The model snapshot matches fresh migrations.
- `FinanceBordereauService.DownloadAsync` now permits an exact submitted historical version under current authority, while superseded unsubmitted drafts remain non-exportable. Mapping correction still requires the current head and is held while a submission has no durable result. Saved version bytes and hashes are rechecked on download.
- Runtime routes are `POST /api/v1/finance/bordereaux/{batchId}/versions/{versionId}/submissions` and `GET /api/v1/finance/bordereaux/{batchId}/versions/{versionId}/submission`. Both require `finance-bordereau`. POST requires CSRF, an idempotency key, exact version `If-Match`, a bounded JSON `contentHash`, and returns only safe identifiers. OpenAPI and generated TypeScript describe the implemented routes and separate status fields. The development-only dispatcher uses SQL leases and the local adapter; no real insurer is contacted.

## Verification

| Check | Result | Evidence |
| --- | --- | --- |
| Failing-first test | Compile failed on missing submission service/worker before implementation | `.local/phase10-10-red` |
| Fresh native SQL Server tests | 2 passed, 0 failed, 0 skipped; fresh migration per case, 1m08s | `.local/phase10-10-sql-verified/phase10-10-sql-verified.trx` |
| Changed-intent SQL follow-up | 1 passed, 0 failed, 0 skipped; affected case rerun after added assertion | `.local/phase10-10-changed-intent/phase10-10-changed-intent.trx` |
| API contract tests | 4 passed, 0 failed | `node --test tests/finance-submission-contracts.test.mjs tests/finance-bordereau-contracts.test.mjs` |
| API build | Passed with 0 warnings/errors as part of final native SQL build | `dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~RealSqlFinanceSubmission` |
| OpenAPI generation/lint | 493 operations; valid with 104 retained warnings | `node scripts/generate-openapi.mjs`; `node scripts/lint-openapi.mjs` |
| Staged whitespace | Passed | `git diff --cached --check` |

Native SQL proves invalid/unvalidated/wrong-hash refusal, exact same-key replay, same-key changed-version and different-key refusal, one successful provider operation, duplicate local apply, retained historical submitted CSV after mapping correction, superseded draft export denial, and current actor denial of detail/replay. The second fresh case proves timeout after saved acceptance, a successor before retry, two separate restarted worker instances, lease expiry after provider recovery, one local apply, one provider operation/inbox, three retained attempts, changed duplicate quarantine and definite provider rejection. It also revokes the submitter's finance role before recovery: user detail/replay are denied while the previously accepted exact provider result is still applied once. The database reports no pending model changes.

## Scope and downstream handoff

- The adapter is deliberately local and deterministic. It does not transmit to an insurer. The current `finance-bordereau` grant is the stored internal finance role plus provider scope. External broker access and the accounting UI are 10-12 work; final end-to-end verification remains 10-13. FIN-07 is therefore not marked complete by this backend slice alone.
- A batch has one submission intent. A later mapping correction creates a new unvalidated version but does not rewrite the submitted bytes or authorize a second submission for that batch. Current authorization still controls historical download and status.
- The final SQL evidence used fresh temporary databases only. Retained demo database, files and keys were untouched.

## Deviations from Plan

### Auto-fixed issues

1. **[Rule 3 - Blocking migration SQL]** A reserved `Values` column and cross-table text collations initially prevented migration. The seed column is quoted and operation-key trigger comparisons use explicit BIN2 collation. The final fresh migration suite passed 2/2.
2. **[Rule 1 - Recovery bug]** A pending submission originally held the batch head and then rejected provider recovery after a successor. The batch hold now releases after a durable matching result, and the worker reads that exact result before stale-head refusal.
3. **[Rule 1 - Recovery authorization bug]** A revoked submitter originally stranded an accepted but unapplied result. New provider execution checks current role in its write transaction; exact saved outcome recovery is allowed without restoring the former actor's grant. User detail and command replay still require current authority.
4. **[Rule 2 - Request bound]** Submission JSON is read with a 2048-byte cap even when `Content-Length` is absent, before parsing.

## Threat Flags

| Flag | File | Description |
| --- | --- | --- |
| threat_flag: scoped-network-command | `backend/src/BackOffice.Api/FinanceSubmissionEndpoints.cs` | New protected submission and status routes require current finance scope and bounded input. |
| threat_flag: retained-provider-outcome | `backend/src/BackOffice.Infrastructure/Persistence/Migrations/20260924111359_FinanceBordereauSubmissions.cs` | New SQL financial submission state and transition guards retain exact provider and local application evidence. |

## Task commits

1. `83f823b` — `feat(10-10): submit exact validated bordereau versions with durable demo recovery`.
2. `c694729` — `test(10-10): prove same-key changed bordereau intent is rejected`.

## Self-Check: PASSED

The implementation and follow-up test commits and named files exist. The final two-case TRX reports two native SQL passes with zero skips; the affected case follow-up reports one pass, and the contract run reports four passes. No tracked file deletion was included. Only inherited `next-env.d.ts`, `tsconfig.json` and `.idea/` remain outside this plan's changes.
