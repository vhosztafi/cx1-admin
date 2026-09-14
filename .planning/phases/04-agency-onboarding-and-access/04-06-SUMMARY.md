---
phase: 04-agency-onboarding-and-access
plan: '06'
subsystem: agency-approvals-and-terms
requires: [04-05]
provides: [independent-agency-state-decisions, immutable-approved-terms, effective-product-grants, atomic-activation-effects, suspension-and-reactivation, approval-workspaces]
affects: [04-07, 04-08, 05, 06, 09, 10]
requirements-completed: []
completed: 2026-09-15
---

# 04-06 — Activation, suspension and agreed terms

Completed across the reviewed service/storage/API/UI slices recorded in04-06-PROGRESS. Responsibilities were split into state, terms, evidence/distribution assessment and UI modules instead of one large AgencyApprovalService. The final inline review is04-06-REVIEW.

## Delivered

- Strict complete terms and immutable SQL-backed proposal/version/product records, independent reviewer seed, current catalogue/distribution checks and approval input fingerprints. Historical initial terms are captured without date rewriting; later versions append with explicit dates and exact money/rates.
- Independent activation atomically activates the agency, publishes initial terms/grants, issues staged invitations, queues protected deterministic notices, records PI/quarter-review obligations, activity/audit and original receipt. Injected failures roll the whole operation back.
- Independent suspension revokes sessions/stamps/invitations under agency-first locks while preserving history and individual user state. Reactivation rechecks currently effective approved terms and evidence, preserves individually disabled users and creates fresh invitation identities. Old tokens/sessions never reopen; initial terms/obligations/notices are not duplicated.
- Protected public proposal/list/detail/decision APIs with bounded bodies, CSRF, current authority, parent/request-specific ETags and exact replay. Published terms/products resolve current versus scheduled versions by London business date with exclusive ends.
- Final onboarding and Overview state approvals; Products complete terms proposals, full snapshot review and independent decisions; actual requester/reviewer/history; Accounts reads published financial settings. Mobile/keyboard dialogs retain uncertain commands and stale inputs, and draft mutation guards remain intact.

## Verification

Final full backend gate:289 passing tests,47 real-SQL scenarios,235 unit/54 integration, no skips. Report gate passed `.local/phase4-lifecycle-full-final`. The first attempt was blocked by owned preview DLL locks; it was discarded as evidence, owned previews stopped, and the entire suite rerun successfully.

Frontend29 tests, TypeScript, ESLint and production build pass.78 Node contract tests/OpenAPI lint and949 source controls/327 operations pass. Existing state/users/terms targeted browser recovery checks remain current.

New real browser acceptance prepares a complete draft through actual APIs, uploads evidence, records all attestations/checks and stages a broker administrator. Two internal logins perform independent activation, actual terms publication, suspension and reactivation in the UI. A genuinely committed activation response is lost and replayed without duplicates. Browser rereads verify current1725bps grant,123456.78 credit, historical1250bps terms, revoked old invitation and a different fresh invitation. Direct SQL acceptance verifies3 applied state decisions,1 terms decision,2 versions,2 obligations and4 notices. Desktop/390px screenshots inspected. No synthetic successful API responses are used in this lifecycle test.

## Next and limits

Execute04-07 trusted broker identity/scope, permissions and internal sharing reference. Broker login is still closed.04-08 owns final cross-phase/source acceptance, including the explicitly recorded directory KPI reconciliation. Finance statements, policy/rating readiness and task materialization retain their owning phases in ACCEPTANCE-BACKLOG. No entire AGY requirement, hosted CI, Docker runtime or human UAT is claimed complete. Sales funnel remains unchanged; all external effects are fictional deterministic adapters. Owned previews are stopped and demo history retained.
