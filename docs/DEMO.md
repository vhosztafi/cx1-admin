# Foundation demonstration

This is the Phase 2 foundation, not the complete insurance MVP. Sign-in, account identity, navigation, diagnostic jobs, recovery and audit use persistent SQL data. Client servicing, quotes, policies, finance and configuration editing arrive in later phases. The supplied sales funnel remains unchanged.

## Start the native Windows preview

Follow SETUP.md for pinned prerequisites and locked dependency restore. The verified SQL instance is `.\SQL2022`; the demo database is `CoverMGA_Demo`. From the repository root, build and initialize without resetting:

```powershell
$env:DOTNET_CLI_HOME = Join-Path (Get-Location) '.local/dotnet'
dotnet build backend/BackOffice.slnx --no-restore
$env:COVER_DEMO_PASSWORD = [IO.File]::ReadAllText((Join-Path (Get-Location) '.local/demo-password.txt'))
try {
    dotnet backend/src/BackOffice.Api/bin/Debug/net10.0/BackOffice.Api.dll --initialize-demo
} finally {
    Remove-Item Env:COVER_DEMO_PASSWORD
}
```

The ignored password file exists on the original development workstation only. On a fresh workstation, set your own COVER_DEMO_PASSWORD as described in SETUP.md. Reinitialization preserves existing passwords and records; a different seed password does not change an existing account's password. No password belongs in source control or a presentation.

In one terminal, start the API and its Development diagnostic worker:

```powershell
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet backend/src/BackOffice.Api/bin/Debug/net10.0/BackOffice.Api.dll --urls http://127.0.0.1:5087
```

In a second terminal, build and start the web preview with the same API origin:

```powershell
$env:BACKOFFICE_API_ORIGIN = 'http://127.0.0.1:5087'
pnpm web:build
pnpm --filter @cover/backoffice start --hostname 127.0.0.1 --port 3100
```

Open `http://127.0.0.1:3100`. Keep these terminals open; stop only these processes with Ctrl+C when finished. Port 5087 avoids an unrelated listener found on 5080 on the original workstation. The API origin must be set before both frontend build and startup because the proxy rewrite is compiled into the build.

## Five-minute walkthrough

1. Sign in as `servicing@cover.example` using the local demo password. Refresh the page and open Manage account. The displayed name, email and role come from SQL. Business dashboard counts deliberately say they are unavailable.
2. Open Admin while signed in as servicing. Access is denied by the application and API. Log out, then sign in as `system-admin@cover.example`.
3. Open Admin > Integrations. Select **Successful delivery**, then **Run demo probe**. The latest probe moves to succeeded. Refresh jobs to see its saved receipt and attempt count.
4. Select **Provider rejection** and run another probe. It fails with `provider-rejected`; it cannot be selected for recovery because repeating a definitive rejection is not a valid retry.
5. Select **Timeout after provider success**. The first attempt records the timeout; the next succeeds after a short retry delay. The provider result is reused, with one local receipt. **Temporary failure, then success** demonstrates a separate transient failure scenario.
6. Open Admin > Audit log. Filter by `diagnostic.completed`, then clear the filter. Sign-in, inspection and command events are persisted. Ordinary audit rows contain reviewed summaries, not raw credentials, payloads or sensitive before/after values.
7. Refresh the browser, or restart the two preview processes with the same commands and OS account. Sign-in persists within its fixed eight-hour lifetime, and jobs/audit remain. Retain the API Data Protection key directory; deleting it invalidates existing cookies.

Browser refresh does not refresh the jobs table automatically; use **Refresh jobs**. The latest-probe panel polls until a terminal result. Dates display Europe/London; authentication and retry timing use real UTC independently of the frozen business demo clock.

## Recovery demonstration and checks

The four scenarios normally finish within one or two attempts. To exercise exhaustion and bulk recovery, run `pnpm web:browser:operations` against the running native preview. It inserts two new, explicitly fictional exhausted jobs and six simulated historical attempts per job, using only `.\SQL2022/CoverMGA_Demo`. It selects these jobs, supplies a reason and verifies recovery, including a deliberately lost successful response. It leaves fixtures and audit as demo history; it performs no reset or deletion. Repeated runs create new fixture IDs.

For a manual recovery demonstration, run `sqlcmd -S '.\SQL2022' -E -C -b -d CoverMGA_Demo -i scripts/seed-browser-recovery.sql`, refresh jobs and filter failed. Select the eligible fictional jobs, enter a reason and choose **Retry selected**. They receive a new six-attempt allowance while preserving prior attempts and provider identity. At most two extra cycles are permitted. Old definitive rejections remain ineligible.

`pnpm web:browser` checks the sign-in/shell journey. Both browser scripts require Chrome, use only localhost, and save screenshots under ignored `.local/browser-evidence`. Automated checks and agent visual review do not constitute business-user acceptance.

The diagnostics make no external calls, send no messages and move no money. Products are draft foundation definitions without rating rules. All six seeded role accounts are listed in SETUP.md. Docker and GitHub-hosted CI execution remain unverified; the native SQL profile is the demonstrated runtime.
