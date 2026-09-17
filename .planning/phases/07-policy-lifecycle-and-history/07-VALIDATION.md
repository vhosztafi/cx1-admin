---
phase: 07
slug: policy-lifecycle-and-history
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-09-17
---

# Phase 7 validation strategy

Plan writing is incomplete. This file records test obligations, not passed tests.
The first four draft plans passed GSD structural validation on 2026-09-17 with
two tasks each and no structural errors/warnings. Semantic phase-plan review,
source completeness and full decision/requirement coverage are still pending.

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

## Current per-task map

| Tasks | Requirement | Threat | Automated behavior gate | Status |
| --- | --- | --- | --- | --- |
| 07-01-01/02 | POL-02/04/05/06/07/08/09 | T07-01 forged authority/money, source omission | ServicingRulesTests, servicing-contracts/source Node suites, validate-contracts | Tests to create in07-01 |
| 07-02-01/02 | POL-01/05/06 | T07-02 temporal disclosure, scope leakage | PolicyTemporalTests, verify-policy-temporal-browser, web:typecheck | Tests to create in07-02; persisted servicing chronology rechecked after issue plans |
| 07-03-01/02 | POL-02/03/06 | T07-03 stale lease/base and lost writes | ServicingDraftTests, two-session verify-servicing-draft-browser, typecheck/lint | Tests to create in07-03 |
| 07-04-01/02 | POL-01/02/03/06 | T07-04 risk identity and backdate violations | ServicingProposalTests unit+SQL, frontend proposal tests, all-category browser | Tests to create in07-04 |

Plans07-05..16 and their task-specific checks must be written before this strategy
can pass. New verification files named in plans are required implementation
outputs, not currently existing tools. Every filtered .NET run must assert a
nonzero expected test count; a filter matching nothing is not a pass.

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

Pending: all16 executable plans, exact source/display ownership, full task map,
semantic plan review and decision coverage. Keep nyquist_compliant=false until
the planning contract is complete; keep execution results separately pending.
