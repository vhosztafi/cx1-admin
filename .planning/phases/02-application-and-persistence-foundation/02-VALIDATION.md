---
phase: 02-application-and-persistence-foundation
status: complete
nyquist_compliant: true
wave_0_complete: true
updated: 2026-09-14
---

# Foundation validation coverage

| Requirement | Repeatable checks | Failure behavior covered |
|---|---|---|
| FND-01 | HostTests, SqlFoundationTests, production Next build and both browser scripts | SQL unavailable fails; no pending model changes; clean schema migrates |
| FND-02 | ActorContextTests, AuthenticationTests, verify-shell-browser.mjs | Anonymous/capability/CSRF denial, lockout, expiry/stamp/suspension/revocation, restart, network errors |
| FND-03 | Six frontend unit checks, lint/typecheck, verify-shell-browser.mjs and verify-operations-browser.mjs | Safe empty/error/denial states, keyboard/mobile, stale selection and lost response; rendered visual review separately recorded |
| FND-04 | SqlFoundationTests and documented repeat initializer | Repeat seed, password hashing, duplicate/FK/JSON/UTC/state/range guards, stale rowversion, forbidden reset target |
| FND-05 | Unit/integration TRX reports and assert-test-results.ps1/test-result-gate.ps1 | Missing SQL, absent reports, skipped/failed tests and undercounts fail explicitly |
| FND-06 | CommandBoundaryTests, JobLeaseTests, DiagnosticProviderTests, DiagnosticInboxTests, DiagnosticDispatcherTests, OperationalJobTests, BatchRetryTests, RetryScheduleTests, JobRetryBudgetTests; operations browser | Transaction rollback/replay conflict, concurrency, lease expiry/fencing/exhaustion, real process restart, changed setting, duplicate/quarantined callback, scope/cursor/ETag failure, atomic retry rollback and budget limits |

Final native result: 46 backend cases / nine named SQL scenarios, 53 design cases and six frontend cases pass with no skips; build/lint/typecheck and both browser journeys pass. Large integration scenarios deliberately exercise multiple related boundaries against real SQL. There is no in-memory substitute or conditional skip. Use SETUP.md for fresh output directories and the verified native profile.

Human visual/business acceptance remains unperformed. UI-REVIEW records agent inspection and limitations. Hosted CI definitions and optional Docker are not counted as executed evidence. Full milestone runtime acceptance remains pending.
