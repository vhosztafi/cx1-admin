---
phase: 02-application-and-persistence-foundation
plan: '01'
status: complete
requirements: [FND-01, FND-05]
completed: 2026-09-13
---

# Buildable application and test scaffold

Delivered pinned Next.js/TypeScript/Tailwind workspace and modular .NET API, Application, Domain and Infrastructure projects. Added central NuGet versions, locked restore, unit and host integration test projects, exact signed GBP value type, root validation commands and setup documentation. Business routes and sign-in remain unimplemented. Sales funnel reference unchanged.

Verification: locked .NET restore succeeds; 11 domain unit cases and one API host test pass. Next webpack production build, standalone TypeScript check and zero-warning ESLint pass. API liveness was verified on a running process. Source diff whitespace check passes. Existing Phase 1 gate recorded 53 design tests; scaffold checks do not imply runtime acceptance of those contracts.

Commits: f65abe0 initial scaffold/plans; b269cdb test, lint and dependency-lock completion.

Environment decisions: webpack is used because Turbopack worker startup fails on this host. ESLint 9.39.5 is temporarily pinned because Next's React/import/accessibility plugins reject ESLint 10; the registry marks 9 unsupported, so upgrade the compatible plugin set together. Only unrs-resolver's native postinstall is allowed. Native SQL Server 2022 connectivity is proven, but no project database exists yet. Portable Compose configuration belongs to 02-02 alongside migrations.

Next: 02-02 real SQL mappings, migration, guarded provisioning, fictional seeds and dedicated-database integration tests. FND requirements remain open until full Phase 2 verification.
