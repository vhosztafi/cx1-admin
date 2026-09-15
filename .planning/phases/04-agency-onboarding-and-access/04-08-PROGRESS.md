# 04-08 progress — agency acceptance and integration

Prepared after04-07 scope/access implementation. No whole-phase acceptance or human UAT claimed.

## First concrete gap

The directory currently renders `Open actions` as undefined/Not available yet in `apps/backoffice/components/agencies/agency-list.tsx`. `AgencyEndpoints.Kpis` returns active/onboarding/suspended and total/invited broker users only. This is the explicit source KPI obligation in ACCEPTANCE-BACKLOG; implement it before phase closure.

Persisted inputs exist: AgencyStateRequest.State, AgencyTermsRequest.State and AgencyPermissionRequest.State provide pending decisions. AgencyFollowUp has immutable obligation provenance and DueOn, but no completion state or linked Phase9 task. Define and label counts honestly; do not represent these as completed or open workflow tasks. Use London business dates where due dates are involved, include an explanatory breakdown, and verify counts against actual stored rows, transitions, boundary dates and current scope. Review source/current UI contract before choosing the final count semantics.

## Remaining acceptance work

- Complete source KPI API/contract/UI and meaningful SQL/browser checks.
- Assemble a no-reset full agency browser run from the existing tested lifecycle/users/evidence/notification/terms/permissions/sharing/access scripts; retain explicit actual versus intercepted recovery evidence. Include restart persistence and existing foundation/client/contact/support/match regressions as required by the plan.
- Consolidate the final source/security/six-pillar UI reviews, validation mapping, demo walkthrough and requirement ownership. AGY-03/04 still depend on real insurance/task/finance records in later phases.
- Run final full gates and keep hosted CI, Docker and human UAT status accurate. Only then complete Phase4 and advance to Phase5.

Current accepted agency API/browser evidence is in04-07-PROGRESS/REVIEW/SUMMARY. Its CI gate update retains the two Windows-only scenario difference. This file records preparation, not completed04-08 tasks.
