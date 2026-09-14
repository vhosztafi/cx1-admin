# Phase 3 implementation research

Researched 2026-09-14 against the local contracts, prototype and verified foundation, plus Microsoft primary documentation. No new runtime dependency is required. Confidence: high on persistence/concurrency approach; future broker sessions and quote transitions retain explicit integration gates.

## Recommended implementation

Keep the current SQL relational model and .NET modules. Add Parties records/configuration in separate files so the existing foundation does not become one larger source file. Use typed JSON only for bounded address/consent/evidence snapshots, with both DTO validation and SQL ISJSON. Client search uses indexed normalized name/company number plus scoped relationship/contact predicates; all counts use the same predicate. SQL unique client reference allocation must tolerate concurrency; use a sequence or sequence-backed counter, never MAX+1.

A filtered unique index enforces at most one active primary contact per relationship. The service additionally locks the relationship and enforces at least one primary whenever active contacts exist. Promote/demote under one transaction, flushing the old primary first to avoid transient index conflicts. EF supports multiple saves in a transaction; disable MARS in this profile and roll back the whole command on failure. This recommendation follows [EF transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions) and [SQL filtered indexes](https://learn.microsoft.com/en-us/sql/relational-databases/indexes/create-filtered-indexes?view=sql-server-ver17).

Use SQL rowversion and strong ETags on mutable client, relationship/contact, flag and match records. Apply current authorization before SqlCommandBoundary replay, and mutable preconditions inside its handler. Contact collection creation uses the relationship ETag; individual edits/end/make-primary use the contact ETag while serializing changes on the parent relationship. Return post-save ETags, and store the original response metadata for replay where needed. EF compares the original token on update and reports a conflict through DbUpdateConcurrencyException; see [EF concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).

Reuse the operational cursor's protection/expiry pattern but use a new purpose and scope fingerprint. Bind user, current role/agency scope, route, filters, page size and ordering. Reject obsolete scope even if the cursor remains cryptographically valid. Search normalization is deterministic and bounded; escape literal SQL LIKE wildcards if LIKE is used. Do not add fuzzy-search packages or SQL full text for this first version.

## Contract gaps to resolve in 03-01

1. ContactWrite consent booleans cannot distinguish Withheld from Not asked. Add a required declared state while retaining explicit channel/source/time values; reject inconsistent combinations.
2. ContactWrite.personId plus name edits could mutate another relationship's shared Person. Store declared name/components on Contact; Person canonical name is created once in this phase. Explicit person reuse requires same-client authorized membership; no global lookup.
3. MatchReview requires a quote which Phase 5 has not implemented. Introduce MatchSubmission and a submissionId, make quoteId optional until attached to a real quote. Store immutable identity/evidence and decision history rather than inventing an unbacked quote.
4. Match evidence currently has only code/summary, insufficient for the prototype's comparison table and Rule tab. Add bounded typed submitted/candidate comparison values, weight/result and a pinned rule summary, excluding unsupported claim/policy assertions. Vehicle evidence can be a fictional intake snapshot, not a vehicle registry or live policy.
5. Client list DTO lacks source display fields, linked activity/list routes and relationship metadata. Add reviewed optional summaries plus explicit relationship/activity endpoints. Do not expose counts for unimplemented policy/quote modules as factual zeroes; represent unavailable separately.
6. Flag change history needs its own capability-scoped endpoint/DTO. Do not expose arbitrary AuditEvent.Before/After through the client activity list. General client activity shows fixed non-sensitive labels; flag detail history is separately authorized.
7. Define response ETag replay metadata support in SqlCommandBoundary before using it for mutable resource responses. Existing operational commands remain compatible; strengthen with a real SQL replay/stale-token regression.

## Security and model boundaries

Dedicated capability mapping: servicing/UW/senior can client-read/write and contact-write; internal agency-admin can read identity/contacts and maintain contacts but cannot edit shared business identity or see internal flags. Support internal read/write is explicitly granted to servicing/UW/senior in this demo. Match decisions require UW/senior; servicing may view an appropriately scoped review if a separate match-read capability is defined. System-admin has no implicit business mutation/flag grant; combine roles explicitly when support access is required. Finance gets no broad client read in this phase; future financial projections will expose only appropriate fields.

Agency-safe projection takes a trusted relationship scope and explicit FlagVisibility; it must not infer visibility from sharing a Person. Until Phase 4 wires broker sessions, API identities remain internal, and tests call the real SQL projection with trusted synthetic agency contexts to verify both positive grants and negative isolation. A separate internal agency-preview read can expose the exact safe DTO to authorized internal staff with read audit, without impersonation or changing their session.

Linking match intake associates one ClientAccount with a new/existing relationship. Contacts, support grants and all proposal evidence stay attached to their original relationship. Decline returns a safe generic outcome to the submission side. Query persists a request record without claiming email delivery. Every decision is audited and replay-safe; reopening is a new trail event, not deletion of the previous one.

## Existing patterns

SqlCommandBoundary for one transaction/effect/audit/receipt; SqlJobRetry for ETag checks under parent locks; OperationalPaging for protected cursors; OperationalReadEndpoints for allowlisted safe summaries; ActorContext for explicit capabilities; Record/Json helpers in BackOfficeDbContext; SqlFoundationTests for generated DB ownership; AuthenticationTests/OperationalJobTests for real cookies/CSRF/denial; admin-workspace.tsx for retained keys and obsolete fetch protection; primitives.tsx for panels/tabs/status/scroll regions.

## Validation architecture

Pure unit cases cover consent normalization, name preservation, contact-primary transitions, matching decision transitions and flag validation/projections. Real SQL scenarios cover filtered uniqueness, parent-lock races, stale ETags, replay after successful writes, rollback after demotion, same-person cross-relationship isolation, flag grant/revocation and audit disclosure, match decision concurrency/reopen, repeated seed and process/reload persistence. API tests use real local cookies/CSRF and verify all capabilities, request bounds and non-disclosing errors. Browser tests cover create/edit/search/page, contact lifecycle, flag lifecycle/validation, match decisions/history and source-faithful desktop/mobile/error states. No tests may silently skip unavailable SQL.
