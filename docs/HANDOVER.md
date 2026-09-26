# Developer handover

## Start here

- [Demo](DEMO.md): short role-based walkthrough, then detailed fictional scenario preparation.
- [Setup](SETUP.md): pinned tools, restore, SQL initialization, auth and preview commands.
- [Acceptance](acceptance/ACCEPTANCE.md): current versus historical evidence and remaining human/runtime checks.
- [Implementation index](acceptance/implementation-evidence.json): all 949 original controls, current bindings and phase evidence. Regenerate with `node scripts/build-milestone-acceptance.mjs`.

The implementation is a local MVP, not a production deployment. Native SQL Server 2022, .NET 10, Next.js 16/React, Node 24 and pnpm are pinned in the repository. Use existing locks; do not update dependencies during a demonstration.

## Choose one environment

| Environment | Purpose and state |
|---|---|
| Retained `CoverMGA_Demo`, API 5087 / web 3100 | Preserved older binary and business examples. Keep its matching database, document files and Data Protection keys. Current identity/reviewer provisioning was not deployed here after an automatic approval rejection. |
| Owned Phase 11/12 fixture, API 5095 / web 3193 | Current administration/reporting acceptance. The database name and connection live only in ignored `.local/phase11-fixture/fixture.json`; credentials are in its separate password file. Three saved product quotes and one task are sufficient for reporting checks, not a complete issued-finance demo. |
| Fresh developer instance | Follow SETUP, set your own `COVER_DEMO_PASSWORD`, then `--initialize-demo`. The command permits only the named demo database and preserves existing records/passwords. Prepare the desired fictional scenarios from DEMO. Do not point fresh setup at somebody else's retained database. |

The browser restart/visual scripts expect the owned workstation fixture and intentionally fail without its exact database guard. For portable isolated SQL verification use the integration test helpers, which create unique `CoverMGA_Test_*` databases and delete only those generated databases. Ignored fixture metadata, cookies and passwords are not repository assets.

## Normal development

From the repository root, restore with the locked commands in SETUP. Run the API and frontend in separate terminals with the same API origin. `BACKOFFICE_API_ORIGIN` must be set before frontend build/start; `COVER_NEXT_DIST_DIR` gives a second preview its own build output. Webpack is used on this Windows host. Keep the documented key path and file storage location consistent across restarts.

Useful checks:

```powershell
node --test tests/*.test.mjs
pnpm web:test
pnpm web:typecheck
pnpm web:lint
dotnet test backend/tests/BackOffice.UnitTests --no-restore
# Target a concrete SQL behavior; the full integration suite takes hours.
dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~RealSqlReportingSavedExports
```

Phase 10 provides the broad SQL baseline; Phase 11/12 checks cover subsequent changes. There is no requirement to repeat the entire suite for each document or UI label. A cross-cutting runtime change may justify a broader run; record the reason, selected cases and source revision.

## Preserve, restart and recover

1. Record the intended database, API binary, frontend origin, key directory and file root. Save an example quote/policy/task reference before stopping.
2. Stop only the app processes you started, preferably Ctrl+C in their terminals. Do not kill all dotnet/node processes or stop the shared SQL service.
3. Start the same API with the same `COVER_SQL_CONNECTION`, `Cover__DataProtectionPath`, environment and file configuration; start the frontend with the matching origin/dist directory. Reopen the saved record and check history/balance. Persisted SQL sessions require the same key/OS identity and may legitimately expire or be revoked.
4. If an operation response was lost, use the same command identity and version where the UI offers retry. Do not fabricate a new payment/issue merely because the first response was not seen. Inspect saved operation/provider outcome first.
5. A real SQL-engine restart needs a dedicated maintenance window and the database owner: take/verify a native database backup and matching document/key backup, stop dependent apps, restart only that dedicated instance, verify recovery, then reopen the same records and compare hashes/balances. This engine restart was not performed on the shared workstation.

`--reset-demo` is destructive and requires `--initialize-demo`; never use it for recovery or to clear a failed workflow. Reinitialization applies current migrations/seeds, so it is also a deliberate database change. Restoring only SQL without matching immutable files/keys can break documents or authentication. Keep originals and verify a restore in a separate approved environment before replacing a retained copy.

## Troubleshooting

| Symptom | Check |
|---|---|
| Empty reports | Current role, inclusive London dates, applicable filters and real saved cohort. Finance is a separate capability. Earned premium accepts completed calendar months contained in a configured period. |
| 403 or missing action | Current identity/role/agency scope, suspended/reset state, source ownership. System administration does not imply underwriting or finance access. |
| 409 / 412 | Read the current revision/ETag and saved decision. Refresh and review; do not remove concurrency guards or overwrite issued history. |
| MFA/reset sign-in | Follow current TOTP/recovery/reset flow. Recovery codes are shown once, hashed, and consumed; never edit credentials directly. |
| Failed delivery/payment | Inspect saved job attempts and provider receipt. Retry only a classified retryable state with authority; a definite rejection or unknown acknowledged payment requires its existing reconciliation path. |
| Blank or slow development navigation | Check API origin, current listener, compiler output and route response. Cold webpack compilation can take tens of seconds. Restart only the owned frontend if its build artifacts are inconsistent; retain screenshots/logs of a reproducible failure. |
| Missing document | Verify its exact policy/template/file version and storage root. Do not regenerate history using current templates. |
| SQL test cannot connect | Check native instance/test connection, credentials and isolated target guard. Unavailable SQL is a test failure, not a skipped pass. |

## Module boundaries

| Area | Primary implementation |
|---|---|
| HTTP, auth/CSRF, service wiring | `backend/src/BackOffice.Api` |
| Application contracts/domain rules | `backend/src/BackOffice.Application`, `backend/src/BackOffice.Domain` |
| Current roles, sessions, MFA | Infrastructure `Identity`; administration approvals/configuration in `Administration` |
| Clients, support, matching, agencies | Infrastructure `Parties`, `Agencies` |
| Quote capture, rating, referral and first issue | Infrastructure `Quotes`, `Underwriting`, issued writer in `Policies` |
| MTA, renewal, cancellation, temporal history | Infrastructure `Policies` |
| Tasks, notes, messages, documents, MID/claims | Infrastructure `Operations`, durable jobs in `Platform` |
| Ledger, earnings, receipts, refunds, periods, bordereaux | Infrastructure `Finance` |
| Search/dashboard/reports/favourites/CSV | Infrastructure `Reporting` |
| SQL schema, triggers, immutable records/seeds | Infrastructure `Persistence`; additive EF migrations |
| Browser pages and domain workspaces | `apps/backoffice/app`, `components`, `lib`; generated API types under `contracts/generated` |
| Design source and contract generation | `docs/design`, `scripts/generate-openapi.mjs`; original prototype under `docs/prototype` |

A later improvement should start in the owning module, retain source/version ownership and money invariants, add only meaningful focused checks, and update generated contracts via their generator. The supplied `frontend-code` reference/funnel is outside this implementation.
