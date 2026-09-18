# Phase 7 data and API design

Status: proposed implementation contract, 2026-09-17; plan review pending.
Decisions implement 07-CONTEXT D-01..12. Existing contracts remain unchanged until
the owning implementation plan updates generators, schemas and runtime together.

## Aggregate and provenance boundaries

Policy retains immutable agency/client/relationship/product/source-quote ownership.
An insured-name correction changes the contractual snapshot through a draft; it
cannot silently transfer the policy to a different client or agency. Renewal
creates another term of the same policy. A servicing operation never writes the
bound source quote's captured risk or its underwriting cycle.

| Record | Identity and contents | Integrity boundary |
| --- | --- | --- |
| ServicingDraft | Id, PolicyId, BaseTermId, BaseVersionId, kind adjustment/renewal/cancellation, state, current revision/cycle, requester, creator, reason, rowversion | Base term/version belong to policy; one live adjustment per term and one live renewal per expiring term; cancellation may coexist until atomic issue abandons conflicts. |
| ServicingRevision | Id, DraftId, sequence, schema version, proposal JSON/hash, schedule, local-time intent, recorded actor/time | Append-only; unique draft/sequence; current pointer uses same-draft FK. |
| ServicingLease | DraftId, holder user, opaque generation, expiresAt, rowversion | One row per draft; current actor and generation checked under lock; expiry uses server clock. |
| ServicingDecisionCycle | Id, DraftId, RevisionId, BaseVersionId, input hash, configuration pins, state, current rating/terms/delivery/acceptance IDs | Same-draft and same-cycle compound ownership; edits supersede rather than delete. |
| ServicingRating | Id, CycleId, immutable inputs/results, annual slice premiums, movement components, work/attempt IDs, expiry | Current configuration and full dated schedule bind the result; deterministic persistent adapter execution. |
| ServicingReferral and Decision | cycle/rating/rule/dimension/stable item, required authority, status; immutable decisions with actor/grant/reason | Current authority plus specific cycle; assignment grants no authority. |
| ServicingEvidenceAssociation and Review | cycle/revision/purpose/stable item/file-version, status; immutable review and withdrawal events | Authorize file and subject; do not reuse quote association approval as servicing approval. |
| ServicingTerms, Delivery, Acceptance | exact cycle/rating/terms hash, recipient, work/result, evidence and recorded intent | Acceptance requires actual current delivered terms and valid evidence; amendments invalidate applicability. |
| CancellationApproval | draft/revision/preview hash, authority version, eligible approver, reason/time | Separate explicit decision; requester cannot self-approve where a separate approver is required. |
| RenewalExperience | revision, observation dates, declared claim count, paid/outstanding, earned premium, source, evidence, recorder | Typed auditable declaration; unknown is distinct from zero; changed experience creates a revision. |
| ServicingLifecycleEvent | policy/term/draft, kind, effective/processed dates, actor/reason, unique operation key | Append-only lapse/abandon/takeover events; lapse does not invent an issued policy term. |

Names are proposed. Final model review must make nullable relationship sets and
insert order explicit. Prefer a servicing-owned capacity graph using the same
pure authority rules and persisted demo provider contract. Existing capacity and
evidence commands take quoteId/cycleId explicitly; they cannot be called against
a servicing draft without subject-specific ownership checks.

PolicyTransaction adds servicing decision provenance and nullable quote decision
columns. New-business requires the existing complete quote provenance and no
servicing decision; adjustment/renewal require the exact servicing issue decision;
cancellation requires its reviewed approval/preview decision. SourceQuoteId stays
the policy's original source, not a fake new underwriting source. Enforce these
alternatives with SQL checks, compound FKs and branch-specific source triggers.

## Proposal and immutable snapshot contracts

Servicing proposal is a closed versioned envelope: baseVersionId, requestedBy,
reason, commonEffectiveIntent, changes[]. Each change has a stable changeId,
target kind, existing or newly allocated riskItemId, typed operation and payload.
Only cover changes accept a later effective override. Added IDs are allocated
once and survive retries/reloads; remove/update commands require a base or
previously added item of the correct type. Reordering is not a material change.
Reject duplicate/conflicting changes to the same target at the same effective
instant. Business, premises, drivers, vehicles and cover use typed editors, not
the prototype's generic free-text after-description as an issued risk model.

Build cumulative full-risk slices in effective order and rate each full combined
risk; calculate the incremental annual difference from the preceding slice.
The schedule hash includes every date and stable change identity. One issue
transaction owns every slice and one fee. Return all versionIds with the earliest
slice's versionId for compatibility. Version sequence advances for each slice;
transaction sequence advances once. Do not conflate them.

Keep old issued-quote-1 bytes and hash unchanged. Introduce a separately validated
issued-servicing-1 envelope retaining product, insured, term and risk locations
needed by registration/document integrity, with explicit draft/revision/cycle
provenance. Cancellation retains risk history plus explicit cancellation outcome.
Versioned readers dispatch on format; unsupported format is a clear error.

## Financial movements and posting periods

Retain IssueFinancialObligation and Journal identities. Add purpose-specific
movement semantics while keeping all first-issue restrictions for old rows.
IssueFinancialComponent gains an ordinal (existing rows default to 1), yielding
unique (ObligationId, Code, Ordinal), signed amount and original-component lineage
for returns. Each movement has its own half-open earning interval. JournalLine
continues to reference the new transaction's movement component; original source
lineage is a separate FK and must belong to the same policy/term and currency.

At sealing, verify all expected movement components, per-code aggregate totals,
positive debit/credit pairs with sign-aware account directions, original parties,
balanced journal, invoice effect and broker remuneration effect. Zero movements
have no lines. Positive fees and negative premiums are allowed in the same MTA.
Once sealed, no append or mutation is allowed. Existing owner/hash guards remain.

Introduce the minimum AccountingPeriod record needed for real enforcement:
Id, startsOn, exclusive endsOn, state open/closed, rowversion, with nonoverlap
validation. Posting holds the chosen period lock. Use the period containing the
processing date if open, otherwise the earliest permitted later open period;
absence blocks posting. Journal stores period and posting date separately from
insurance effective time and original correction reference. Period-closing UI
and full finance operations remain Phase10. Tests must close a period and prove
the open-period fallback/race behavior; a label alone is insufficient.

### Mixed multi-date worked example

Fictional rules: 2026 calendar-year cover, original annual premium 1200.00,
12% tax, 10% commission, agency net settlement, no fee share. One adjustment has
annual difference +600 from 15 September (108 days), then -300 from 1 October
(92 days). Round each component movement to pennies away from zero. One fee 15.

| Movement | Premium | Tax | Commission |
| --- | ---: | ---: | ---: |
| First slice delta | 177.53 | 21.30 | 17.75 |
| Second slice incremental delta | -75.62 | -9.07 | -7.56 |
| Adjustment totals | 101.91 | 12.23 | 10.19 |

Gross due is 129.14 including the fee; net agency due is 118.95. The second delta
is against the first cumulative risk, not the original risk. Repeating the full
second annual premium difference would double-count the first change.

Cancellation on 15 October leaves 78 days. Reverse each posted component over
its own original interval, including the negative second movement:

| Original movement reversed | Premium effect | Tax effect | Commission effect |
| --- | ---: | ---: | ---: |
| Original issue, 365 days | -256.44 | -30.77 | -25.64 |
| First MTA slice, 108 days | -128.22 | -15.38 | -12.82 |
| Second MTA slice, 92 days | 64.11 | 7.69 | 6.41 |
| Total | -320.55 | -38.46 | -32.05 |

Net agency credit is 326.96; previously charged fees remain retained with zero
new fee. These numbers were checked using Python Decimal and ROUND_HALF_UP;
they must become independent expected-value .NET tests, not recomputed expected
values from the implementation. No cash payment is implied.

## Command contracts and concurrency

Preserve the existing /terms and /drafts contract family, mounted under /api/v1.
Authorize policy ownership through the term/draft on every route. Do not accept
agency/client/product ownership from request bodies. Every mutation uses CSRF,
current permissions, a bounded strict body and the established idempotency
receipt semantics; fresh mutations also require current ETag and lease generation
where editing authority applies. Current authorization precedes receipt replay.

| Route or operation | Required behavior |
| --- | --- |
| POST /terms/{termId}/drafts | Create typed adjustment/renewal/cancellation against explicit base; return persisted draft, revision and ETag. |
| POST/PUT/DELETE /drafts/{draftId}/lease | Acquire/renew/release with generation; explicit takeover mode requires reason and permission; delayed old-generation heartbeats fail. |
| PUT /drafts/{draftId}/proposal | Save incomplete typed revision, validation and actual diff; invalidate dependent applicability without deleting history. |
| POST /drafts/{draftId}/rate | Queue exact immutable input; return work ID and pending state, not fabricated rated success. |
| POST /drafts/{draftId}/submit | Require complete valid rating; create current referrals/authority workflow, not issued cover. |
| Draft evidence/referral/capacity subresources | Explicit same-cycle authorization, upload/attach/review/withdraw, resolution and persistent provider response history. Add missing contract operations during owning plans. |
| Draft terms/delivery/acceptances | Publish exact resolved terms, record actual demo delivery and separate evidenced acceptance. |
| POST /drafts/{draftId}/issue | Recheck base, lease, dates, current authority, rating, proof and acceptance/approval; atomically write graph, finance, requests, audit and receipt. |
| POST /drafts/{draftId}/abandon | Reason required; preserve history and invalidate further issue/worker authority. |
| POST /drafts/{draftId}/renewal-invitation | Persist exact invitation/outbox; state invited only after applied delivery. |
| POST /terms/{termId}/lapse | Require eligible unaccepted renewal, reason, configured clock; deduplicate event and queue notification. |
| GET /drafts/{draftId}/cancellation-preview | Return revision/base/ledger/config-bound preview hash, components, notice/authority blockers; no mutation. |
| Cancellation approval command | Persist eligible independent approval against exact preview hash; edits or changed ledger invalidate it. |
| GET /terms/{termId}/as-at | Explicit E/K and basis, selected immutable version, applied/excluded transactions, version document requests and coverage outcome. |
| POST /terms/{termId}/as-at/export | Durable immutable reconstruction request; pending renderer remains Phase9. |
| Policy clone command | New incomplete quote, fresh item IDs, source lineage and target relationship authorization; no copied underwriting acceptance or private flags. |

Missing precondition is 428, stale ETag 412, invalid syntax 400, field/domain
validation follows API-CONVENTIONS, state/lease/base conflict 409, and unauthorized
objects use established 403/404 scope conventions. Strictly validate both IDs
and lineage for comparisons. Lists and historical reads are no-store; agency
sharing retains its seven-field allowlist and cannot search hidden registrations.

## Remaining design gates

### 07-06 explicit underwriting submission refinement

The implemented rating worker already derives the cycle's referrals. The planned
`POST /drafts/{draftId}/submit` must persist the explicit handoff without creating
duplicate referrals or changing issued cover. A rating request or toast alone is
not submission. This refinement is implementation guidance, not a completed API.

Use an immutable `ServicingUnderwritingSubmission` record with `Id`, `DraftId`,
`CycleId`, `RevisionId`, `RatingId`, `InputHash`, `Reason`, `SubmittedBy`,
`SubmittedAt`, and ordinary stored-record provenance. Enforce a unique cycle
submission and compound ownership to the existing cycle and rating keys. Guard
immutable rows and require the submission's input hash to equal its cycle's hash.
Retain older submissions when the proposal is edited, rerated or abandoned;
current applicability derives from the current owned cycle, revision and rating.

The command requires current `policy-draft-write` access, strong draft If-Match,
the editing lease, CSRF and an idempotency key. Accept only `cycleId`, `revisionId`
and a reason. Hold the current policy/draft scope before receipt replay; reject
foreign or superseded cycles, incomplete capture, expired or unavailable rating,
changed base and lost lease. Reuse the rated cycle's existing referral workflow;
outstanding underwriting proof can be submitted for review and must remain
independently blocking for approval/issue. Do not require an underwriting grant
merely to submit a servicing case. A different new key cannot create a second
submission for the same cycle.

Return a durable submission receipt and updated draft ETag. Provide scoped,
bounded no-store submission history/current status so reload and rerating show
the actual handoff state. UI retains the exact command through uncertain results,
shows the saved submission, and links to that draft's referral section. Verify
both Motor Trade products through SQL and actual browser commands, including
same-key retry, duplicate submission, stale/foreign scope, CSRF/lease failures,
post-rerating history and unchanged issued policy JSON. Generic notifications
and task inbox projections retain their later phase ownership; do not fake an
external delivery or completed task here.

Submission history HTTP refinement: implement `GET /drafts/{draftId}/submissions`
with `policy-read`, no-store and the existing actor/route/draft-ETag-bound signed
cursor (`pageSize`1..50). The planned response has `draftId`, `draftEtag`,
`assessedAt`, nullable `currentCycleId`, nullable `current`, `items`, and nullable
`nextCursor`. Each item contains `id`, `cycleId`, `revisionId`, `ratingId`,
`inputHash`, `reason`, `submittedBy`, `submittedAt`, and `applicable`. Current
submission is returned separately even when browsing an older history page.
Applicability describes whether that handoff still matches current valid rating;
it does not represent proof acceptance, underwriting approval or issue authority.
Historical records remain readable after rerating and abandonment. A changed
lease can block an exact retry after success, so UI recovery must read persisted
submission status rather than inventing another command or claiming failure.
Implemented HTTP contract in5301304: strict POST submit and scoped GET submissions,
generated schemas, actual SQL/HTTP negative tests and response validation pass.
UI/browser submission integration verified in5be1f37 for both Motor Trade
products. The existing demo database is additively migrated through submission
storage and its preview includes the verified routes and UI.

### 07-06 specific referral-work navigation

SourceCTL-d2bf3015d449 is a specific underwriting-referral row linking to the
prototype task page. It now opens focused servicing referral work at the same
draft with a reloadable `#servicing-referral-{id}` link. A dedicated no-store
`GET /drafts/{draftId}/referrals/{referralId}` returns exactly one current owned
referral, its real conditions, decision readiness and latest decision. It rejects
foreign/missing referrals and stale cycles; UI shows current authority and saved
history and records decisions through the existing scoped commands. Returning
to the list restores the current rated referrals without silently replacing an
old work link. This uses actual rule identities rather than hardcoding the
prototype's fictional UW-14/TSK-2291 values. Generic task inbox, assignment, due
dates and comments retain their approved Phase9 ownership under D-10.

Review against full source field/control coverage, current API-CONVENTIONS and
PERMISSIONS. Specify exact schema fields and cancellation rule/approval catalogue,
renewal missing-experience handling, fair-value provenance, capacity subject
records and document purposes in the owning plans. Then create UI-SPEC, validation
mapping and checked sequential plans. This document does not activate endpoints
or declare the phase planned.

## 07-07 capacity implementation refinement

Keep servicing capacity separate from quote-owned records. The first storage
prerequisite is ServicingCapacityCase: Id,DraftId,RevisionId,CycleId,RatingId,
ReferralId,ProviderId,BinderVersionId,Reason,State,RaisedBy and ordinary mutable
record provenance. Unique ReferralId; compound referral FK includes all draft/
revision/cycle/rating identifiers. Provider is derived from the pinned binder,
never accepted as an arbitrary caller choice. Insert requires current rated draft,
unexpired rating, current non-declined referral and active exact binder provider.
Case provenance and original reason are immutable; deletion is forbidden.

Until submission/response graph and worker are implemented, the first migration
permits only draft or superseded states. No API is opened by this prerequisite.
A subsequent additive migration must add owned current-submission/current-response
FKs and replace this narrow guard with pointer/history/state consistency checks;
it must not rewrite the initial migration or allow approved state without exact
response provenance. Case history must survive withdrawal, reopen and rerating.

ServicingCapacityRules uses explicit draft/revision/cycle/rating/case/referral/
provider/submission/hash identity. CapacityRules.Covers is the shared subject-neutral
limit arithmetic; no servicing draft is passed as a fake QuoteId. ExtentApplies
is only response extent: current grants, provider, evidence, current conditions,
proof readiness and every dated exposure remain mandatory service/worker checks.
ActionState retains history/pointers while draft state fences late delivery.

### Servicing capacity submission ownership

ServicingCapacitySubmission pins CaseId,DraftId,RevisionId,CycleId,RatingId,
monotonic Sequence,Body,Reason,ContextJson,ContextHash,WorkId,ScenarioVersionId,
SubmittedBy/At and ResponseDueAt. Hash is SHA-256 of the stored UTF-8 context
bytes; context has its own servicing-capacity-submission-1 format and explicit
owner identifiers. Unique case/sequence and WorkId; compound case ownership and
same-case CurrentSubmissionId FK. Outbox kind/subject/operation/scenario must match
before insert. No quote-owned submission or quote receipt is reused.

Submission insert requires a current unexpired owned rating and active pinned
provider, and a case eligible for a new submission. A new submission resets prior
response applicability when the response graph is added; it never rewrites history.
Case pointers are monotonic. Withdrawal keeps the pointer and moves to draft;
returning to queued requires a strictly newer submission. Until response storage
is implemented no approved/conditional/queried/sent states are enabled. Every
row is append-only and retains exact provider request bytes, caller reason and
server-derived provenance. Storage is a prerequisite, not an activated endpoint.

### Capacity correspondence and response boundary

ServicingCapacityMessage is immutable staff correspondence (submission, chase,
query-reply), separately sequenced per capacity case and linked by the complete
submission/case/cycle/draft/revision/rating tuple. Incoming responses will use a
separate servicing-owned response record because they carry provider provenance,
extent and evidence that an outbound chase cannot grant. The case read model will
merge these histories by timestamp with explicit direction/provenance.
The correspondence hash is SHA256 of stored UTF-8 body bytes; relational ownership,
actor, timestamp and kind are immutable and validated independently by SQL.

ServicingCapacityResponseRules validates typed response extent and conditions
before persistence. Each carrier condition has a definition and explicit ordered
EffectiveDates; it is parsed against every corresponding cumulative proposal.
Dates cannot be inferred from only the final snapshot. Query/decline carry no
approval dates, limits or conditions; conditional approval requires conditions.
These library/storage refinements do not activate routes before service/SQL/UI
verification is complete.

### Supplied-response evidence applicability

The nullable CapacitySubmissionId is server-derived from an exact purpose
fingerprint and stored with compound cycle/draft/revision/rating ownership.
Response proof requires the current non-withdrawn capacity submission; prior
purpose associations remain history. Projection exposes response proof as an
available supplied-response purpose, not a universal condition for demo-provider
responses. Final eligibility must require it when provenance is supplied-response
and independently evaluate normal risk proof and all current carrier conditions.
No carrier reply grants authority merely because its evidence review is accepted.
