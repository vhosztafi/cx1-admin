---
phase: 04-agency-onboarding-and-access
plan: '04'
subsystem: agency-notifications
requires: [04-03]
provides: [protected-delivery-storage, durable-demo-provider, fenced-notification-worker, scoped-notification-retry, delivery-workspace]
affects: [04-05, 04-06, 04-08]
requirements-completed: []
completed: 2026-09-14
---

# 04-04 — Durable agency demo notifications

Completed in **b25e48d** (storage/provider/recovery) and **06774f7** (scoped API, retry workspace and browser acceptance).

## Delivered

- Immutable owned notification/encrypted-envelope and terminal provider-receipt rows. Purpose-separated persistent Data Protection binds content to agency/message IDs. Ordinary outbox payload contains only notificationId; public reads and command receipts omit recipient, token, password, ciphertext, content hash and provider body.
- Dedicated agency worker uses shared durable attempts, six automatic attempts and lease fencing. Persisted pass, rejection, transient, unavailable and timeout-after-success scenarios never contact a real provider. Independently committed receipts survive an actual API process kill/restart and prevent duplicate delivery effects. Stale workers cannot overwrite newer outcomes; default diagnostic claiming remains isolated.
- Internal activation enqueue participates in the owning transaction and rejects changed-content or cross-agency operation reuse. Public notification list/detail are scoped and paged, return safe status/ETag, and do not cache. Retry requires current internal permission, active agency, owned notification, current job ETag and a bounded reason. Successful replay precedes stale-version checks; current authority is rechecked first. Only eligible exhausted transient work can expand its original budget to12 and18. Rejection is terminal.
- Activity tab now contains a reusable Demo delivery panel. It displays actual queued/processing/delivered/rejected/exhausted results, separates delivery from invitation acceptance and refreshes agency activity after retry. Uncertain responses retain the exact key/body/ETag, disable cancellation and recover by replay. Stale versions retain the reason and require explicit reload; changed eligibility disables retry. Keyboard focus enters the reason form and returns to refresh; navigation links and unload are guarded during editing. Source blue header, white panels and contained mobile tables are preserved.
- An explicit Development-only CLI `--seed-agency-notification-demo` adds a labelled fictional agency and pass/reject/exhausted histories using the real worker. It validates CoverMGA_Demo, preserves existing records, claims only fixture-owned jobs and never sends email. Its directly seeded active agency is a test fixture, not evidence that activation approval is implemented. The browser harness creates its own fixtures and preserves them.
- OpenAPI has325 operations and949 mapped controls; notification schema/ID-only retry contract and actual SQL storage are documented. CI minima raised to211/27 on Windows and209/25 on Linux.

## Verification

- Full backend: **211 passed**, **27 real-SQL scenarios**, no failures/skips; report gate verified `.local/phase4-notification-complete-results`. This includes177 unit and34 integration cases. Final API rerun after correcting non-transient failure display passed in `.local/phase4-notification-api-reviewed`.
- Notification SQL/API acceptance covers protection/purpose/agency binding, immutable ownership, rollback, stable operations, all provider scenarios, actual process restart, stale leases, bounded attempts, lost-response replay, current permission revocation, CSRF, missing/stale ETags, forged/oversized input, rejection denial, cross-agency records and cursor scope. Initial harness expectations for CSRF/unknown fields were corrected to the established403/400 API behavior; no protection was weakened.
- Frontend20 tests; contract76 tests; ESLint, TypeScript and production build pass. Final build uses BACKOFFICE_API_ORIGIN=http://127.0.0.1:5087.
- Final built Chrome `verify-agency-notifications-browser.mjs` passes persisted outcomes, keyboard focus, exact uncertain replay, navigation guard, retained stale reason/current eligibility and390px containment. Existing `verify-agencies-browser.mjs` six-stage/replay/stale/abandonment/filter regression also passes. Screenshots inspected: `.local/browser-evidence/agency-notifications-desktop.png` and `agency-notifications-mobile.png`.
- Task-owned preview/test processes stopped; demo history retained. Hosted CI and human/assistive-technology UAT are not claimed.

## Next integration boundaries

Continue **04-05**, agency users and invitations. AGY-05 remains incomplete until its real invitation lifecycle is verified. Draft staged users issue no token/delivery. Add actual invitation/agency ownership and current-token validity to enqueue, worker and retry; stale queued invitations become superseded, and delivery never accepts a user. Current invitation dispatch intentionally fails closed.04-06 then enqueues from approved activation/lifecycle transactions, extending notice purposes as needed.04-08 performs whole-phase integration and visual review. Sales funnel remains unchanged; no real transport or deployment was introduced.
