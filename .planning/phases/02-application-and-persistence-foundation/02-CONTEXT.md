# Phase 2 context

User authorises autonomous implementation. Phase 1 contracts are the baseline; fix discovered inconsistencies with tests instead of bypassing them. Build Next.js/TypeScript/Tailwind and .NET 10 modular API with SQL Server; frontend-code remains read-only. Prototype IBM Plex Sans, compact tables, pale backgrounds, blue record headers and persistent sidebar are authoritative.

Scope: runnable solution, migrations/seeding, local login/logout/revocable sessions, actual API permission/CSRF checks, shell/loading/error states, durable audit/job primitives, integration tests and repeatable setup. Later feature phases add their actual screens and records; do not display fake business success or claim those features work now. Full account/MFA administration is Phase 11; foundation identity must support its designed storage and security primitives.

Local SQL Server 2022 Developer at .\SQL2022 is running and accepts Windows authentication outside the sandbox. Use only a new CoverMGA_Demo database and uniquely named CoverMGA_Test_* test databases; do not inspect or modify other user databases. Docker is installed but stopped, so native SQL is the immediate runtime, with Compose provided for portability. No production deployment. Reset requires exact designated demo database plus explicit CLI switch; never reset at normal startup.

Dependency registry evidence: Node 24.19.0, .NET SDK 10.0.401 installed; Next 16.3.5, React 19.3.0, Tailwind 4.3.3 and EF SQL Server 10.0.12 available on 2026-09-13. Pin selected versions and lock restores. Windows integrated SQL access requires sandbox escalation; no database password is needed for native development.
