---
phase: 03
slug: clients-and-contact-servicing
status: in_progress
nyquist_compliant: false
wave_0_complete: true
created: 2026-09-14
---

# Phase 3 validation strategy

Existing infrastructure: xUnit with real SQL Server, Node test runner, TypeScript/ESLint/Next build and local Playwright Chrome. No new framework or mock database is needed. The table below is the phase coverage plan; completed 03-01 through 03-03 evidence is recorded in their summaries. Support flags, matching and final cross-feature verification remain pending.

After a contract task: `node scripts/validate-contracts.mjs` and `node --test tests/party-contracts.test.mjs`. After backend tasks: relevant xUnit filter plus the full suite after each plan (`dotnet test backend/BackOffice.slnx --no-restore`). After frontend tasks: `pnpm web:test`, `pnpm web:lint`, `pnpm web:typecheck` and browser smoke; build before full preview acceptance. SQL checks are generally seconds to minutes and must fail if SQL is unavailable. Fix a failed boundary before advancing its dependent work.

| Tasks | Requirement | Threat | Automated evidence planned |
|---|---|---|---|
| 03-01-1/2 | All CLI | T-01 | Party schema positive/negative cases plus all source/control contracts; no consent state loss or restricted safe fields |
| 03-01-3 | All CLI | T-02 | CommandBoundaryTests real SQL original ETag replay, changed intent and rollback |
| 03-02-1 | CLI-01/02 | T-03 | PartyValidationTests and ClientTests schema/reseed/reference/FK/UTC/JSON checks |
| 03-02-2/3 | CLI-01/02 | T-04 | ClientTests real cookies/CSRF/capability/scope/cursor/ETag tests; browser create/edit/search/reload |
| 03-03-1/2/3 | CLI-02 | T-05 | ContactRulesTests and ContactTests concurrency, rollback, primary/end/consent/person-isolation; browser lifecycle |
| 03-04-1/2/3 | CLI-03 | T-06 | SupportFlagTests unit/SQL/API grant isolation/revocation, non-persistence on declined consent, redacted activity and restricted history; browser safe preview |
| 03-05-1/2/3 | CLI-04 | T-07 | MatchRulesTests and MatchTests decision matrix/concurrency/replay/reopen/candidate isolation; browser all outcomes |
| 03-06-1/2/3 | All CLI | T-08 | verify-clients-browser, full design/frontend/backend suites, CI count gate updates, inline security/UI review and truthful evidence docs |

Browser command to add: `pnpm web:browser:clients`, local API5087/web3100 only, unique fictional data with no reset. Current shell/operations browser tests remain regressions. Capture actual source/app evidence at 1560x1000 and 390px; human judgment/assistive-technology acceptance remains separate.

Sign-off remains pending execution. Set nyquist_compliant true only when all planned boundary cases have passing evidence. Do not inflate CI minimums using old TRX directories, and do not count hosted CI as run merely because its YAML exists.

03-01 evidence (2026-09-14): tasks 1/2 pass all 62 design tests and OpenAPI lint, preserving 949 controls/five conditional rules. Task 3 passes full native 46-case backend suite and a final targeted CommandBoundary SQL regression, including old receipt compatibility, original response ETag and invalid-header rollback.

03-02 evidence (2026-09-14): all tasks complete. Final full backend passes 65 cases including eleven real SQL scenarios, zero skips (.local/client-agency-results). Nine web unit cases, lint/typecheck/build and real Chrome create/edit/reload/search/page, uncertain response replay, 422/stale recovery, denied writes and mobile checks pass. Final agency closeout adds scoped name/reference discovery, real cursor paging, header names, lost-response link replay and stale parent link recovery. Foundation browser regression passes. Source/client desktop and mobile captures inspected. See 03-02-SUMMARY.md and retained progress history for the single earlier post-rebuild detail timeout and subsequent passing runs. Rows 03-03 onward and phase sign-off remain pending.

03-03 Task 1 evidence (2026-09-14): ContactRulesTests adds thirteen unit cases; ContactTests verifies real SQL schema/primary/FK/consent/UTC/ending/rollback/stale constraints and scoped person reuse, including historical and revoked relationships. Migration applied to Demo. Final full suite passes 79 cases/twelve SQL scenarios, zero skips (.local/contact-storage-serial-results). Initial parallel fixture setup hit SQL model CREATE DATABASE locks; serial fixture scheduling resolved this, preserving explicit concurrency within tests. Contact lifecycle service/API/UI and seeds remain pending; a storage rollback test is not counted as a completed HTTP lifecycle test.
