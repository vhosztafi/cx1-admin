# Durable platform checkpoint

Plan 02-05 remains IN PROGRESS. Task 1 (atomic command/replay/audit boundary) is implemented and real-SQL verified. Tasks 2/3 (worker/provider/inbox, supported operational endpoints/views and recovery tests) are not implemented. Do not create a completed 02-05-SUMMARY or advance plan count yet.

SqlCommandBoundary in Infrastructure/Platform accepts an authorized actor, canonical concrete server route, command key, normalized typed request DTO, event type and database handler. Caller authorization happens before entry; stale-version checks inside the handler happen after replay. Handler writes domain/outbox through the supplied context and performs no external calls. Only successful non-secret JSON results enter the result store. Authentication paths are rejected; future secret-returning endpoints must stay outside this helper.

SQL application locks serialize concurrent same-scope/route/key commands. Request SHA-256 hash mismatches conflict; request bytes are not saved. The existing IdempotencyRecord stores an internal ResourceId/Body envelope and remains immutable beyond cache ExpiresAt. Receipts are not purged in MVP. SQL triggers make audit and receipts append-only/immutable. Binary collation distinguishes operation keys and callback event IDs by case; keys reject surrounding whitespace.

Validation: 13 unit cases and eight integration cases pass, zero skips. New SQL scenario covers invalid-JSON rollback across effect/outbox/audit/receipt, simulated crash after flushing SQL, three concurrent retries producing one handler effect, replay through a fresh boundary/context after cache horizon and before stale-version logic, changed input conflict, case-distinct keys and raw-SQL tamper rejection. Existing authentication and foundation suites still pass. DurableCommandReceipts migration applied to CoverMGA_Demo successfully. No worker/process-recovery claim is made.

Next work:
- Choose a bounded diagnostic demo operation through a documented operational route. Current OpenAPI has integration settings, job status/retry/list and audit list, but no start-probe route; add an explicitly reviewed contract rather than pretending a business payment occurs.
- Add lease token/fencing as needed: expired/replaced lease owners must not commit completion. Claim due work atomically; attempts must be durable and bounded.
- Pin scenario version and persist DemoProviderOperation independently before simulating timeout. Reconcile the same operation key after restart; no in-memory outcomes.
- Apply callback/inbox result and local effect atomically. Matching duplicates acknowledge; changed hashes cannot overwrite applied content and require separate quarantine evidence.
- Add supported admin job/audit API/UI views with scope checks, redacted DTOs, meaningful real SQL/API recovery tests and actual process restart. Fault hooks stay outside production HTTP.
- Exception/task identity must be durable; no task table exists yet, so do not claim business-task deduplication from the current Team/outbox test fixture.

Ports: 5080 belongs to unrelated Docker service. Prior UI tests used API 5087/web 3100 and stopped their own processes. Demo password remains in ignored .local/demo-password.txt. Source funnel unchanged.
