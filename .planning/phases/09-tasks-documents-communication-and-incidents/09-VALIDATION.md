---
phase: 09
slug: tasks-documents-communication-and-incidents
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-09-21
---

# Phase 9 validation strategy

Research-derived strategy. Detailed plan/task mapping is not yet written and is required before planning approval. No Phase9 runtime pass is claimed.

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

- [ ] Concrete plan/task verification map and threat IDs assigned.
- [ ] Every implementation task has an executable meaningful check or earlier dependency creating it.
- [ ] No three consecutive implementation tasks without automated feedback.
- [ ] Source-ID and inherited obligation coverage reconciled.
- [ ] Final full current suites, actual PDFs, browser readbacks and restart preservation passed.
- [ ] nyquist_compliant and wave_0_complete updated from actual evidence, not planning intent.

Approval: preliminary strategy only; detailed plans and implementation verification pending.
