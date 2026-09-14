---
phase: 03
slug: clients-and-contact-servicing
status: complete
nyquist_compliant: true
wave_0_complete: true
created: 2026-09-14
---

# Phase 3 validation strategy

Existing infrastructure: xUnit with real SQL Server, Node test runner, TypeScript/ESLint/Next build and local Playwright Chrome. All six plans are complete; 03-VERIFICATION.md is the current sign-off. Earlier dated checkpoints below remain historical. No mock database or skipped SQL acceptance is used.

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

Implemented browser commands: `pnpm web:browser:clients`, `web:browser:contacts`, `web:browser:support`, `web:browser:matches`, plus shell/operations regressions. Local API5087/web3100 only, fictional data with no reset. Actual source/app evidence captured at1560x1000 and390px; human judgment/assistive-technology acceptance remains separate.

Sign-off: all planned Phase 3 boundary cases have passing evidence. Fresh full backend123/19 (98unit+25integration) has zero skips, plus targeted actor-label API regression; 63contracts,15frontend tests, lint/typecheck/build and browser journeys pass. Result-gate rejection tests and CI YAML parsing pass; hosted execution unperformed. CLI-01 remains partial for real quote/policy links; this is a planned cross-phase obligation, not an untested client-foundation boundary.

## Final evidence mapping (2026-09-14)

| Plan/threat | Final evidence |
|---|---|
| 03-01 / T-01,T-02 | tests/party-contracts.test.mjs and full contract validation; CommandBoundaryTests replay/original ETag/rollback |
| 03-02 / T-03,T-04 | PartyValidationTests, ClientTests, ClientApiTests; client browser create/edit/filter/page/scope/retry/stale |
| 03-03 / T-05 | ContactRulesTests, ContactTests, ContactServiceTests, ContactApiTests; contact browser all consent/primary/end/shared-person flows |
| 03-04 / T-06 | SupportFlagRulesTests and support storage/service/API/demo tests; support browser declined draft, sharing/revocation/review/history/end/replay/role denial |
| 03-05 / T-07 | MatchRulesTests, MatchStorageTests, MatchApiTests; match browser all outcomes, retained separate identity, stale/replay/role denial and accessible status |
| 03-06 / T-08 | .local/phase3-final-clean-results full123/19; .local/phase3-actor-fix-results targeted API pass; final build/lint and six browser suites; code/UI reviews and truthful requirements/backlog |

The initial full-suite attempt was rejected because the active preview locked API DLLs. The clean run after stopping that process passed. A shell test raced streamed content; visible heading/sidebar waits repaired the harness while preserving exact dimensions. Human UAT is not included in this automated sign-off.

03-01 evidence (2026-09-14): tasks 1/2 pass all 62 design tests and OpenAPI lint, preserving 949 controls/five conditional rules. Task 3 passes full native 46-case backend suite and a final targeted CommandBoundary SQL regression, including old receipt compatibility, original response ETag and invalid-header rollback.

03-02 evidence (2026-09-14): all tasks complete. Final full backend passes 65 cases including eleven real SQL scenarios, zero skips (.local/client-agency-results). Nine web unit cases, lint/typecheck/build and real Chrome create/edit/reload/search/page, uncertain response replay, 422/stale recovery, denied writes and mobile checks pass. Final agency closeout adds scoped name/reference discovery, real cursor paging, header names, lost-response link replay and stale parent link recovery. Foundation browser regression passes. Source/client desktop and mobile captures inspected. See 03-02-SUMMARY.md and retained progress history for the single earlier post-rebuild detail timeout and subsequent passing runs. Rows 03-03 onward and phase sign-off remain pending.

03-03 Task 1 evidence (2026-09-14): ContactRulesTests adds thirteen unit cases; ContactTests verifies real SQL schema/primary/FK/consent/UTC/ending/rollback/stale constraints and scoped person reuse, including historical and revoked relationships. Migration applied to Demo. Final full suite passes 79 cases/twelve SQL scenarios, zero skips (.local/contact-storage-serial-results). Initial parallel fixture setup hit SQL model CREATE DATABASE locks; serial fixture scheduling resolved this, preserving explicit concurrency within tests. Contact lifecycle service/API/UI and seeds remain pending; a storage rollback test is not counted as a completed HTTP lifecycle test.
