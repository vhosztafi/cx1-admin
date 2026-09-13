# Durable platform checkpoint

Plan 02-05 remains IN PROGRESS. Completed components: atomic commands (de573c0), lease fencing/retries (84bb124), independent provider outcomes and fenced inbox application (682c2bb). Hosted dispatch, operational contracts/API/UI and actual worker-process recovery verification remain unfinished. Do not create a completed 02-05-SUMMARY or advance plan count yet.

## Implemented contracts

SqlCommandBoundary accepts an authorized actor, canonical concrete server route, key, normalized typed DTO, event type and database handler. Authorize before entry; stale-version checks go inside the handler after replay. Handler uses supplied context and performs no external calls. Only successful non-secret JSON results are stored. Same-key commands use SQL transaction application locks. Receipts remain immutable beyond ExpiresAt; request bytes are not retained. SQL triggers prohibit audit/receipt UPDATE/DELETE. Operation/event keys have binary collation and command keys reject surrounding whitespace.

SqlJobLeases currently claims only diagnostic-probe work. It uses UPDLOCK/READPAST/ROWLOCK in REPEATABLE READ and assigns a 30-second lease token. Final writes fence on token, attempt, state and expiry. Expired attempts close as lease-expired. Unavailable/timeout failures use deterministic bounded jitter and six attempts maximum; permanent rejection/invalid payload/provider conflict are terminal. Lost sixth owner becomes failed with one JobException. JobException is infrastructure evidence, not a business Task.

DiagnosticDemoProvider accepts exactly {probe:"foundation"}, with pinned SettingVersion values {kind:"diagnostic-probe",scenario:<success|reject|fail-once|timeout-after-success>} under scope diagnostic-probe/<scenario>. It commits independently of local work application. Success/rejection references and timestamps persist; fail-once first commits transient-failed and succeeds on subsequent service invocation; timeout-after-success commits success before throwing typed timeout. Same key/different pinned request hash conflicts. No external service/payment is called. Diagnostic settings are not yet seeded in the demo; tests create their own.

DiagnosticInbox locks the job, validates its recorded provider outcome and lease, then atomically applies inbox, DiagnosticReceipt or terminal rejection/JobException, attempt, job status and audit. Matching duplicates acknowledge with no second effect. Changed duplicate hashes add one AdapterQuarantine and audit event while preserving original applied inbox/receipt. A pre-application altered result is rejected. OutboxWork now pins CorrelationId. DiagnosticInboxReceipts migration is applied to CoverMGA_Demo.

## Verification

Final backend run: 21 unit cases and 11 integration cases pass, zero skips. SQL scenarios cover command rollback after flushed writes, concurrent replay and expiry horizon; exclusive claims/stale-owner fencing/delays/exhaustion; independently committed provider success before timeout, fresh-service reconciliation, fail-once, rejection, concurrent deduplication and request conflict; local SQL rollback after a test-only SaveChanges interceptor throws, concurrent callback completion, changed duplicate quarantine and rejection with one exception/no successful receipt. Each run owns and removes its generated CoverMGA_Test_* database. Provider/inbox tests use new service instances/contexts; actual OS worker-process restart remains pending.

## Resume next

1. Review/add a bounded diagnostic-start operational contract. Current OpenAPI has integration settings, job status/retry/list and audit list but no start-probe route. Add a clearly documented operational endpoint; do not fake a business payment. Fault/recovery hooks must not exist in production HTTP. Consider a development-only admin probe route with explicit scenario selection.
2. Seed four immutable diagnostic scenario versions. Register command/lease/provider/inbox services and a hosted dispatcher. Catch only typed provider failures for safe retry classification; unexpected failures must not leak raw exceptions or create duplicate effects. Handle cancellation by leaving leases recoverable. Reconcile persisted provider outcome on every retry using the same key.
3. Add scoped operational job/audit APIs and prototype-style admin views with safe DTOs; no raw internal payload/hash/ciphertext/provider errors. Preserve creator/subject authorization and admin inspection audit. Manual retry must preserve identity and reject retrying definite business rejection as new intent.
4. Test real SQL/API authorization/replay, hosted job lifecycle and actual worker process restart after provider success/local timeout. Verify browser operational flow. Only then close 02-05 and continue 02-06 foundation gate.

Ports: 5080 belongs to unrelated Docker service; prior tests used API 5087/web 3100 and stopped their own processes. No test servers are running. Demo password stays in ignored .local/demo-password.txt. Keep frontend-code unchanged. No production deployment or business-task deduplication claim.
