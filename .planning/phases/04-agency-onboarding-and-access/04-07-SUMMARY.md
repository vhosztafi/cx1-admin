---
phase: 04-agency-onboarding-and-access
plan: '07'
subsystem: agency-scope-and-permissions
requires: [04-06]
provides: [trusted-agency-identity, scoped-sharing-api, own-agency-user-management, effective-permission-matrix, permission-administration, internal-sharing-preview, workspace-scope-guards]
affects: [04-08, 05, 06, 09, 10]
requirements-completed: []
completed: 2026-09-15
---

# 04-07 — Trusted agency scope, permissions and sharing

Completed across the reviewed service/storage/API/UI slices in04-07-PROGRESS. Inline review is04-07-REVIEW. Independent snapshot, scope, projection, cursor and command-authority helpers preserve transaction ownership instead of combining authentication and business reads in one service.

## Delivered

- Active stored agency/user/exclusive-role authentication; explicit scope/agency in actor responses and claims; scope/subject/stamp-bound SQL sessions; current identity checks before credential/session operations. Invalidated identities fail closed, including before command replay.
- Own-agency context and paged safe client/contact/instruction APIs, sharing projections and held cursor fingerprints with internal preview. Actual effective product/grant provenance is distinct from unavailable future workflows. Foreign identifiers, searches and cursors do not expose competitor data; private support evidence and master-person details remain excluded.
- Broker-administrator own user/invitation lifecycle, role allowlist and last-admin protection through current stored authority; scoped read cursors and versioned writes. Internal staging/demo-secret reveal remain protected. Accepted-cookie HTTP tests cover create/replay, edit/demotion, deactivate/reactivate, resend/revoke, target isolation and authority loss.
- Persistent allowlisted permission requests and independent internal decisions/revocation, immutable SQL grant provenance, session invalidation and server-derived role/capability matrix. Internal UI includes actual request/reviewer labels, histories and durable recovery; internal preview retains the staff session and source Separation notice.
- Server workspace guards on every internal page/layout, explicit internal API capability boundaries and a minimal agency signed-in result with working logout/recovery. No separate broker portal workflow UI.

## Verification

Final full backend report gate passes323 tests:260unit/63integration,56 real-SQL scenarios, no skips, in `.local/phase4-external-full-final`.80contract tests/OpenAPI lint and949 source controls/328operations pass.30frontend unit tests, TypeScript, ESLint and production build pass. Windows CI minima323/56 and Linux321/54 retain the platform difference; YAML parses and the failed-result gate was exercised. Hosted CI is unperformed.

New real agency-access browser verification issues and accepts an invitation, signs in, verifies stored agency context, eight internal deep-link guards, rejected internal APIs, reload, desktop/mobile layout, network-failed logout recovery and keyboard logout. Final screenshots inspected. Existing internal shell regression passes. Prior actual sharing/permission/users browser checks and their SQL persistence evidence are recorded in the progress file.

Fixes found by verification: wrong physical Session table name during earlier lock preparation; empty-role authentication expectations corrected to401; SQL-observed same-actor invitation deadlock resolved with actor update locking; unauthenticated new-policy parsing fixed to return401; an empty UI error strip removed; test fixture decision value and consumed-response reuse corrected before final evidence. No failed or interrupted run is counted as passing.

## Remaining scope

04-08 must reconcile the source directory Open actions KPI, run consolidated source/security/UI/acceptance checks and finish demo documentation. Whole AGY requirements are not completed by this summary. AGY-03/04 retain insurance/task/finance acceptance with their owning phases; no unsupported policy/statement/task rows are invented. No human UAT, optional Docker execution or real external delivery is claimed. Sales funnel and SQL schema remain unchanged in the coordinated access slice. Owned previews and tests are stopped.
