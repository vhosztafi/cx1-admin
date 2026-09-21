---
phase: 09
slug: tasks-documents-communication-and-incidents
status: planned
nyquist_compliant: false
wave_0_complete: false
created: 2026-09-21
---

# Phase 9 validation strategy

Research-derived strategy with18 sequential plans and36 mapped tasks. No Phase9 runtime pass is claimed. nyquist_compliant/wave_0_complete remain false until implemented test infrastructure and outcomes justify them.

## Test infrastructure

Existing xUnit unit and real SQL/API integration projects under backend/tests, Node contract tests under tests, frontend tests under apps/backoffice/tests and Playwright browser scripts. Native SQL2022 uses isolated GUID-named databases; files use a separate owned test directory. Retained CoverMGA_Demo and original data-protection keys are never fixture reset targets.

Quick commands depend on each slice's newly implemented test files/classes. Baseline contract command: `node --test tests/api-contracts.test.mjs tests/adapter-contracts.test.mjs`. Full root: `pnpm test`. Frontend: `pnpm web:test`, `pnpm web:lint`, `pnpm web:typecheck`, `pnpm web:build`. Backend: `dotnet test backend/BackOffice.slnx --no-restore` with the documented native SQL connection and TRX loggers in a fresh result directory. If dependency changes occur, use the repo restore configuration/lockfiles before no-restore checks.

Full integration baseline from Phase8 took4.13hours; do not advertise a sub-minute full gate. Use focused meaningful tests after each affected task and one full current-source gate at final acceptance. Inspect every process exit and strict TRX result; a missing filter match, unavailable SQL or skipped case is not success. `scripts/assert-test-results.ps1` must run in a child PowerShell because it exits its process. Inventory discovery must match actual integration cases; report unique counts rather than adding repeated runs.

## Required verification families

| Slice | Requirement / decision | Secure behavior and proof | Planned tests |
|---|---|---|---|
| Source/contracts | OPS01–08, D01/13 | Original IDs, variants, display ownership; closed writes, money/date/unknown precision | Node source/contract tests; final ledger independently reconciled |
| Subjects/tasks | OPS01, D02/03 | Current typed-parent scope before replay; forbidden assignments, stale ETags, complete/reopen reasons; atomic bulk failure | Unit transitions + actual SQL FKs/unique/revocation/rollback + browser create/edit/bulk/checklist/comment |
| Workflow tasks | OPS02, D04 | Published rule/source-event uniqueness, no retry duplicates or automatic reopening | Unit rule matrix + SQL concurrent repeated event/terminal exception/retained follow-up bridge |
| Files | OPS05, D06 | Bytes/hash/media/limit/path scope, retained SQL evidence, pending unavailable, recovery after rename/commit crash | Unit filename/signature/limits + SQL and filesystem fault injection + real authorized download and foreign/withdrawn cases |
| PDF/documents | OPS04/05, CC05, D07 | Exact source/template versions, product fields, original immutable bytes | Unit projection/template negatives + actual multi-page PDFs, parsed values, page renders inspected, SQL source/hash/readback |
| Notes/threads | OPS03, D05/08 | Internal content never agency-visible; foreign recipient/attachment rejected | SQL/API authority and revoked relationship tests + browser audience/save/draft recovery |
| Delivery/pack | OPS06, D08 | Retry same envelope/version/provider effect; explicit resend distinct operation | Provider timeout-after-success, lease loss, changed duplicate quarantine, revoked actor before execution/apply, actual browser failed→retry readback |
| Incident precision | OPS07, CC05, D09 | Date-only/approximate/exact semantics, DST, historical source clipping and ambiguous dates | Unit temporal matrix + SQL immutable source/version and foreign risk negatives |
| Incident/claims | OPS07, D09/10 | Incomplete draft permitted, handoff complete/owned, no local settlement, append-only summaries | Browser MT and CC conditional forms + SQL failed/uncertain/crash/retry/summary provenance |
| MID | OPS08, D11 | Only Motor Trade, exact intent/version/action/date, old response does not mutate later history | Unit product/action matrix + SQL duplicate/retry/lease/revocation/version negatives + vehicle exception browser |
| Cancellation | POL01, CC05, D12 | Future withdrawal/task close not early; legacy receipts preserved without silent resend | Frozen-time before/at/after tests and actual restart with pending consequences |
| Integrated demo | OPS01–08, D14/15 | Missing-only seeds, saved workflows, retained data/keys, meaningful failure recovery | Actual demo/browser walkthrough, two additive initializations, process restart, DB/file graph hashes, retained MT/CC regressions |

The planner must replace this family map with per-task IDs, exact test commands, threat IDs and expected nonzero counts. It must create meaningful test implementations in their owning slices before those checks run. No acceptance may be met solely by a generated schema or static string assertion when runtime behavior is required.

## Sampling and manual checks

After a domain/API change run its unit/SQL negatives and positive journey; after UI changes run its frontend checks and saved browser journey. Do not repeat a full suite for documentation-only edits. Keep progress updates while long tests run and retain raw failed attempts when recovery is used. Final full suite runs after last product change, with assembly/source hash provenance and strict inventory evidence.

Rendered PDF inspection must include long tables, multiline names/addresses, currency values, glyphs, page breaks, MT certificate, CC schedule/EL, quote, renewal and cancellation. Browser visual checks include desktop and390px,200% zoom, keyboard/dialog focus, no page overflow, readable errors, uncertain-input preservation and no success before hydration/save. Automated checks do not claim human business or assistive-technology UAT.

## Sign-off

- [x] Concrete plan/task verification map and threat IDs assigned.
- [x] Every implementation task has an executable meaningful check or owning task creating it.
- [x] No three consecutive implementation tasks without planned automated feedback.
- [ ] Source-ID and inherited obligation coverage reconciled.
- [ ] Final full current suites, actual PDFs, browser readbacks and restart preservation passed.
- [ ] nyquist_compliant and wave_0_complete updated from actual evidence, not planning intent.

Approval: planning strategy reviewed; execution evidence pending.

## Concrete plan/task mapping

All rows cover task01 implementation and task02 review of the same fresh results, with git diff --check. SQL test names must include RealSql or RealApiProcessRestart; each filtered suite must run nonzero cases. Commands are individual sequential invocations. Existing runners require no bootstrap install; each test/script below is an explicit output of its owning plan before invocation.

| Tasks | Requirements | Threat | Automated verification | State |
|---|---|---|---|---|
|09-01-01/02|OPS-01, OPS-02, OPS-03, OPS-04, OPS-05, OPS-06, OPS-07, OPS-08|T09-01|`node --test tests/operations-contracts.test.mjs tests/operations-source.test.mjs`|Pending implementation|
|09-02-01/02|OPS-01|T09-02|`dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter FullyQualifiedName~OperationalTask --logger trx --results-directory .local/phase9-02-unit`<br>`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~OperationalTask --logger trx --results-directory .local/phase9-02-sql`|Pending implementation|
|09-03-01/02|OPS-01|T09-03|`node --test apps/backoffice/tests/tasks.test.mjs`<br>`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~OperationalTaskBrowser --logger trx --results-directory .local/phase9-03-sql`<br>`node scripts/verify-task-browser.mjs`|Pending implementation|
|09-04-01/02|OPS-02|T09-04|`dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter FullyQualifiedName~OperationalWorkflow --logger trx --results-directory .local/phase9-04-unit`<br>`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~OperationalWorkflow --logger trx --results-directory .local/phase9-04-sql`|Pending implementation|
|09-05-01/02|OPS-05|T09-05|`dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter FullyQualifiedName~OperationalFile --logger trx --results-directory .local/phase9-05-unit`<br>`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~OperationalFile --logger trx --results-directory .local/phase9-05-sql`|Pending implementation|
|09-06-01/02|OPS-04, CC-05|T09-06|`dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter FullyQualifiedName~OperationalPdf --logger trx --results-directory .local/phase9-06-unit`<br>`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~OperationalPdf --logger trx --results-directory .local/phase9-06-sql`|Pending implementation|
|09-07-01/02|OPS-04, OPS-05, CC-05|T09-07|`dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter FullyQualifiedName~OperationalDocument --logger trx --results-directory .local/phase9-07-unit`<br>`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~OperationalDocument --logger trx --results-directory .local/phase9-07-sql`|Pending implementation|
|09-08-01/02|OPS-04, OPS-05|T09-08|`node --test apps/backoffice/tests/documents.test.mjs`<br>`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~OperationalDocumentBrowser --logger trx --results-directory .local/phase9-08-sql`<br>`node scripts/verify-document-browser.mjs`|Pending implementation|
|09-09-01/02|OPS-03|T09-09|`dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter FullyQualifiedName~OperationalCommunication --logger trx --results-directory .local/phase9-09-unit`<br>`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~OperationalCommunication --logger trx --results-directory .local/phase9-09-sql`|Pending implementation|
|09-10-01/02|OPS-03, OPS-06|T09-10|`dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter FullyQualifiedName~OperationalDelivery --logger trx --results-directory .local/phase9-10-unit`<br>`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~OperationalDelivery --logger trx --results-directory .local/phase9-10-sql`<br>`node scripts/verify-communication-browser.mjs`|Pending implementation|
|09-11-01/02|OPS-07, CC-05|T09-11|`dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter FullyQualifiedName~OperationalOccurrence --logger trx --results-directory .local/phase9-11-unit`<br>`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~OperationalOccurrence --logger trx --results-directory .local/phase9-11-sql`|Pending implementation|
|09-12-01/02|OPS-07, CC-05|T09-12|`dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter FullyQualifiedName~OperationalIncident --logger trx --results-directory .local/phase9-12-unit`<br>`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~OperationalIncident --logger trx --results-directory .local/phase9-12-sql`<br>`node scripts/verify-incident-browser.mjs`|Pending implementation|
|09-13-01/02|OPS-07, CC-05|T09-13|`dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter FullyQualifiedName~OperationalClaims --logger trx --results-directory .local/phase9-13-unit`<br>`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~OperationalClaims --logger trx --results-directory .local/phase9-13-sql`|Pending implementation|
|09-14-01/02|OPS-08|T09-14|`dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter FullyQualifiedName~OperationalMid --logger trx --results-directory .local/phase9-14-unit`<br>`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~OperationalMid --logger trx --results-directory .local/phase9-14-sql`<br>`node scripts/verify-mid-browser.mjs`|Pending implementation|
|09-15-01/02|OPS-02, OPS-04, OPS-06, OPS-08, CC-05, POL-01|T09-15|`dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter FullyQualifiedName~OperationalCancellation --logger trx --results-directory .local/phase9-15-unit`<br>`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~OperationalCancellation --logger trx --results-directory .local/phase9-15-sql`|Pending implementation|
|09-16-01/02|OPS-01, OPS-02, OPS-03, OPS-04, OPS-05, OPS-06, OPS-07, OPS-08|T09-16|`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~OperationalDemo --logger trx --results-directory .local/phase9-16-sql`|Pending implementation|
|09-17-01/02|OPS-01, OPS-02, OPS-03, OPS-04, OPS-05, OPS-06, OPS-07, OPS-08, CC-05|T09-17|`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~OperationalAcceptance --logger trx --results-directory .local/phase9-17-sql`|Pending implementation|
|09-18-01/02|OPS-01, OPS-02, OPS-03, OPS-04, OPS-05, OPS-06, OPS-07, OPS-08, CC-05, POL-01|T09-18|`dotnet test backend/tests/BackOffice.UnitTests --no-restore --logger trx --results-directory .local/phase9-final/unit`<br>`dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --logger trx --results-directory .local/phase9-final/sql`<br>`pwsh -NoProfile -File scripts/assert-test-results.ps1 -ResultsDirectory .local/phase9-final -MinimumTests 1511 -MinimumSqlTests 383`<br>`pnpm test`<br>`pnpm web:test`<br>`pnpm web:lint`<br>`pnpm web:typecheck`<br>`pnpm web:build`<br>`node scripts/verify-operational-suite.mjs`<br>`node scripts/verify-commercial-suite.mjs`<br>`node scripts/verify-servicing-suite.mjs`<br>`node scripts/verify-underwriting-suite.mjs`|Pending implementation|

Planning sign-off2026-09-21: each task has a concrete check or reviews its already produced current-source reports; all new test files are owned by that implementation task. Unit feedback target under60seconds per focused family; SQL/browser feedback may take minutes and final regression hours. Do not interrupt a healthy long run to meet that target. No watch mode.
