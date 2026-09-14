# Local setup

Current status: SQL persistence, local authentication, prototype shell and durable diagnostic operations are implemented and locally verified. Client identity, agency relationships, contacts and restricted support flags are also implemented; matching and insurance workflows remain in later plans. The native foundation acceptance gate passed on 2026-09-14; human UAT and hosted CI remain unperformed.

For the verified native preview on API port 5087/web port 3100 and a short business-facing walkthrough, follow [DEMO.md](DEMO.md). The general development defaults below use API port 5080.

Prerequisites: .NET SDK 10.0.401, Node 24 and pnpm 11.19.0. Version pins live in global.json and package manifests; pnpm-lock.yaml covers the workspace. Do not change frontend-code, which is reference material.

From the repository root in PowerShell:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.local/dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet restore backend/BackOffice.slnx --configfile NuGet.Config --packages .local/nuget --locked-mode
dotnet build backend/BackOffice.slnx --no-restore
pnpm install --frozen-lockfile
pnpm --filter @cover/backoffice build
pnpm --filter @cover/backoffice typecheck
node scripts/validate-contracts.mjs
```

Complete SQL initialization below before starting the API. Then run API and web in separate terminals:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project backend/src/BackOffice.Api --no-restore --urls http://127.0.0.1:5080
pnpm --filter @cover/backoffice dev
```

The web app proxies /api/* to BACKOFFICE_API_ORIGIN, default http://127.0.0.1:5080. Liveness is http://127.0.0.1:5080/health/live and does not prove database readiness. Actual /api/v1 features are added with authentication and persistence. Do not expose this development profile publicly.

This host has SQL Server 2022 Developer at .\SQL2022 (16.0.1200.5), verified with Windows authentication. The application database is CoverMGA_Demo; uniquely named integration databases use CoverMGA_Test_*. Initialization provisions only this application database. SQL access and NuGet restore may need execution outside Codex's sandbox due to Windows credential/configuration access; this does not require a SQL password. Other user databases remain outside this project's scope.

Next uses webpack on this Windows host because Turbopack's PostCSS child-process creation failed with access denied, including outside the sandbox. Webpack compiled successfully; do not disable CSS or type checks to work around that failure. Build/typecheck results and remaining setup work are recorded in Phase 2 checkpoints. Native SQL is the immediate runtime; compose.yaml provides the alternative SQL container profile.

Root shortcuts: pnpm backend:restore (locked), pnpm backend:build, pnpm test:backend, pnpm web:build, pnpm web:typecheck and pnpm web:lint. Keep pnpm test for the design-contract suite. Backend central package versions are in backend/Directory.Packages.props; per-project packages.lock.json files are committed. On this host use the DOTNET_CLI_HOME setting above and run restore outside the sandbox if Windows denies NuGet user-config access.

Initial backend tests include exact signed money, strict API-format parsing, rounding/overflow and API host wiring. The integration suite also creates a unique CoverMGA_Test_* database, migrates and seeds twice, verifies SQL constraints and rowversion concurrency, then deletes only that run's database. SQL unavailability fails the suite.

ESLint is pinned to 9.39.5 because the React/import/accessibility plugins bundled by eslint-config-next 16.3.5 do not support ESLint 10. The registry marks ESLint 9 unsupported; upgrade the plugin set and ESLint together when compatibility is available. pnpm permits only the unrs-resolver native binding postinstall, required by this lint configuration.


## SQL initialization

Set COVER_DEMO_PASSWORD locally (at least 12 characters with upper/lower case, number and symbol), then run:

```powershell
dotnet run --project backend/src/BackOffice.Api --no-restore -- --initialize-demo
```

COVER_SQL_CONNECTION optionally overrides the native Windows connection; its database must be exactly CoverMGA_Demo. The command migrates and seeds transactionally without changing existing passwords or records. Six fictional users have addresses servicing@cover.example, underwriter@cover.example, senior-underwriter@cover.example, agency-admin@cover.example, finance@cover.example and system-admin@cover.example. Sign-in is available through the auth API and /login browser screen. Products are deliberately draft foundation definitions without rating rules. DemoClock starts frozen at 2026-09-13 12:00 UTC.

For an intentional destructive reset of this demo database only, add --reset-demo after --initialize-demo. The command validates its target and password before deletion. Never use it to reset any other database. Repeated initialization without reset preserves existing data.

For this agent-created local demo, the randomly generated password is retained only in ignored .local/demo-password.txt; it is not printed or committed. Load it into COVER_DEMO_PASSWORD for repeat initialization. Authentication SQL/API and real-browser checks pass.

## Optional Docker SQL profile

Set MSSQL_SA_PASSWORD in your local environment and run `docker compose up -d sql`. Data stays in the named sql-data volume, and SQL listens only on 127.0.0.1:14333. Use a SqlConnectionStringBuilder to set Server=127.0.0.1,14333, Database=CoverMGA_Demo, User ID=sa, Password from MSSQL_SA_PASSWORD, Encrypt=true and TrustServerCertificate=true; assign its ConnectionString to COVER_SQL_CONNECTION without printing it. For tests, COVER_SQL_TEST_CONNECTION selects the server/credentials; the suite always substitutes its own generated database name.

The profile follows Microsoft's [SQL Server container guidance](https://learn.microsoft.com/en-us/sql/linux/quickstart-install-connect-docker?view=sql-server-ver17), using SQL Server 2022 Developer and mssql-tools18. The 2022-latest tag tracks development updates; record an image digest before relying on an identical container build. Native SQL is the verified runtime on this host. Docker runtime verification has not been performed; this is not claimed as a tested container deployment.


## Local authentication API

Use GET /api/v1/auth/csrf to establish the antiforgery cookie and retrieve requestToken. POST /api/v1/auth/login with JSON email/password, the cookie and X-CSRF-Token. After successful login, fetch a new CSRF token for the authenticated identity. GET /api/v1/account returns the current actor. POST /api/v1/auth/logout requires the authenticated CSRF token and revokes the SQL session. All API responses use Cache-Control: no-store. Unknown JSON properties are rejected.

Development uses cover-dev-session with HttpOnly, Path=/ and SameSite=Lax. Sessions have a fixed eight-hour maximum; SQL status, security stamp, expiry and current roles are checked for every authenticated request. Five bad passwords lock the local credential for 15 minutes; a separate per-direct-IP limiter permits 20 login requests/minute. These use real UTC time, independently of the frozen business demo clock. MFA-enabled or must-reset credentials cannot sign in until their full flows are implemented.

Data Protection keys persist under the API content root's .local/data-protection directory by default; Cover__DataProtectionPath can select an explicit persistent directory. Windows keys are protected with the current user's DPAPI. A restart must retain the same key directory and OS identity. Non-development startup requires an explicit key path, enforces HTTPS and uses the Secure __Host-cover-session cookie without a Domain attribute. Non-Windows production hosting is blocked until encrypted key storage is configured; this milestone has not deployed a production environment. No forwarded headers are trusted automatically.

Re-run --initialize-demo after pulling migrations. Test hosts use independent databases and key directories in ignored .local/auth-test-keys; tests restart the ASP.NET host and reuse the original cookie to verify persisted keys/tickets and SQL revocation. API tests do not replace browser or human acceptance checks.


## Frontend verification and preview

Run pnpm web:test for validation/error-message unit checks, pnpm web:lint, pnpm web:typecheck and pnpm web:build. The font is extracted from the supplied prototype into public/fonts by scripts/extract-prototype-font.mjs, with no runtime font CDN request. The shell matches the source's 238px sidebar and 62px header. Missing business metrics show an unavailable marker; future-feature pages do not pretend to save data.

For a built preview on a different API port, set BACKOFFICE_API_ORIGIN before both build and start: Next rewrites are compiled at build time, and server-rendered account requests use the runtime value. This host had an unrelated Docker listener on 5080 during browser verification; the test API used 5087 and web used 3100. Run the API with --urls http://127.0.0.1:5087 and Development environment, set BACKOFFICE_API_ORIGIN=http://127.0.0.1:5087, build the frontend, then run pnpm --filter @cover/backoffice start --hostname 127.0.0.1 --port 3100.

With these local processes running, pnpm web:browser executes the Chrome journey using the fictional servicing account and the ignored local password file (or COVER_DEMO_PASSWORD). COVER_WEB_ORIGIN defaults to http://127.0.0.1:3100 and rejects non-local hosts. Chrome must be installed; COVER_BROWSER_CHANNEL can select another installed Playwright browser channel. Screenshots are written to ignored .local/browser-evidence. The test checks login, validation, reload, logout, replay of a revoked session, network errors, admin denial, account-tab availability, keyboard focus, responsive layout and source/app screenshots. Visual inspection is recorded separately from automated assertions and does not claim human UAT.

## Durable operations demo

Re-run --initialize-demo to apply the current migrations and seed four diagnostic scenario versions. Sign in as system-admin@cover.example, open Admin > Integrations, choose a scenario and run a probe. The service performs no external calls: provider outcomes, attempts, local receipts, audit and command replay all persist in SQL. The latest-probe panel polls completion; Refresh jobs loads the current table. Admin > Audit log filters exact event codes such as diagnostic.completed or diagnostic.batch-retry-requested.

The Development worker runs unless Cover__DiagnosticWorkerEnabled=false. Production hosts expose no diagnostic mutation routes or dispatcher. A probe has an initial allowance of six attempts; an administrator can authorize two more six-attempt recovery cycles for exhausted transient failures, preserving provider identity and global attempt history. Definitive rejection/invalid payload/conflict cannot be retried. Bulk recovery is atomic, requires a reason and current ETags, and safely replays a successful command after a lost response.

Run pnpm web:browser:operations with the same native SQL/API/web preview to exercise the operational screens in Chrome. The harness requires sqlcmd and integrated access to .\SQL2022/CoverMGA_Demo. scripts/seed-browser-recovery.sql refuses every other database and inserts two explicitly fictional exhausted jobs with six simulated attempt records each; it does not reset or delete existing data. Each run uses new IDs. Fixtures and audit remain as demo history; their request JSON and audit reason label them as browser fixtures. Never run this harness against a production database.

The operational journey verifies success/rejection/timeout recovery, reload persistence, bulk recovery with a deliberately lost successful HTTP response, safe list failure recovery, audit filtering, mobile table scrolling and role denial. It exercises job paging when the seeded history exceeds a page. Screenshots are saved under ignored .local/browser-evidence/admin-*.png. Human UAT and later business workflow acceptance remain separate.
## Continuous integration

.github/workflows/foundation.yml has three jobs: web/contracts; Linux with an actual SQL Server 2022 service container; Windows with an owned SQL Server 2022+ LocalDB instance and the full DPAPI/production-cookie suite. Linux excludes the two test classes that exercise Windows production key protection; the Windows job runs all cases. Neither job silently skips unavailable SQL. Windows explicitly fails if a suitable LocalDB runtime cannot start. Tests always replace the database name with their own CoverMGA_Test_<guid> and delete only that owned database.

scripts/assert-test-results.ps1 requires both unit/integration TRX reports, passing counters with no skipped cases, a minimum total and named real-SQL scenarios. scripts/test-result-gate.ps1 verifies rejection of skipped/failed/missing/undersized reports. The native run on this host passed 46 cases, including nine real-SQL scenarios. The workflow YAML and Windows startup script were parsed locally; GitHub-hosted execution and its container/LocalDB provisioning have not been run here. These are CI definitions, not a claim of a hosted green build.

The split follows GitHub's [Linux-only service-container requirement](https://docs.github.com/en/actions/tutorials/use-containerized-services/use-docker-service-containers). The [Windows runner inventory](https://github.com/actions/runner-images/blob/main/images/windows/Windows2025-Readme.md) lists the Visual Studio LocalDB component; the workflow checks the actual available engine version before testing. Setup actions follow official [setup-dotnet](https://github.com/actions/setup-dotnet) and [setup-node](https://github.com/actions/setup-node) usage. The SQL container password is a run-specific disposable CI value, never a production credential. The workflow has read-only repository permission and performs no deployment.

To reproduce the native report gate after restore, use a new results directory for each run so old reports cannot inflate the counts:

```powershell
$foundationResults = Join-Path '.local' ('test-results-' + [Guid]::NewGuid().ToString('N'))
dotnet test backend/BackOffice.slnx --no-restore --logger trx --results-directory $foundationResults
if ($LASTEXITCODE -ne 0) { throw 'Backend tests failed.' }
./scripts/assert-test-results.ps1 -ResultsDirectory $foundationResults -MinimumTests 121 -MinimumSqlTests 18
./scripts/test-result-gate.ps1
```

These minima include Phase 3 matching storage: 97 unit +24 integration cases, including eighteen named SQL scenarios, pass locally. Integration fixtures run serially to avoid SQL model-database CREATE DATABASE lock contention; explicit concurrent writers/leases/API requests inside tests still run concurrently. They should increase as subsequent phases add coverage. Compose configuration was validated with `docker compose config --quiet` and a disposable environment password; no SQL container was started for that check.

The client-persistence migration adds 32 fictional client identities, 35 agency relationships and two draft agencies on repeatable initialization. Stable seed IDs preserve existing edits. Client references use a SQL sequence and may contain gaps after rollback. Agency drafts do not assert completed onboarding. Client HTTP routes now support scoped list/search, create/edit, agency relationships and safe activity. Writes require current permissions, CSRF and a command key; updates and relationship creation also require the client ETag. Quote/policy records return explicit unavailability until implemented.

Client screens now support list/search/filter/page, create and edit identity, agency relationships, reload and safe activity. Sign in as servicing@cover.example and open Clients. Search accepts business, client/company reference and agency name/reference. Open a client and choose Add agency relationship to select an agency through paged discovery; linking a draft agency does not complete its onboarding. Contact and support servicing are available on Contacts after choosing an agency relationship. Run pnpm web:browser:clients against the same local preview to verify a uniquely labelled fictional client, lost-response create/link replay, validation/stale recovery, real agency paging, read-only access and mobile layouts. This harness retains created demo clients and their audit history; it never resets the database. Prototype and application screenshots are saved under .local/browser-evidence. Policy and quote tabs remain explicitly unavailable until their owning phases.

Migration 20260914023721_RelationshipContacts adds Person and Contact tables, separate declared names, consent JSON and primary/ending/relationship constraints. It is applied to the native demo. The Contacts tab supports add/edit, primary selection, consent and ending with retained history. The third demo client has a shared person with separate declarations across two agencies. Run pnpm web:browser:contacts for its lifecycle and error checks.

Support migration 20260914041804_SupportFlags is applied to the native demo. Servicing and underwriting staff can choose a contact on Contacts, then add, amend, review, end and inspect restricted support history. Consent Declined clears the unsaved sensitive draft. Sharing requires explicit active relationship/contact membership and separate functional wording. The labelled agency-safe preview omits internal category, reason and hidden totals. Internal agency-admin can preview granted wording but cannot read internal flags/history. Reinitialization seeds one shared and one internal-only fictional flag on the third client only when no flags exist, preserving edits and ended history. Run pnpm web:browser:support for consent, lifecycle, interrupted-save replay, stale recovery, grant withdrawal and responsive role checks. Browser harnesses retain labelled fictional records and history.

Migration 20260914054253_MatchIntakeEvidence adds immutable match intake/comparison evidence, separate current associations, and append-only decisions/information requests. Repeatable initialization adds MI-DEMO-0001 through MI-DEMO-0006 across the two fictional agencies: four pending, one declined and one queried. These are saved intake examples with no quote or policy IDs. Existing cases and trails are retained on reinitialization. Matching HTTP routes and screens are not yet implemented; this storage increment does not enable a review workflow.
