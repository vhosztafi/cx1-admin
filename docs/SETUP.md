# Local setup

Current status: foundation scaffold only. The API exposes liveness and the web app displays setup status. Database migrations, seeded sign-in, domain endpoints and worker implementation are still pending in Phase 2.

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
dotnet run --project backend/src/BackOffice.Api --no-restore --urls http://127.0.0.1:5080
pnpm --filter @cover/backoffice dev
```

The web app proxies /api/* to BACKOFFICE_API_ORIGIN, default http://127.0.0.1:5080. Liveness is http://127.0.0.1:5080/health/live and does not prove database readiness. Actual /api/v1 features are added with authentication and persistence. Do not expose this development profile publicly.

This host has SQL Server 2022 Developer at .\SQL2022 (16.0.1200.5), verified with Windows authentication. The future application database is CoverMGA_Demo; uniquely named integration databases use CoverMGA_Test_*. No database has been created or reset by the scaffold. SQL access and NuGet restore may need execution outside Codex's sandbox due to Windows credential/configuration access; this does not require a SQL password. Other user databases remain outside this project's scope.

Next uses webpack on this Windows host because Turbopack's PostCSS child-process creation failed with access denied, including outside the sandbox. Webpack compiled successfully; do not disable CSS or type checks to work around that failure. Build/typecheck results and remaining setup work are recorded in Phase 2 checkpoints. Native SQL is the immediate runtime; portable Docker configuration follows with migrations.
