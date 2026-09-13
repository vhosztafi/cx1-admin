# Foundation gate checkpoint

02-06 is IN PROGRESS. Plans 02-01 through 02-05 are complete; Phase 2 and FND requirements remain open.

## Completed this increment

Commit 591a914 adds .github/workflows/foundation.yml: web/contracts on Ubuntu, real SQL Server 2022 container integration profile on Ubuntu, and Windows2025 full identity/SQL profile using an owned LocalDB instance. Linux excludes AuthenticationTests and OperationalJobTests because those classes exercise Windows-only production DPAPI behavior; Windows runs the entire suite. Windows checks available LocalDB major >=16 before startup and fails explicitly if unavailable. No tests silently skip SQL failure. All database tests generate and clean only their CoverMGA_Test_GUID names.

CI setup actions use current official examples (checkout@v7, setup-node@v7, setup-dotnet@v6; pnpm action v4). Workflow has contents:read, no deployment, no stored production secrets. Docker service health uses mssql-tools18; service credentials are disposable per run. Official runner docs list a LocalDB component but do not establish its engine version, so the guard is intentional. GitHub-hosted jobs have NOT been executed; YAML and extracted Windows startup PowerShell parse locally. Do not claim hosted CI/container/LocalDB provisioning succeeded.

Native full backend run passes 32 unit +14 integration =46 cases, including nine named real-SQL scenarios, zero skips. Reports are in ignored .local/foundation-gate-results. scripts/assert-test-results.ps1 verifies reports, all-pass counters and minimum real-SQL coverage. scripts/test-result-gate.ps1 passes six positive/negative cases (valid, skipped, failed, missing SQL/report, undercount). Test-generated cases are ignored .local/trx-gate-cases-*.

docs/SETUP.md status/locked restore/initialization ordering corrected and CI profiles/limitations documented with primary sources. GSD code-review and ui-review skills/workflows read; review will be done inline per spawn restriction. No formal full code/security/UI review is complete yet.

## Resume next

1. Review all phase foundation source and tests, including cross-file identity/authorization/CSRF, idempotency/leases/provider/inbox, rowversion/manual retry, JSON/SQL guards, safe UI and setup scripts. Write REVIEW.md with findings/resolution and a UI-REVIEW.md using the actual source contract/browser evidence. Fix material findings. GSD workflow config keeps these gates enabled; no subagents.
2. Finish repeatable setup and demo instructions. docs/DEMO.md does not yet exist. Native SQL fresh/repeated migration and seed are exercised in SqlFoundationTests; actual API restart/session persistence and worker recovery are covered. Assess any remaining fresh documented-command or process restart gaps. Compose config can be validated without claiming container runtime; do not disturb unrelated Docker service on 5080.
3. Re-run relevant full design/frontend/SQL/browser/build gates after changes, record only actual evidence, then 02-06-SUMMARY/VERIFICATION and FND requirement status. No human UAT or future business features passed by inference. CI remote execution is unperformed, not a verified deployment.

Previous operational browser gate: pnpm web:browser:operations and shell regression pass; screenshots visually inspected and selector/mobile table issues fixed in 53b9045. APIs/UI implemented through 02-05-SUMMARY.md. Test-owned servers stopped. Password remains ignored .local/demo-password.txt. Native .\SQL2022/CoverMGA_Demo; API5087/web3100 preferred for tests with matching Next build/runtime BACKOFFICE_API_ORIGIN. Keep frontend-code unchanged.

YAML parse tooling here: Node dependency node_modules/.pnpm/js-yaml@4.3.2/node_modules/js-yaml is available. Bundled Python has no PyYAML and there is no direct yaml package; avoid repeating failed discovery. No project dependency was added for the one-off parse.
