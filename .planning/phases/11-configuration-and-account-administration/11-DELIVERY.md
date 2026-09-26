# Phase 11 delivery agreement

Source: user direction of 2026-09-26 and PROJECT.md. Plan directly; six feature slices, no research/checker/UI-contract agents. All ADM-01..08 are covered. Existing admin/account source controls are copied unchanged into 11-SOURCE-CONTROLS.json (79 controls); original broad requirement labels do not replace requirements coverage.

| Plan | Delivery | Requirements |
|---|---|---|
| 11-01 | Product/scheme/provider/cover editors | ADM-01 |
| 11-02 | Authority and referral rules/approval | ADM-02 |
| 11-03 | Workflow, matching/flags, templates and settings | ADM-03 |
| 11-04 | Internal users/teams/roles/approval/suspension | ADM-04, ADM-06 |
| 11-05 | Profile/password/reset/sessions/local MFA/recovery | ADM-05, ADM-06 |
| 11-06 | Audit/integration oversight and concise acceptance | ADM-07, ADM-08 |

Reuse existing SQL models, serializable command boundary, source-version pinning, identity locks, protected ticket store and UI primitives. Published changes apply to future commands; retain old issued/decision/document IDs and bytes. Sensitive identity and authority increases need distinct approval. All external delivery stays deterministic/local. Keep invitation/reset/second-factor tokens out of ordinary API/audit output; use protected one-time delivery/enrolment paths.

Validation: focused nonzero cases for changed invariants; relevant UI tests/typecheck/lint; one saved-data smoke path per feature. Run narrow regression only when evidence exposes a specific remaining risk. Do not run the entire native SQL suite, repeat unchanged evidence, generate separate research/UI-contract/audit runbooks, or block on Phase13 human UAT. Known security/correctness failures still require fixes. No new infrastructure or product expansion is needed.

Phase10 follow-up remains visible for Phase13: earned-premium read against a legitimately closed period. It is independent of Phase11 administration.
