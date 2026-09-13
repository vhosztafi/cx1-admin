# Durable platform checkpoint

Plan 02-05 remains IN PROGRESS. Task 1 (atomic command/replay/audit boundary) is implemented and real-SQL verified. Task 2 has a verified lease/retry component; provider/inbox, hosted dispatch, operational endpoints/views and full recovery tests remain unimplemented. Do not create a completed 02-05-SUMMARY or advance plan count yet.

SqlCommandBoundary in Infrastructure/Platform accepts an authorized actor, canonical concrete server route, command key, normalized typed request DTO, event type and database handler. Caller authorization happens before entry; stale-version checks inside the handler happen after replay. Handler writes domain/outbox through the supplied context and performs no external calls. Only successful non-secret JSON results enter the result store. Authentication paths are rejected; future secret-returning endpoints must stay outside this helper.

SQL application locks serialize concurrent same-scope/route/key commands. Request SHA-256 hash mismatches conflict; request bytes are not saved. The existing IdempotencyRecord stores an internal ResourceId/Body envelope and remains immutable beyond cache ExpiresAt. Receipts are not purged in MVP. SQL triggers make audit and receipts append-only/immutable. Binary collation distinguishes operation keys and callback event IDs by case; keys reject surrounding whitespace.

Validation: 13 unit cases and eight integration cases pass, zero skips. New SQL scenario covers invalid-JSON rollback across effect/outbox/audit/receipt, simulated crash after flushing SQL, three concurrent retries producing one handler effect, replay through a fresh boundary/context after cache horizon and before stale-version logic, changed input conflict, case-distinct keys and raw-SQL tamper rejection. Existing authentication and foundation suites still pass. DurableCommandReceipts migration applied to CoverMGA_Demo successfully. No worker/process-recovery claim is made.

Next work:
- Choose a bounded diagnostic demo operation through a documented operational route. Current OpenAPI has integration settings, job status/retry/list and audit list, but no start-probe route; add an explicitly reviewed contract rather than pretending a business payment occurs.
- Lease/fencing component is complete (84bb124): reuse SqlJobLeases.OwnedAsync inside the result-application transaction, never update completion without that lock/fence.
- Pin scenario version and persist DemoProviderOperation independently before simulating timeout. Reconcile the same operation key after restart; no in-memory outcomes.
- Apply callback/inbox result and local effect atomically. Matching duplicates acknowledge; changed hashes cannot overwrite applied content and require separate quarantine evidence.
- Add supported admin job/audit API/UI views with scope checks, redacted DTOs, meaningful real SQL/API recovery tests and actual process restart. Fault hooks stay outside production HTTP.
- Exception/task identity must be durable; no task table exists yet, so do not claim business-task deduplication from the current Team/outbox test fixture.

Ports: 5080 belongs to unrelated Docker service. Prior UI tests used API 5087/web 3100 and stopped their own processes. Demo password remains in ignored .local/demo-password.txt. Source funnel unchanged.


## Latest checkpoint: fenced SQL leases

Commit 84bb124 adds SqlJobLeases, RetrySchedule, OutboxWork lease/scenario/status fields and JobException, plus JobLeaseFencing migration (applied to CoverMGA_Demo). Claim currently accepts only diagnostic-probe kind. No hosted worker is registered yet and no public operational start endpoint exists. Claim selects due work using UPDLOCK/READPAST/ROWLOCK in REPEATABLE READ; a 30-second random token fences final writes. Old attempts close as lease-expired. Failure enum intrinsically classifies unavailable/timeout as transient; rejection/invalid payload/provider conflict are terminal. Six attempts maximum; lost sixth owner terminally fails on recovery. JobException is one per WorkId and explicitly is not a business Task.

Final backend run: 21 unit cases and nine integration cases pass, zero skips. Added retry-window/budget unit cases and a real-SQL lease scenario: concurrent claim exclusivity, fresh-service reclaim after expiry, old-token rejection before/after replacement, delayed retry, six-attempt exhaustion when the final owner disappears, unique exception, and idempotent rejection handling. Existing transaction/auth/foundation regressions still pass.

Resume with independently committed deterministic provider results and fenced inbox/local result application. Provider success must survive an injected local timeout and a new worker instance/process. Changed callback content needs separate quarantine evidence without changing applied original content. Then wire hosted dispatch, reviewed operational contract/API/UI and full process recovery verification. Plan 02-05 remains incomplete; plan count unchanged. No test servers are running.
