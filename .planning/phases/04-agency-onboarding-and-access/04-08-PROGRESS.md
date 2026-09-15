# 04-08 progress — agency acceptance and integration

04-08 is complete. Consolidated phase acceptance passed; see04-08-SUMMARY and04-VERIFICATION. Human UAT remains unperformed.

## Initial concrete gap — resolved

The directory previously rendered `Open actions` as undefined/Not available yet in `apps/backoffice/components/agencies/agency-list.tsx`. `AgencyEndpoints.Kpis` returned active/onboarding/suspended and total/invited broker users only. This is the explicit source KPI obligation in ACCEPTANCE-BACKLOG; the implementation/evidence is recorded below.

Persisted inputs exist: AgencyStateRequest.State, AgencyTermsRequest.State and AgencyPermissionRequest.State provide pending decisions. AgencyFollowUp has immutable obligation provenance and DueOn, but no completion state or linked Phase9 task. Define and label counts honestly; do not represent these as completed or open workflow tasks. Use London business dates where due dates are involved, include an explanatory breakdown, and verify counts against actual stored rows, transitions, boundary dates and current scope. Review source/current UI contract before choosing the final count semantics.

## Remaining acceptance work

- [x] Complete source KPI API/contract/UI and meaningful SQL/browser checks.
- [x] Assemble a no-reset full agency browser run from the existing tested lifecycle/users/evidence/notification/terms/permissions/sharing/access scripts; retain explicit actual versus intercepted recovery evidence. Include restart persistence and existing foundation/client/contact/support/match regressions as required by the plan.
- [x] Consolidate the final source/security/six-pillar UI reviews, validation mapping, demo walkthrough and requirement ownership. AGY-03/04 still depend on real insurance/task/finance records in later phases.
- [x] Run final full gates and keep hosted CI, Docker and human UAT status accurate. Only then complete Phase4 and advance to Phase5.

Current accepted agency API/browser evidence is in04-07-PROGRESS/REVIEW/SUMMARY. Its CI gate update retains the two Windows-only scenario difference. Only the action-indicator slice is complete; remaining04-08 tasks are listed above.


## Action indicator implementation

Implemented a shared serializable AgencyActionProjection for global KPI and paged directory rows, plus AgencyActionCounts for pending-decision totals and London dates. Open actions count pending state/terms/permission requests only; immutable due obligations are separate, explicitly labelled records with no completion tracking. This resolves both the undefined KPI and the unavailable directory column. Contracts now require the implemented counts/date; unknown KPI filters are rejected.

Targeted5 unit and1 real SQL/API case pass after correcting a missing System.Data import. Tests cover summer/winter local-midnight conversion, exclusion of due obligations from pending totals, scope-limited rows, empty selection, before/on/after due dates, terminal decision removal, independent persisted API rereads and unauthorized/query rejection. Actual accepted broker regression additionally denies KPI access.81contract tests/OpenAPI/source validation and30frontend tests/lint/typecheck/build pass. Actual KPI browser creates a permission request through the API, observes global/filtered-row increments, independently rejects it, verifies decrements/reload, error retry and390px containment. Visual review found adjacent row counts running together; pending and due figures were separated and the rebuilt browser rerun passes. Final desktop/mobile screenshots inspected. Full backend gate .local/phase4-action-kpis-full passes329 cases (265unit/64integration),57 real-SQL scenarios, no skips. CI minimums329/57Windows327/55Linux preserve the platform difference and YAML parses. Owned previews/tests are stopped. This slice does not complete04-08.


NEXT assemble/run consolidated agency and existing foundation/client/contact/support/match browser regressions, verify restart persistence, source control completeness, final security/UI/validation reports and demo acceptance mapping. Do not repeat completed KPI implementation. No wholeAGY/humanUAT/hosted CI or Docker acceptance is inferred.


Final2026-09-15: all20browser journeys,15data-set restart verification and fresh login/API/Accounts reload passed;329backend/57SQL,81contracts,30frontend/lint/typecheck and CI gate/YAML passed. Reviews/demo/setup complete. Owned previews stopped. ContinuePhase5 planning.
