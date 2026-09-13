# Local setup

Current status: SQL foundation is implemented. The API exposes liveness and the web app displays setup status. Local sign-in, current account and sign-out APIs are implemented. The prototype shell and worker implementation remain pending in Phase 2.

Prerequisites: .NET SDK 10.0.401, Node 24 and pnpm 11.19.0. Version pins live in global.json and package manifests; pnpm-lock.yaml covers the workspace. Do not change frontend-code, which is reference material.

From the repository root in PowerShell:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.local/dotnet'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
dotnet restore backend/BackOffice.slnx --configfile NuGet.Config --packages .local/nuget
dotnet build backend/BackOffice.slnx --no-restore
pnpm install --frozen-lockfile
pnpm --filter @cover/backoffice build
pnpm --filter @cover/backoffice typecheck
node scripts/validate-contracts.mjs
```

Run API and web in separate terminals:

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

COVER_SQL_CONNECTION optionally overrides the native Windows connection; its database must be exactly CoverMGA_Demo. The command migrates and seeds transactionally without changing existing passwords or records. Six fictional users have addresses servicing@cover.example, underwriter@cover.example, senior-underwriter@cover.example, agency-admin@cover.example, finance@cover.example and system-admin@cover.example. Sign-in is available through the auth API; its browser UI follows in 02-04. Products are deliberately draft foundation definitions without rating rules. DemoClock starts frozen at 2026-09-13 12:00 UTC.

For an intentional destructive reset of this demo database only, add --reset-demo after --initialize-demo. The command validates its target and password before deletion. Never use it to reset any other database. Repeated initialization without reset preserves existing data.

For this agent-created local demo, the randomly generated password is retained only in ignored .local/demo-password.txt; it is not printed or committed. Load it into COVER_DEMO_PASSWORD for repeat initialization. Authentication SQL/API tests pass; browser UI follows in 02-04.

## Optional Docker SQL profile

Set MSSQL_SA_PASSWORD in your local environment and run `docker compose up -d sql`. Data stays in the named sql-data volume, and SQL listens only on 127.0.0.1:14333. Use a SqlConnectionStringBuilder to set Server=127.0.0.1,14333, Database=CoverMGA_Demo, User ID=sa, Password from MSSQL_SA_PASSWORD, Encrypt=true and TrustServerCertificate=true; assign its ConnectionString to COVER_SQL_CONNECTION without printing it. For tests, COVER_SQL_TEST_CONNECTION selects the server/credentials; the suite always substitutes its own generated database name.

The profile follows Microsoft's [SQL Server container guidance](https://learn.microsoft.com/en-us/sql/linux/quickstart-install-connect-docker?view=sql-server-ver17), using SQL Server 2022 Developer and mssql-tools18. The 2022-latest tag tracks development updates; record an image digest before relying on an identical container build. Native SQL is the verified runtime on this host. Docker runtime verification is pending because Docker is stopped; this is not claimed as a tested container deployment.


## Local authentication API

Use GET /api/v1/auth/csrf to establish the antiforgery cookie and retrieve requestToken. POST /api/v1/auth/login with JSON email/password, the cookie and X-CSRF-Token. After successful login, fetch a new CSRF token for the authenticated identity. GET /api/v1/account returns the current actor. POST /api/v1/auth/logout requires the authenticated CSRF token and revokes the SQL session. All API responses use Cache-Control: no-store. Unknown JSON properties are rejected.

Development uses cover-dev-session with HttpOnly, Path=/ and SameSite=Lax. Sessions have a fixed eight-hour maximum; SQL status, security stamp, expiry and current roles are checked for every authenticated request. Five bad passwords lock the local credential for 15 minutes; a separate per-direct-IP limiter permits 20 login requests/minute. These use real UTC time, independently of the frozen business demo clock. MFA-enabled or must-reset credentials cannot sign in until their full flows are implemented.

Data Protection keys persist under the API content root's .local/data-protection directory by default; Cover__DataProtectionPath can select an explicit persistent directory. Windows keys are protected with the current user's DPAPI. A restart must retain the same key directory and OS identity. Non-development startup requires an explicit key path, enforces HTTPS and uses the Secure __Host-cover-session cookie without a Domain attribute. Non-Windows production hosting is blocked until encrypted key storage is configured; this milestone has not deployed a production environment. No forwarded headers are trusted automatically.

Re-run --initialize-demo after pulling migrations. Test hosts use independent databases and key directories in ignored .local/auth-test-keys; tests restart the ASP.NET host and reuse the original cookie to verify persisted keys/tickets and SQL revocation. API tests do not replace browser or human acceptance checks.
