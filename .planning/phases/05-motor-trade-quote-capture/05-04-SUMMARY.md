---
phase: 05-motor-trade-quote-capture
plan: '04'
status: complete
completed: 2026-09-16
requirements_completed: []
requirements_supported: [QUO-04]
---

# 05-04 — driver and repeated history capture

Both Motor Trade products expose persistent Drivers and Claims & convictions stages. Named, additional/any-driver and restriction answers remain independent. Drivers and all five nested history types support stable-ID addition, editing, reordering and explicit removal. Exact source references, typed money/dates/counts, separate full/separate names, licence/test dates, parent declarations and inactive history are preserved. Removal cannot orphan retained vehicle, trip or loss references. Existing scoped atomic revision/audit, CSRF, authority, idempotency and ETag handling remain in use.

Backend assessments cover age at inception, licence/residence chronology, company/relationship/use/personal-cover prerequisites, motorcycle eligibility, history lookbacks against the trusted London assessment day, bans and exact declared bands, source text limits and conflicting source declarations. Source-aware cover reconciliation supplies per-driver young/inexperienced choices without treating prototype ordinal IDs as funnel IDs. The form uses the same pure option selector as the executable contract. Age/cover changes preserve stale selections; saved guidance focuses the relevant field or explicit Clear action. Personal vehicle owners must be actual proposal drivers with personal cover.

Implementation commits: 2ba9b48 (core), ff5db28 (forms/history/eligibility), 573a9eb (chronology/dynamic choices/ownership). Intermediate evidence and corrected test-clock failure are retained in05-04-PROGRESS.md. Execution was sequential inline, with no subagents or funnel edits.

## Final evidence

- Fresh `.local/phase5-driver-options-20260916` and matching log: **587 passing tests =494 unit +93 integration;67 real SQL;0 skips**. Result assertion587/67passed. Integration9.9447minutes. No previous TRX results aggregated.
- **70 frontend tests**, lint, typecheck and production build pass in `.local/phase5-driver-options-{web,lint,types,build}.log`. Build origin5087. **294 contract tests**,949controls/336operations pass in `.local/phase5-driver-options-contracts.log`.
- Actual Chrome dynamic-choice journey: **QT-MT-0000000086/87**, revision14; `.local/phase5-driver-options-browser.log`, `.local/browser-evidence/quote-driver-options/report.json`. Both products cover age bands, exact families/limits, stale selections, precise focus, explicit clearing at25, licence anniversary, driver basis changes and empty eligible families.
- Final full driver/history Chrome regression: **88/89**, revision23; `.local/phase5-driver-options-history-regression.log`. All five history groups, stable identities, edit/reorder/remove/reload, money/false/zero, numeric buffers, orphan422 without revision, lost-response exact replay and stale comparison/discard pass.
- Existing readiness regression: **90/91**,43targets each, `.local/phase5-driver-options-readiness-regression.log`. Earlier business recovery regression81/83is recorded in progress. Desktop and390px screenshots inspected; driver regression also checks314px rail.
- Owned final API32848/web33900 stopped after command-line verification. All test/browser sessions completed. `git diff --check` clean; `frontend-code/` unchanged. Fictional histories retained; no reset, migration, external send, deployment or human UAT claim.

## Boundaries and next plan

Licence/provider checks and evidence remain explicitly unavailable until05-07/08. Cover context can be read from saved source answers, but its editor and complete cover readiness belong to05-06. Historical storage is tested; the historical revision viewing UI belongs to05-09. Full quote readiness remains blocked until remaining sections, evidence and matching are composed. No QUO signoff before05-11.

Next:05-05 vehicles, specified selections, modifications, portfolio and trade plates, using the verified owner prerequisites and existing revision/search projection contracts.
