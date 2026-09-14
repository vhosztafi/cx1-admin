---
phase: 03-clients-and-contact-servicing
plan: '01'
status: complete
requirements: [CLI-01, CLI-02, CLI-03, CLI-04]
completed: 2026-09-14
---

# Party contracts and replay metadata

Completed all three tasks. Contract refinement module scripts/openapi-parties.mjs runs after existing form refinements. OpenAPI now has 292 operations; all 949 source controls and five conditional rules remain mapped. New party examples and nine contract tests bring the full design suite to 62 passing cases. Schema compilation/lint and source-binding checks pass.

Contracts preserve given/withheld/not-asked consent with consistent channels; declared contact name and explicit relationship/person identity; client summary availability and scoped relationship/activity reads; safe flag mutation receipts, separately restricted history and internal safe preview; persisted MatchSubmission identity with optional actual quote link, typed comparison evidence, pinned rule and decision/request trails. Existing operation IDs and permissions remain. x-etag-resource identifies the client/relationship/contact/flag/review precondition. Flag mutations return only ID in the body and ETag in headers; detail is fetched under current permission. API/data/permission docs updated together.

SqlCommandBoundary now accepts optional validated strong ASCII ETag metadata, stores it with the original outcome and returns it on replay. Existing JSON without Etag remains readable. Handlers can flush writes for rowversion within the still-open transaction; invalid header metadata rolls back effect/receipt/audit. No arbitrary response-header caching was introduced and authentication remains excluded.

Verification: full backend suite passes 32 unit +14 integration cases, nine named real-SQL scenarios, zero skips; TRX reports in ignored .local/phase3-contract-results pass the report gate. Added SQL assertions prove original ETag/body/status after a later edit, changed-intent conflict, rollback of flushed writes for invalid tags and explicit old-shape receipt compatibility. A final targeted SQL run passes the additional no-audit-on-invalid-header assertion. No frontend implementation changed, so browser checks are deferred to owning client plans rather than rerun without a changed behavior.

Review found and fixed a generator aliasing issue: assigning description directly to a shared UUID schema polluted unrelated fields. Property-specific clones and a regression test now protect canonical UUIDs. Semantic before/after comparison confirms changed existing paths/schemas are confined to the party APIs. One validation attempt aborted inside Node/libuv with UV_HANDLE_CLOSING; the same full command rerun completed with all 62 tests and lint passing, without suppressed checks.

Commits: 723d98d (response ETag support), 20349cb (party contracts/tests/docs). No party HTTP endpoints, database migrations or client UI are implemented by this plan. CLI requirements remain pending; Phase 3 itself is not complete. Next: 03-02 client identity/relationships persistence, scoped APIs and source-faithful list/detail UI. No active test servers or sessions remain; preserve funnel and ignored secrets.
