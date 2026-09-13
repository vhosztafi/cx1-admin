---
phase: 02-application-and-persistence-foundation
plan: '02'
status: complete
requirements: [FND-01, FND-04, FND-05]
completed: 2026-09-13
---

# Real SQL foundation and repeatable fictional seeds

Commit 5e8ba9d delivers 17 foundational tables, two EF migrations, SQL checks/foreign keys/unique indexes/rowversions, UTC instant checks and a published-product validity trigger. Infrastructure contains persistence records and mappings; Domain remains independent of EF. Mutable saves stamp UpdatedAt. SQL compatibility is explicitly 160. The pinned EF CLI is in .config/dotnet-tools.json.

CoverMGA_Demo was provisioned on native .\SQL2022 using Windows authentication. Two separate API initialization processes completed successfully. A separate sqlcmd process confirmed compatibility 160, six users, three draft product versions, two migrations and the frozen demo clock. The random local demo password is only in ignored .local/demo-password.txt and hashes in SQL; no plaintext password was logged or committed. Reinitialization preserves existing records/passwords. Reset is implemented behind the explicit --initialize-demo --reset-demo flags and exact database-name/attached-file guard; no reset was run on the demo.

Final validation: dotnet build succeeds with zero warnings/errors; locked restore succeeds; 11 domain cases and six integration cases pass with zero skips. The integration cases include four reset-target guard inputs, API host behavior and a real-SQL scenario. The SQL scenario migrates and seeds twice, reloads through a fresh context, verifies hashed credentials, rejects duplicate/invalid JSON/FK/state/non-UTC records, rejects overlapping published product ranges while allowing adjacent ranges, and detects stale rowversion updates. Each run uses and removes only its own randomly named CoverMGA_Test_* database. No in-memory provider or SQL-unavailable skip exists.

Compose configuration validates with docker compose config --quiet. Docker's user config was inaccessible in the sandbox, but syntax validation exited 0. Container startup is not verified because this host uses native SQL and Docker is stopped. The optional image tag tracks SQL Server 2022 development updates; setup docs state this reproducibility limitation.

Inline review: mappings contain no cascade deletes, raw tokens or plaintext passwords; seeds use a serializable transaction and transaction-owned application lock; reset validates before connection/mutation; test cleanup owns its generated target; incomplete product definitions remain draft. Agency and WorkRecord relationships are added when their owning tables exist. Profile/MFA/account approval tables belong to later account work; adapter state semantics and append-only audit enforcement belong to 02-05. DATA-MODEL.md records these implementation boundaries and the DemoClock addition.

Next: 02-03 local cookie authentication, SQL session storage/revocation, antiforgery and permission checks. Phase 2 and its FND requirements remain in progress. No browser acceptance, sign-in or human UAT success is claimed.
