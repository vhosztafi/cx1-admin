---
phase: 02-application-and-persistence-foundation
plan: '06'
status: complete
requirements: [FND-01, FND-02, FND-03, FND-04, FND-05, FND-06]
completed: 2026-09-14
---

# Foundation acceptance gate

Completed the native Windows/SQL Server foundation gate, inline code/security/UI reviews, CI definitions, result-gate checks and setup/demo runbook. Review found a mismatched diagnostic setting scope could execute different JSON scenario behavior; fixed and tested in 36fee75. No unresolved material foundation finding remains. See 02-REVIEW.md and 02-UI-REVIEW.md for scope and limitations.

Final verification on 2026-09-14: full backend suite 32 unit +14 integration tests, nine named real-SQL scenarios, zero skips; all 53 design checks; six frontend tests; zero-warning lint; TypeScript; production webpack build with API5087; shell and operations real-Chrome journeys. Browser checks include two-page navigation and lost-response batch replay. Test-owned preview processes were verified and stopped. The source funnel has no diff.

The documented compiled-API initializer successfully reapplied migrations/seed to CoverMGA_Demo without reset. SQL integration tests create a fresh owned database, migrate/seed twice and verify independent reload, constraints and rowversion. Authentication tests plus prior actual-process smoke prove session restart behavior; DiagnosticDispatcherTests again exercise two real API processes and provider-success-before-local-completion recovery. Browser reload and a newly started built web preview retain stored records.

CI adds web/contracts, Linux SQL container and Windows full SQL/DPAPI profiles, failing explicitly for missing SQL or skipped/insufficient test reports. Six result-gate positive/negative cases pass. Compose config validates without starting a container. GitHub-hosted jobs and optional Docker runtime remain unexecuted; native SQL2022 is the verified route.

docs/SETUP.md documents pins, restore, initialization, authentication, ports, test reports and CI boundaries. docs/DEMO.md gives exact native preview commands, account walkthrough, diagnostic scenarios and labelled recovery fixtures. Secrets remain ignored. No production deployment, real external action, human UAT or future business workflow acceptance is claimed.

All six Phase 2 plans are complete. Next: plan and implement Phase 3 clients/contact servicing within the approved autonomous scope.
