---
phase: 08
slug: commercial-combined-back-office
status: approved
nyquist_compliant: true
wave_0_complete: false
created: 2026-09-19
---

# Phase8 — Validation strategy

## Test infrastructure

| Property | Value |
|---|---|
| Framework |Existing xUnit/.NET10, native SQL Server2022, Node test runner, frontend tests and Playwright/Chrome |
| Configuration |backend test csproj files, package.json, apps/backoffice/package.json and existing browser runners |
| Quick contracts |`node --test tests/commercial-combined-contracts.test.mjs tests/commercial-combined-source.test.mjs` — created08-01 |
| Targeted backend |`dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~Commercial` |
| Full backend |`dotnet test backend/BackOffice.slnx --no-restore` with the existing native SQL environment and fresh TRX directories |
| Full frontend/source |`pnpm test`; `pnpm web:test`; `pnpm web:lint`; `pnpm web:typecheck`; `pnpm web:build` |
| Runtime |Unit/contracts generally seconds; SQL/browser minutes; preceding full backend83minutes. Measure actual runs. |

## Sampling rate

Every code task has a targeted automated assertion; use fail-fast commands and reject empty test filters/skipped SQL. Run impacted product regression after shared dispatch changes. Run the full current-source backend and browser aggregates once at final acceptance, then repeat only for subsequent relevant changes/failures. Do not repeatedly run83-minute suites for documentation-only changes.

Business rules, validators, money, temporal selection and command/security behavior use RED/GREEN cases. UI/glue use meaningful interaction and persisted readback tests; do not write tests that merely mirror CSS or implementation text.

## Per-task verification map

Detailed task IDs and commands appear below. Every plan has automated verification and a security negative; every new named test file has a same-plan creation owner before its command is used.

| Plan | Requirements | Required evidence family | State |
|---|---|---|---|
|08-01|CC-01..05|Closed unions, unknown keys, old snapshots, source ledger, golden examples|pending|
|08-02|CC-01..02|Save/resume/revision, scope/product/item negatives, additive config|pending|
|08-03..04|CC-01..02|All capture branches and item CRUD, browser/API readback|pending|
|08-05|CC-03|Independent exact-money factors, job retry, pinned configuration|pending|
|08-06..07|CC-03..04|Referral/evidence/carrier/terms independent gates and stale denial|pending|
|08-08..09|CC-03..05|Temporal projection, rollback, concurrent ceiling, first issue|pending|
|08-10|CC-04|Actual CC tabs, E/K history, privacy,390px|pending|
|08-11..12|CC-03..04|Stable edit subjects, slices, signed money, current scope/replay|pending|
|08-13..14|CC-03..04|Expiring risk, early renewal, cancellation dates and release|pending|
|08-15|CC-01..05|Exact-version operational payload and missing-only demo|pending|
|08-16|CC-01..05|Full current backend/UI/source/browser, MT regression, restart/preservation|pending|

## Wave0 requirements

-08-01 creates CC source/contract test files and fixtures; no new test framework.
- Each implementation plan creates its named Commercial* tests before relying on their filtered command.
- Browser fixtures use dynamic API ports and assert they did not bind5000; never collide with the retained preview.
- Reuse scripts/assert-test-results.ps1 and the native SQL test configuration. Never drop CoverMGA_Demo.

## Manual-only verification

Business acceptance of fictional underwriting assumptions and assistive-technology review remain human tasks. Supply demo instructions and distinguish automated keyboard/responsive evidence from those reviews. HostedCI and Docker are unperformed unless actually run.

## Validation sign-off

- [x] Every detailed task has automated verification and a test creation owner.
- [x] No three implementation tasks without automated evidence.
- [x] Current-source full gate and all source controls accounted for.
- [x] No watch flags, empty filters, skipped SQL or stale results accepted.
- [x] Actual feedback runtime recorded; no false sub-minute SQL promise.
- [x] nyquist_compliant updated after the planning check.

Approved by inline planning check2026-09-19. This signs off planned validation coverage, not execution results. Wave0 remains incomplete until its test artifacts exist.

## Detailed automated task map

All commands below are planned checks, not claimed results. For task02, inspect the same fresh current-source result unless changes justify rerunning. Security reference applies to both tasks.

| Task IDs | Wave | Threat | Automated command | Test creation owner | State |
|---|---|---|---|---|---|
|08-01-01 / 08-01-02|1|T08-01|`node --test tests/commercial-combined-contracts.test.mjs tests/commercial-combined-source.test.mjs; node scripts/validate-contracts.mjs; node scripts/lint-openapi.mjs`|08-01-01 creates new named tests; existing suites retained|pending|
|08-02-01 / 08-02-02|2|T08-02|`dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~CommercialCapture; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CommercialCapture`|08-02-01 creates new named tests; existing suites retained|pending|
|08-03-01 / 08-03-02|3|T08-03|`pnpm web:test; pnpm web:typecheck; node scripts/verify-commercial-capture-browser.mjs --stage business-loss`|08-03-01 creates new named tests; existing suites retained|pending|
|08-04-01 / 08-04-02|4|T08-04|`pnpm web:test; pnpm web:typecheck; node scripts/verify-commercial-capture-browser.mjs; dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~CommercialCapture`|08-04-01 creates new named tests; existing suites retained|pending|
|08-05-01 / 08-05-02|5|T08-05|`dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~CommercialRating; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CommercialRating; pnpm web:test`|08-05-01 creates new named tests; existing suites retained|pending|
|08-06-01 / 08-06-02|6|T08-06|`dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~CommercialReferral; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CommercialReferral; pnpm web:typecheck`|08-06-01 creates new named tests; existing suites retained|pending|
|08-07-01 / 08-07-02|7|T08-07|`dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~CommercialCapacity; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CommercialTerms; node scripts/verify-commercial-underwriting-browser.mjs`|08-07-01 creates new named tests; existing suites retained|pending|
|08-08-01 / 08-08-02|8|T08-08|`dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~CommercialExposureRules; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CommercialExposureStorage`|08-08-01 creates new named tests; existing suites retained|pending|
|08-09-01 / 08-09-02|9|T08-09|`dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~CommercialDocumentSelection; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CommercialIssue; node scripts/verify-commercial-issue-browser.mjs`|08-09-01 creates new named tests; existing suites retained|pending|
|08-10-01 / 08-10-02|10|T08-10|`dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CommercialPolicyRead; pnpm web:test; pnpm web:typecheck; node scripts/verify-commercial-policy-browser.mjs`|08-10-01 creates new named tests; existing suites retained|pending|
|08-11-01 / 08-11-02|11|T08-11|`dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~CommercialServicingProposal; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CommercialServicingDraft; node scripts/verify-commercial-servicing-editor-browser.mjs`|08-11-01 creates new named tests; existing suites retained|pending|
|08-12-01 / 08-12-02|12|T08-12|`dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~CommercialServicingRating; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CommercialServicingIssue; node scripts/verify-commercial-servicing-issue-browser.mjs`|08-12-01 creates new named tests; existing suites retained|pending|
|08-13-01 / 08-13-02|13|T08-13|`dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CommercialRenewal; pnpm web:test; node scripts/verify-commercial-renewal-browser.mjs`|08-13-01 creates new named tests; existing suites retained|pending|
|08-14-01 / 08-14-02|14|T08-14|`dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CommercialCancellation; node scripts/verify-commercial-cancellation-browser.mjs`|08-14-01 creates new named tests; existing suites retained|pending|
|08-15-01 / 08-15-02|15|T08-15|`dotnet test backend/tests/BackOffice.UnitTests/BackOffice.UnitTests.csproj --no-restore --filter FullyQualifiedName~CommercialOperationalPayload; dotnet test backend/tests/BackOffice.IntegrationTests/BackOffice.IntegrationTests.csproj --no-restore --filter FullyQualifiedName~CommercialDemo; node --test tests/commercial-combined-contracts.test.mjs`|08-15-01 creates new named tests; existing suites retained|pending|
|08-16-01 / 08-16-02|16|T08-16|`pnpm test; pnpm web:test; pnpm web:lint; pnpm web:typecheck; pnpm web:build; dotnet test backend/BackOffice.slnx --no-restore; node scripts/verify-commercial-suite.mjs; node scripts/verify-underwriting-suite.mjs; node scripts/verify-servicing-suite.mjs`|08-16-01 creates new named tests; existing suites retained|pending|
