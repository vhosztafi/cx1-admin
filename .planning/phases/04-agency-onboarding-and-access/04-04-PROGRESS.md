# 04-04 implementation progress

Plan remains **in progress**. Do not create a completion summary or mark AGY-05 complete yet.

Production commit: **b25e48d**. No active previews or test processes remain.

## Implemented delivery foundation

- `AgencyNotificationPayload` protects delivery content with the existing persistent Data Protection provider, purpose-separated and bound to agency/message IDs. Envelope formatting does not print secrets; invalid decryption has a generic error. Recipient/template/content are bounded; content uses an8192-byte UTF-8 limit. Ordinary job JSON contains only notificationId.
- Immutable SQL `AgencyNotification` owns agency, unique work ID, purpose, encrypted envelope and content hash. `AgencyNotificationReceipt` has a composite notification/agency FK, unique notification and immutable terminal demo outcome. Migration20260914112254 includes update/delete rejection and job ownership insert validation.
- `AgencyNotificationService.EnqueueActivation` participates in an existing transaction, locks/checks the active agency, pins an explicit valid scenario version and rejects changed-content/other-agency operation reuse. Atomic rollback includes job/message/activity. It is an internal service seam; activation routes remain unavailable until04-06. No public arbitrary-message enqueue exists.
- Shared `SqlJobLeases.ClaimKindAsync` supports the bounded agency kind; default ClaimAsync remains diagnostic-only. The existing diagnostic retry API still excludes agency work.
- `AgencyNotificationWorker` simulates pass/reject/transient/unavailable/timeout-after-success. It decrypts only in worker memory, checks current agency, and independently commits a provider receipt before applying local job status. Application fences the current lease, appends safe attempt outcome/activity and reuses saved receipts. Six failed automatic attempts exhaust; no real transport exists.
- Development-only `AgencyNotificationDispatcher` uses the shared persistent key provider and current lease infrastructure. Unknown exceptions log only generic recovery text. Invitations fail closed until04-05 adds current invitation ownership/token validity; delivery never changes user acceptance.
- Canonical DATA-MODEL records actual storage versus pending later lifecycle purposes. Agency activity summaries distinguish queued/delivered/rejected demo notifications.

## Verification so far

Targeted notification tests:3 passed, including2 real-SQL scenarios, in `.local/phase4-notification-process-results`. Covers protected content/purpose/agency/message binding, persisted keys, ownership, immutable records, changed operation content, rollback, independent reads, all five scenarios, bounded failure, stale worker fencing, kind isolation and an actual hidden API process kill/restart after committed provider success. The recovered job references the original single receipt. Full regression passed210 cases including26 real-SQL scenarios, no failures/skips, verified by assert-test-results.ps1 in `.local/phase4-notification-full-results`. Final build after adding activity labels passed with zero warnings/errors. CI minima now210/26 Windows and208/24 Linux; hosted CI remains unrun.

## Required next work

1. Implement scoped list/detail and eligible retry APIs with current authorization before replay, notification/job ETag, required reason, bounded body, safe ID-only receipt and safe projections. No token/envelope/provider payload in reads, audit or receipt. Add direct wrong-agency/revoked-role/CSRF/stale/replay tests. Preserve job identity and expand retry budget only for retryable exhaustion; definitive rejection remains rejected.
2. Add notification panel to agency activity and reuse on later users/activation. Explicit Demo delivery, queued is not Sent. Preserve exact uncertain retry command and error state. Current source/UI contracts are in04-UI-SPEC/OpenAPI.
3. Add additive explicit fictional scenario seeds/browser fixtures. The tested notification migration was additively applied to Demo; existing fictional records were preserved. Do not reset it. Browser verifies success/rejection/exhaustion/retry/lost response, source alignment and390px viewport.
4. Invitation/superseded behavior and other lifecycle notice purposes must be implemented with their real owning rows in04-05/06. Never dispatch a draft/stale invitation or treat delivery as acceptance.
5. Finish full backend/contract/frontend/build/browser gates, review and summary before plan completion. No human UAT or hosted CI is inferred.

No sales-funnel changes or real messages. Task-owned process tests clean up their own processes and generated SQL databases.
