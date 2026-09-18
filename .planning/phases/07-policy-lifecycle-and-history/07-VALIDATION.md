---
phase: 07
slug: policy-lifecycle-and-history
status: approved
nyquist_compliant: true
wave_0_complete: false
created: 2026-09-17
---

# Phase 7 validation strategy

All16 plans and32 tasks have explicit automated checks. GSD structural and
decision coverage checks and inline semantic review pass; runtime tests remain
pending. wave_0_complete=false reflects that new tests/harnesses are created by
their owning implementation plans, not already run during planning.

## Infrastructure and cadence

Reuse xUnit in backend/tests/BackOffice.UnitTests and BackOffice.IntegrationTests,
Node test runner in tests and apps/backoffice/tests, and actual Chrome/SQL2022
browser harness patterns. No new test framework is required.

Run targeted pure/contract tests after each behavior change; run real SQL/API
tests for each command/migration boundary and actual browser readback for each UI
slice. Every task has an automated verification command in its PLAN. Execute
each command with fail-fast exit checking; semicolon-separated display commands
must not hide earlier failures. Reuse successful verification for unchanged
evidence-only commits rather than redundantly rerunning a browser suite.

Full backend baseline took about 37 minutes (838 tests,169 real SQL); final gate
must include added tests with fresh TRX paths and actual start cutoff. This runtime
is not a quick-feedback promise. Pure tests are the short feedback loop. Full
frontend/contract suites, retained37 browser journeys plus Phase6 scenarios,
servicing journeys, restart and extended44-set preservation are final obligations.

## Complete per-task map

All32 task commands are copied from their owning plans. New test/harness files
are implementation outputs and must exist with nonzero meaningful assertions
before their task can pass. Evidence-only task02 reuses unchanged successful
task01 runs and adds diff/source review; do not repeat expensive checks blindly.

| Task | Requirements | Threat | Automated gate | Runtime status |
| --- | --- | --- | --- | --- |
| 07-01-01 | POL-02, POL-04, POL-05, POL-06, POL-07, POL-08, POL-09 | T07-01 | `node --test tests/servicing-contracts.test.mjs tests/servicing-source.test.mjs; node scripts/validate-contracts.mjs; dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~ServicingRulesTests` | Passed:14 targeted /348 full contract checks;659 unit tests (17 servicing),0 skipped; see07-01-SUMMARY |
| 07-01-02 | POL-02, POL-04, POL-05, POL-06, POL-07, POL-08, POL-09 | T07-01 | `git diff --check; node --test tests/servicing-contracts.test.mjs tests/servicing-source.test.mjs; node scripts/validate-contracts.mjs; dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~ServicingRulesTests` | Passed:14 targeted /348 full contract checks;659 unit tests (17 servicing),0 skipped; see07-01-SUMMARY |
| 07-02-01 | POL-01, POL-05, POL-06 | T07-02 | `dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~PolicyTemporalTests; node scripts/verify-policy-temporal-browser.mjs; pnpm web:typecheck` | Passed:9 focused backend checks (4 realSQL),both-product browser,104 frontend tests;see07-02-SUMMARY |
| 07-02-02 | POL-01, POL-05, POL-06 | T07-02 | `git diff --check; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~PolicyTemporalTests; node scripts/verify-policy-temporal-browser.mjs; pnpm web:typecheck` | Passed:9 focused backend checks (4 realSQL),both-product browser,104 frontend tests;see07-02-SUMMARY |
| 07-03-01 | POL-02, POL-03, POL-06 | T07-03 | `dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ServicingDraftTests; node scripts/verify-servicing-draft-browser.mjs; pnpm web:typecheck; pnpm web:lint` | Passed:675 backend cases (3 realSQL),2 two-user browser journeys,104 frontend/348 contract checks,44 preserved datasets;see07-03-SUMMARY |
| 07-03-02 | POL-02, POL-03, POL-06 | T07-03 | `git diff --check; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ServicingDraftTests; node scripts/verify-servicing-draft-browser.mjs; pnpm web:typecheck; pnpm web:lint` | Passed:675 backend cases (3 realSQL),2 two-user browser journeys,104 frontend/348 contract checks,44 preserved datasets;see07-03-SUMMARY |
| 07-04-01 | POL-01, POL-02, POL-03, POL-06 | T07-04 | `dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~ServicingProposalTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ServicingProposalTests; node --test apps/backoffice/tests/servicing-proposal.test.mjs; node scripts/verify-servicing-editors-browser.mjs; pnpm web:typecheck` | Passed: 33 unit + 1 real SQL, 16 browser journeys; final 2-product review rerun; see 07-04-SUMMARY.md |
| 07-04-02 | POL-01, POL-02, POL-03, POL-06 | T07-04 | `git diff --check; dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~ServicingProposalTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ServicingProposalTests; node --test apps/backoffice/tests/servicing-proposal.test.mjs; node scripts/verify-servicing-editors-browser.mjs; pnpm web:typecheck` | Passed: 33 unit + 1 real SQL, 16 browser journeys; final 2-product review rerun; see 07-04-SUMMARY.md |
| 07-05-01 | POL-04, POL-06 | T07-05 | `dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~ServicingRatingTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ServicingRatingTests; node scripts/verify-servicingrating-browser.mjs; pnpm web:typecheck` | Verified; 725 unit/13 SQL, 119 web/352 root, both-product browser; see07-05-SUMMARY |
| 07-05-02 | POL-04, POL-06 | T07-05 | `git diff --check; dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~ServicingRatingTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ServicingRatingTests; node scripts/verify-servicingrating-browser.mjs; pnpm web:typecheck` | Verified; 725 unit/13 SQL, 119 web/352 root, both-product browser; see07-05-SUMMARY |
| 07-06-01 | POL-04 | T07-06 | `dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~ServicingEvidenceTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ServicingEvidenceTests; node scripts/verify-servicingevidence-browser.mjs; pnpm web:typecheck` | Verified; see07-06-SUMMARY.md for measured SQL/HTTP/browser evidence |
| 07-06-02 | POL-04 | T07-06 | `git diff --check; dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~ServicingEvidenceTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ServicingEvidenceTests; node scripts/verify-servicingevidence-browser.mjs; pnpm web:typecheck` | Verified; see07-06-SUMMARY.md for measured SQL/HTTP/browser evidence |
| 07-07-01 | POL-04 | T07-07 | `dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~ServicingCapacityTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ServicingCapacityTests; node scripts/verify-servicingcapacity-browser.mjs; pnpm web:typecheck` | Pending implementation |
| 07-07-02 | POL-04 | T07-07 | `git diff --check; dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~ServicingCapacityTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ServicingCapacityTests; node scripts/verify-servicingcapacity-browser.mjs; pnpm web:typecheck` | Pending implementation |
| 07-08-01 | POL-04, POL-07 | T07-08 | `dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~ServicingTermsTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ServicingTermsTests; node scripts/verify-servicingterms-browser.mjs; pnpm web:typecheck` | Pending implementation |
| 07-08-02 | POL-04, POL-07 | T07-08 | `git diff --check; dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~ServicingTermsTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ServicingTermsTests; node scripts/verify-servicingterms-browser.mjs; pnpm web:typecheck` | Pending implementation |
| 07-09-01 | POL-04, POL-09 | T07-09 | `dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~ServicingPostingTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ServicingPostingTests; pnpm web:typecheck` | Pending implementation |
| 07-09-02 | POL-04, POL-09 | T07-09 | `git diff --check; dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~ServicingPostingTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ServicingPostingTests; pnpm web:typecheck` | Pending implementation |
| 07-10-01 | POL-04, POL-05, POL-06 | T07-10 | `dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~ServicingIssueTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ServicingIssueTests; node scripts/verify-servicingissue-browser.mjs; pnpm web:typecheck` | Pending implementation |
| 07-10-02 | POL-04, POL-05, POL-06 | T07-10 | `git diff --check; dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~ServicingIssueTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~ServicingIssueTests; node scripts/verify-servicingissue-browser.mjs; pnpm web:typecheck` | Pending implementation |
| 07-11-01 | POL-07 | T07-11 | `dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~RenewalPreparationTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~RenewalPreparationTests; node scripts/verify-renewalpreparation-browser.mjs; pnpm web:typecheck` | Pending implementation |
| 07-11-02 | POL-07 | T07-11 | `git diff --check; dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~RenewalPreparationTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~RenewalPreparationTests; node scripts/verify-renewalpreparation-browser.mjs; pnpm web:typecheck` | Pending implementation |
| 07-12-01 | POL-07, POL-08 | T07-12 | `dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~RenewalLifecycleTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~RenewalLifecycleTests; node scripts/verify-renewallifecycle-browser.mjs; pnpm web:typecheck` | Pending implementation |
| 07-12-02 | POL-07, POL-08 | T07-12 | `git diff --check; dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~RenewalLifecycleTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~RenewalLifecycleTests; node scripts/verify-renewallifecycle-browser.mjs; pnpm web:typecheck` | Pending implementation |
| 07-13-01 | POL-09 | T07-13 | `dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~CancellationReviewTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CancellationReviewTests; node scripts/verify-cancellationreview-browser.mjs; pnpm web:typecheck` | Pending implementation |
| 07-13-02 | POL-09 | T07-13 | `git diff --check; dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~CancellationReviewTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CancellationReviewTests; node scripts/verify-cancellationreview-browser.mjs; pnpm web:typecheck` | Pending implementation |
| 07-14-01 | POL-09, POL-05, POL-06 | T07-14 | `dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~CancellationIssueTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CancellationIssueTests; node scripts/verify-cancellationissue-browser.mjs; pnpm web:typecheck` | Pending implementation |
| 07-14-02 | POL-09, POL-05, POL-06 | T07-14 | `git diff --check; dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~CancellationIssueTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CancellationIssueTests; node scripts/verify-cancellationissue-browser.mjs; pnpm web:typecheck` | Pending implementation |
| 07-15-01 | POL-01, POL-02, POL-05 | T07-15 | `dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~PolicyHistoryTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~PolicyHistoryTests; node scripts/verify-policyhistory-browser.mjs; pnpm web:typecheck` | Pending implementation |
| 07-15-02 | POL-01, POL-02, POL-05 | T07-15 | `git diff --check; dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~PolicyHistoryTests; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~PolicyHistoryTests; node scripts/verify-policyhistory-browser.mjs; pnpm web:typecheck` | Pending implementation |
| 07-16-01 | POL-01, POL-02, POL-03, POL-04, POL-05, POL-06, POL-07, POL-08, POL-09 | T07-16 | `Run the unique-directory backend and assert-test-results procedure in07-VALIDATION.md; pnpm web:test; pnpm web:lint; pnpm web:typecheck; pnpm web:build; pnpm test; node scripts/verify-underwriting-suite.mjs; node scripts/verify-servicing-suite.mjs` | Pending implementation |
| 07-16-02 | POL-01, POL-02, POL-03, POL-04, POL-05, POL-06, POL-07, POL-08, POL-09 | T07-16 | `git diff --check; Run the unique-directory backend and assert-test-results procedure in07-VALIDATION.md; pnpm web:test; pnpm web:lint; pnpm web:typecheck; pnpm web:build; pnpm test; node scripts/verify-underwriting-suite.mjs; node scripts/verify-servicing-suite.mjs` | Pending implementation |

## Mandatory final scenarios

- Both Motor products: persisted MTA draft, current proof/referral/capacity,
  exact acceptance, multi-slice issue and one balanced financial boundary.
- Two-editor lease expiry/takeover, stale ETag, delayed renewal/release,
  revoked scope before receipt replay, issue versus cancellation and base change.
- Future MTA/early renewal leaves current risk and registration discovery intact;
  effective and processing cutoffs differ correctly and old JSON hashes survive.
- Worked pure decimal examples, positive/negative same-code components over
  different intervals, cancellation of prior negative movements, no duplicate
  returns, real closed-period fallback and posting/period-close contention.
- Renewal supplied experience, exact delivered invitation, separate acceptance,
  linked nonoverlapping term; manual/automatic lapse deduplicates notification.
- Cancellation preview/notice/approval, scheduled versus effective outcome,
  credit/refund obligation with no paid-cash claim, conflicting draft abandonment.
- Desktop/390px containment, keyboard/dialog recovery, source control/display
  fidelity, clone fresh identities and restricted agency projection.

## Manual-only evidence

Human business sign-off and assistive-technology user review remain unperformed.
Automated keyboard and accessibility assertions supplement them; do not label
them human UAT. Hosted CI and Docker runtime are not inferred from native tests.

## Sign-off

Planning sign-off:2026-09-17. All32 tasks mapped, all new test files have explicit
creation owners, no watch mode, short pure/domain feedback plus meaningful SQL
and browser gates. Runtime statuses stay pending until actual execution.

## Final backend gate procedure

Use native SQL2022 with the already authorized test connection. Keep the run
inside its own directory; record start before dotnet. PowerShell example:

```powershell
$ErrorActionPreference = 'Stop'
$runId = [Guid]::NewGuid().ToString('N')
$results = Join-Path '.local' ('phase7-final-' + $runId)
$started = [DateTimeOffset]::UtcNow
New-Item -ItemType Directory -Path $results | Out-Null
$started.ToString('o') | Set-Content (Join-Path $results 'started.txt')
dotnet test backend/BackOffice.slnx --no-restore --logger trx --results-directory $results
if ($LASTEXITCODE -ne 0) { throw 'Backend suite failed' }
```

Before running, count the newly implemented unit/SQL cases in owning-plan
summaries and test discovery. Write expected backend minimum =838 plus new cases
and SQL minimum =169 plus new real-SQL cases to the final run manifest. Never
derive minimums solely from the possibly incomplete output under test. Then:

```powershell
./scripts/assert-test-results.ps1 -ResultsDirectory $results -MinimumTests $expectedTests -MinimumSqlTests $expectedSql -NotBeforeUtc $started
```

Define expectedTests/expectedSql from that reviewed manifest before invocation.
No fallback to baseline-only minimums when added tests are missing. The result
gate must reject skips/failures/stale files/duplicate runs. Run full web tests,
lint/typecheck/build and contracts using actual package scripts; set the API
origin before web build. Check each external exit code independently.

Final harness outputs: verify-servicing-suite.mjs, verify-servicing-restart.mjs,
verify-servicing-preservation.ps1. Plan16 must define their explicit capture/
verify options and report paths using the existing Phase6 patterns. Preserve
old44 named sets and extend with every new servicing aggregate; validate SQL
output shape before comparing hashes. Capture before, initialize twice without
reset, compare both, restart only verified owned previews, fresh login and read
the exact issued graphs. No production Release build while the owned API holds
those binaries; use Debug or orderly owned-process stop/restart.

## 07-07 measured completion — 2026-09-18

Carrier lifecycle verified by final27 unit/SQL cases (25+2, no skips) in
.local/phase7-07-capacity-filter-green, cutoff09:18UTC;38 actual responses
validate strict schemas.52 API/frontend tests, full web lint/typecheck and
API/Next production builds pass. Both actual Motor Trade browser journeys pass
in.local/browser-evidence/servicing-capacity/report.json, completed09:25:52UTC;
review alone does not resolve conditions, and carrier readiness alone does not
approve referrals. Reopening retains history and removes applicability. Issued
snapshots unchanged. Desktop/390px screenshots inspected. Full detail and
overlapping prior checks are in07-07-SUMMARY.md; atomic issue remains07-10.

## 07-08 measured completion — 2026-09-18

Exact prepared terms, demo delivery and separate acceptance verified in final
31backend checks (27unit+4SQL, no skips), .local/phase7-08-final, cutoff11:40UTC;
24 captured current/history HTTP responses pass strict schemas. Signed conditions
pass both product SQL cases; shared referral/carrier regression passes29unit+3SQL.
56API/frontend tests and final web lint/production builds pass. Actual both-product
browser journeys completed11:43:57UTC, including lost-response retry, proof-bound
acceptance, changed-draft invalidation, fresh-rating reset, history and unchanged
issued snapshots. Desktop/mobile screenshots inspected. Additive demo migration
and missing-only terms configuration preserve109existing count/hash records.
See07-08-SUMMARY.md for exact paths, overlap and remaining issue/renewal boundaries.

## 07-10 measured completion — 2026-09-18

Atomic accepted adjustment issue verified in d02a2b7. Final gate11 (9unit+2SQL),
no skips, .local/phase7-10-final, cutoff14:00UTC. Both SQL product scenarios prove
14write-boundary graph rollbacks, different-key duplicate contention, exact concurrent
replay, current authority before replay, future slice/registration selection and
immutable original history. Full799unit pass; affected SQL27/28initial plus corrected
both-product reruns cover28distinct cases. The one failure was a registration fixture
applied after rating, corrected before save/rating; failed output is retained honestly.
83contract/frontend/source checks, final lint/typecheck and production builds pass.
Actual both-product browser issue/retry journeys completed14:11:00UTC; final restart
readback validates hashes, signed posting, exact issued-version/transaction UI links
and actual HTTP schemas.113existing demo table hashes preserved by additive migration.
See07-10-SUMMARY.md. Documents/MID are real pending work; no cash collection or external
delivery is claimed.07-09 cancellation lineage remains pending13/14. Continue07-11.
