# Phase6 data and API design

Reviewed planning design2026-09-16; implementation contracts gated by06-01. This refines approved Phase1 contracts against the actual Phase5 runtime; no table/endpoint below is claimed implemented. Complete strict schema generation, source/UI coverage and plan review before runtime execution. SQL Server2022 remains authoritative; all external effects use persisted deterministic demo adapters.

## 1. Aggregate ownership and version boundaries

Quote remains the concurrency root for new business. Extend its state constraint deliberately to draft, rating-pending, rated, referred, approved, sent, accepted, declined, bound, withdrawn. Add CurrentUnderwritingCycleId nullable and BoundPolicyId nullable with same-quote composite ownership. Keep CurrentRevisionId and immutable QuoteRevision unchanged. No direct state PATCH. Display expired from current rating expiry, not by destructive history updates.

Rating request closes capture atomically and sets CurrentUnderwritingCycleId. CaptureClosedAt/Reason describe this closure; cycle identity is the explicit owner. Draft proposal editing, clone destination writes and fresh matching reassociation/reopen cannot bypass it. An audited return-to-draft command supersedes the active cycle, clears current cycle/closure and resets state, preserving original revision and every old result. Bound and withdrawn quotes cannot reopen. The existing closure race tests must run against real rating and bind commands.

Separate three concepts to avoid a circular invalidation flow:

1. **PricingInputHash** binds canonical proposal, quote/revision/retained ownership, product/rating/binder/authority configuration, commercial terms and term instants. A new risk/ownership/terms/config revision requires a new cycle/rating.
2. **TermsHash** binds completed rating ID/components, current applied endorsements/warranties/cover terms and contractual conditions, commercial settlement and document payload version. Terms-changing decisions require a new terms version; price/limit changes require an explicit return-to-draft/new rating rather than arbitrary manual premium writes.
3. **AssuranceHash** binds active evidence associations and latest accepted/rejected decisions plus relevant referral/condition/carrier decision IDs for this cycle. It changes when proof or approval changes, independently of pricing. Acceptance stores both TermsHash and AssuranceHash. Routine proof completion must not invalidate pricing and create an endless rating/referral loop.

All hashes use canonical closed typed JSON with ordered collections by stable IDs and SHA256. Exclude presentation timestamps/labels from pricing inputs, but retain them in immutable records. Compare IDs as well as hashes. Assessment recomputes current applicability under the held transaction; no persisted Ready/Accepted flag is permission.

## 2. State transitions

| Command | Allowed source | Result and safeguards |
|---|---|---|
| rateQuote | draft; or explicit re-rate from unbound current cycle | Current complete pricing input, resolved matching and published eligibility; create new cycle/request/outbox and close capture. Re-rate supersedes prior cycle and all old acceptance applicability. |
| apply rating outcome | current rating-pending cycle | Immutable result. Valid current output creates individual referrals and state rated/referred/approved as appropriate. Rejected/unavailable output exposes retry/revise; historical/superseded results never alter current pointers. |
| submitQuote | current rated/referred | Persist submission/assignment/routing after assessment. No fabricated task; create case routing identity. Calling again under exact key replays. |
| decide referrals / review proof | active unbound completed-rating cycle | Immutable decisions under actor authority and exact quote/referral versions. Recompute blockers/current terms/assurance; never erase prior decisions. |
| send terms | approved; or current sent for deliberate resend | Current pricing, all send blockers clear, safe recipients, immutable terms/document payload and durable delivery. Queued is not sent; only applied successful demo outcome enables sent. |
| record acceptance | current sent terms with delivered outcome | Exact cycle/rating/terms/assurance; named accepter, received time, channel and evidence. Advance accepted; no acceptance inferred from successful send. |
| issueQuote | accepted | Held current authority, product/terms, expiry, evidence/referrals and exact accepted context. One atomic issue; duplicate source quote cannot produce a second policy. |
| return-to-draft | any active unbound cycle | Required reason; supersede cycle, clear closure/current acceptance applicability. Existing bytes/results remain. Editing follows ordinary capture guards. |
| withdrawQuote | any unbound state | Reason and current ETag; terminal quote, superseded outstanding work applicability. Existing provider outcomes retained. No cancellation of an issued policy through this route. |

Capture assessment retains ordinary closure errors. Progression assessment permits only the held active cycle's own closure while applying the same semantic/eligibility/provenance rules; do not add a generic ignore-errors option. Evidence missing/current-but-unaccepted is a separate referral/issue blocker; invalid structure/term/matching/vehicle provenance prevents rating.

## 3. Storage conventions

UUID primary keys, UTC datetimeoffset(7), exact decimal(19,2) GBP amounts/API decimal strings, binary(32) hashes, nvarchar(max) JSON with ISJSON checks. Mutable root/read-model records have rowversion; snapshots/decisions/events are append-only. Required reason nvarchar(2000), labels bounded200, codes60, reference100, body8000 unless an existing stricter contract applies. No arbitrary storage paths or external URLs. Composite FKs enforce same owner, not merely GUID existence. Store human reference separately from identity.

### Underwriting/configuration tables

| Proposed table | Essential columns | Constraints and indexes |
|---|---|---|
| RatingRuleVersion | ProductId, Version, State, EffectiveFrom/To, DefinitionJson, SchemaVersion | Unique product/version; published immutable typed config; non-overlapping effective selection enforced transactionally. |
| BinderVersion | ProviderId, ProductId, Version, ValidFrom/To, LimitsJson | Unique provider/product/version; nonempty interval; current provider active. |
| AuthorityVersion | ProductVersionId, BinderVersionId, Version, EffectiveFrom/To, RulesJson | Product/binder same product/provider; explicit every supported dimension. |
| UserAuthorityGrant | UserId, AuthorityVersionId, EffectiveFrom/To, LimitsJson, GrantedBy, Reason | Current internal user; no grant above binder; missing dimension never interpreted as unlimited. Seeded controlled configuration; editorPhase11. Grant is not a carrier override. |
| UnderwritingCycle | QuoteId, QuoteRevisionId, ClientId, RelationshipId, ProductVersionId, AgencyTermsVersionId, RatingRuleVersionId, BinderVersionId, AuthorityVersionId, PricingInputHash, InputJson, State, RequestedBy/At, SupersededAt/Reason, CurrentRatingId | Alternate key(Id,QuoteId); composite quote/revision and retained ownership; immutable input columns; unique quote/sequence. Current cycle pointer belongs to quote. |
| QuoteRatingResult | CycleId, QuoteId, WorkId, AttemptId, InputHash, ResultJson, RuleVersionId, CompletedAt, ExpiresAt, Outcome | One applied result per work/provider outcome; immutable. ExpiresAt=CompletedAt+configured14days, additionally bounded by product/binder validity for applicability. |
| QuoteSubmission | CycleId, QuoteId, SubmittedBy/At, RoutingVersionId, AssignedUserId/TeamId | Append-only submission with stable operation identity; current assignment separate audited event if changed. No phantom TaskId. |
| QuoteReferral | CycleId, QuoteId, RatingId, RuleCode, RiskItemId?, RequiredAuthorityJson, Reason, State, Sequence, LatestDecisionId? | Unique(cycle,rule,stable target); nullable target normalized to a non-null discriminator for unique-key semantics. Same-cycle latest decision. Index current state/assigned owner. |
| QuoteReferralDecision | ReferralId, CycleId, Sequence, Outcome, Reason, ActorId, AuthorityVersionId, DecidedAt, ConditionsJson | Unique referral/sequence, immutable; exact quote/cycle/referral versions in request. Bulk command validates all before committing any. |
| QuoteCondition | DecisionId, CycleId, StableConditionId, Kind, Code, Wording, RequiresEvidence, RiskItemId?, EffectJson | Kind documentary/warranty/risk-change; typed catalog codes/effects, no arbitrary executable rule or premium. Risk-change blocks until explicit new draft/rating. |
| QuoteConditionResolution | ConditionId, CycleId, Sequence, Outcome, EvidenceAssociationId?, ActorId, Reason, RecordedAt | Immutable; latest applicable resolution, same-cycle proof; superseding source decision removes applicability without deletion. |

Quote State/current-cycle/current-rating pointer checks and all referenced alternate keys require real migrations and model-snapshot verification. Prefer separate migrations for underwriting and first-issue aggregates rather than one unreviewable migration.

### Evidence, escalation and quotation

| Proposed table | Essential columns | Constraints |
|---|---|---|
| UnderwritingEvidenceAssociation | QuoteId, CycleId, QuoteEvidenceFileId, CaptureEvidenceId?, RequirementCode, RiskItemId?, ConditionId?, InputFingerprint, CreatedBy/At, WithdrawnEventId? | Reuse existing immutable quote file bytes; same-quote file/capture/condition; a requirement or condition must identify the purpose. Withdrawal is separate immutable event. |
| UnderwritingEvidenceReview | AssociationId, CycleId, Sequence, Outcome accepted/rejected, ActorId, Reason, Fingerprint, RecordedAt | Current authorised UW; immutable latest-sequence decision. Screening accepted does not supply this row. |
| CapacityEscalation | QuoteId, CycleId, ReferralId, ProviderId, BinderVersionId, State, CurrentSubmissionId? | Same current referral/cycle/provider; one active escalation per referral/request purpose. |
| CapacitySubmission | EscalationId, Sequence, Body, EvidenceAssociationIdsJson, ContextJson, ContextHash, WorkId, SubmittedBy/At | Immutable exact request, safe provider projection excludes support flags and unrelated private data. |
| CapacityMessage | EscalationId, SubmissionId, Direction, Outcome?, ProviderReference?, UnderwriterLabel?, Body, ReceivedAt, RecordedBy?, ProviderEventId?, EvidenceAssociationId? | Immutable; unique provider/event when present; received time bounded/not future; manual record requires current assigned authority and actual evidence. |
| QuoteTermsVersion | QuoteId, CycleId, RatingId, Number, TermsHash, AssuranceHashAtPreparation, TermsJson, TemplateVersionId, PreparedAt/By | Immutable snapshot of price/components/cover/endorsements/conditions/settlement. Unique cycle/number. |
| QuoteTermsDelivery | TermsVersionId, RecipientSnapshotJson, PayloadHash, WorkId, State, CompletedAt?, OutcomeCode? | Immutable recipients/content; current role/scope governs reads. Lost response never creates second delivery for same command key; deliberate resend uses new request identity. |
| QuoteAcceptance | QuoteId, CycleId, RatingId, TermsVersionId, TermsHash, AssuranceHash, AccepterLabel, AcceptedAt, Channel, EvidenceAssociationId, RecordedBy/At | Immutable, same-cycle FKs; acceptedAt not future and within valid delivered terms window. Requirement is actual evidence association, not a boolean. |

Progression requires uploading/attaching proof after capture closes. Implement a dedicated cycle-evidence command boundary that allows files/associations/reviews for active unbound cycles while leaving proposal/matching closed. Reuse bounded file validation and same-quote storage. Do not relax existing draft-only evidence write routes globally. Source requirements include driver proof, premises-security where applicable, signed statement of fact and condition-specific evidence. The fingerprint includes relevant risk plus exact terms version where the document signs contractual facts.

Prevent the statement-of-fact circular dependency: prepare an immutable terms version once pricing and non-signature underwriting blockers clear; signed-statement evidence binds that version. Sending then requires current accepted proof as configured. A signature is evidence and does not itself record quote acceptance. Preparing the same unchanged terms need not regenerate its ID/hash after signing; AssuranceHash is recomputed separately for acceptance.

Carrier and rating/delivery jobs extend the explicit SqlJobLeases kind allowlist, dispatcher, retry policy and subject-read authorisation. Provider execution occurs outside business transactions, followed by held current-cycle application. Same event/different payload enters quarantine. Stale results remain queryable history and cannot authorise a new cycle.

### First policy and minimal posting

06-11 contract refinement: the early `policy.schema.json` describes normalized
servicing data and requires facts absent from some valid source declarations
(for example licence test date, normalized vehicle ownership and postal address
line1). Issue must not invent those facts. `issued-policy.schema.json` is the
closed `issued-quote-1` snapshot contract: it retains the validated ready-capture
insured/risk/cover declarations, adds owned client/relationship/product IDs,
resolved term, explicitly projected driver basis, selected cover sections,
current endorsements/warranties, exact rated premium/settlement and provenance.
The old normalized contract is preserved for its existing fixtures and later
servicing work. `FirstPolicyView.snapshot` references the issued contract.
Selected section IDs and their premises/vehicle limits remain retained; declared
unselected sections and source responses remain available. Own/customer vehicle
cover is projected only when actually selected in the trusted cover facts.
No licence date, address or zero-valued cover is fabricated during issue.

The issuance service validates this entire bundled schema before any policy
insert. Policy source history remains immutable and exact, including later
changes to client master data. Read routes for policy, owned term, version,
transaction and obligation expose the same first-issue record with source cycle,
rating, acceptance, hash and sequence metadata. These are protected read routes;
later servicing writes remain separate phases. First-issue document kinds are
schedule, certificate and statement, with `requested` status until Phase9.

06-10 storage refinement: `IssueFinancialComponent` gives each original premium,
tax, fee, commission and fee-share amount an immutable identity and coverage
interval. Journal lines reference that identity and transaction through a
composite foreign key. Zero amounts retain a component but create no journal
lines. Each positive component has an exact debit/credit pair; the net debtor
and broker balances match the settlement examples. SQL checks the full component
set, settlement accounts, parties and balance when a draft journal is sealed.
Sealed journals and their lines cannot be altered. These are liabilities only;
no receipt, cash or paid amount is created.

Policy document requests pin one published product template for each of schedule,
certificate and statement, with identical retained payload bytes in the request
and outbox. Template kinds are extended additively; quotation selectors continue
to select only quote-terms templates. Requests remain `requested` until the Phase9
generation workflow exists. Binding a quote requires its own current issued
version, sealed posting and all three requests. Existing quote/history rows are
preserved; `BoundPolicyId` starts nullable and has a same-source composite FK.

| Proposed table | Essential columns | Constraints |
|---|---|---|
| Policy | Reference, SourceQuoteId, AgencyId, ClientId, RelationshipId, ProductId, CurrentTermId | Unique SourceQuoteId and Reference; same-owner current term. Current policy status derived from issued term/time, with no premature servicing state changes. |
| PolicyTerm | PolicyId, Number, StartsAt, EndsAt, LocalTermIntentJson, ProductVersionId, CurrentVersionId | Unique policy/number; positive half-open interval; current version belongs to term. |
| PolicyTransaction | PolicyId, TermId, Sequence, Kind, QuoteRevisionId, RatingId, AcceptanceId, EffectiveAt, ProcessedAt, Reason, OperationKey | Kind new-business inPhase6; unique filtered PolicyId WHERE Kind=new-business and unique(term,sequence); unique Policy.SourceQuoteId independently prevents a second policy for the same quote; immutable. |
| PolicyVersion | PolicyId, TermId, TransactionId, Sequence, SliceOrdinal, SnapshotJson, SchemaVersion, ContentHash, EffectiveAt, ProcessedAt | Unique transaction/slice and term/sequence; immutable full validated issued policy schema. Stable source risk IDs retained; all version ownership constrained. |
| PolicyRegistration | PolicyId, VersionId, RiskItemId, NormalizedRegistration | Version/item ownership and unique version/item; index registration. Current-policy lists query current issued version, historical projections remain for later servicing. |
| IssueFinancialObligation | TransactionId, DebtorKind, DebtorAgencyId?, DebtorRelationshipId?, Currency, Premium, Tax, Fee, Commission, FeeShare, GrossDue, InvoiceDue, NetDue, TermsSnapshotJson | Exactly one valid debtor, same distribution relationship; unique transaction/purpose. Immutable amounts, zero paid/allocated until actual finance workflow. |
| Journal | TransactionId, Purpose, Currency, PostedAt | Unique transaction/purpose; immutable posted entries. |
| JournalLine | JournalId, AccountCode, PartyKind, PartyId?, Debit, Credit, ComponentCode, CoverageStartsAt/EndsAt, SourceComponentId | Exactly one positive debit or credit; bounded cents, same currency; balance checked before insert and with an aggregate posting guard in transaction. No UPDATE/DELETE. |
| PolicyDocumentRequest | TransactionId, VersionId, Kind, TemplateVersionId, PayloadJson, PayloadHash, WorkId, State | Unique version/kind/template/purpose; actual generation remainsPhase9 where not implemented. Show requested/queued honestly. Durable request committed with issue. |

Use the approved financial examples: premium1200.00+tax144.00+fee35.00=gross1379.00;10%commission120.00 gives net debtor1259.00, insurer1224.00 and fee35.00. Net settlement journal Dr receivable1259 / Cr insurer1224 / Cr fee35. Separate settlement retains gross debtor and separate broker payable; direct collection changes debtor to client relationship. Fee-sharing20% of35 creates7broker share and28retained fee. Round each component halves away from zero. No payment/receipt or total-collected result inPhase6.

Issue transaction order under held quote fence: current authority/context/acceptance validation; allocate policy/term/reference; insert transaction/version/projections; post balanced obligation/journal; insert document requests/outbox; update quote bound/current policy; audit and successful ID-only receipt; commit. Any failure rolls back all rows. Unique sourceQuoteId prevents different idempotency keys producing duplicate policies. Same-key replay returns original IDs after current authorisation. A second fresh bind against already bound quote returns409 with authorised existing reference, not a second charge.

## 4. API contract

Product/binder/rules must be applicable at command time and cover the requested insurance term under explicit versioned policy. For the initial demo require binder validity to contain the whole requested term; a current binder that expires before term end cannot silently authorise it. Capture-only or malformed eligibility never becomes published eligibility by a label change.

All paths below are under /api/v1. Commands require authenticated cookie+CSRF, Idempotency-Key, strong If-Match of the owning quote unless stated, bounded strict JSON and reason for decisions/reopen/withdraw. Client supplies expected identities, never actor/state/price/authority. Successful command receipts contain IDs and original ETag; GET returns current scoped result. 401unauthenticated,404foreign/missing,403visible forbidden,412stale token/context,409domain transition,422input,503missing/malformed configuration. No-store on private responses. Child command responses expose the owning quote ETag separately from child ETags; single referral/capacity commands require current quote If-Match plus expected child ETag in their body. Bulk decisions check each child ETag. Contract examples must make that distinction explicit rather than reusing an ambiguous ETag header.

| Route | Request / response | Capability / essential checks |
|---|---|---|
| POST quotes/{id}/rate | expectedRevisionId, reason for re-rate, allowed demo scenario;202 cycleId/jobId | quote-rate; current pricing readiness, published configuration; close capture/create job atomically |
| GET quotes/{id}/underwriting | current cycle/rating/referrals/terms/evidence summary/capabilities plus exact context IDs | underwriting-read; no hidden carrier/internal payload in agency projection |
| GET ratings/{id} | immutable factors/components/input/rule provenance/expiry/state | same-quote current read authority; no generic job permission bypass |
| POST quotes/{id}/submit | cycleId, reason;200 submission/assignment | quote-submit; current result/routing and no client-supplied authority |
| POST quotes/{id}/return-to-draft | cycleId, reason;200 quote | quote-revise; unbound; supersede current applicability and release own closure only |
| GET referrals?quoteId=... | scoped filtered cursor page | underwriting-read; current query scope before counts |
| GET referrals/{id} | rule/authority/context/history/conditions | underwriting-read |
| POST quotes/{id}/referral-decisions | cycleId; selected referralId/ETag/outcome/reason/typed conditions | underwriting-decide; each dimension and version; all-or-none; no blanket approve-all bypass |
| POST referrals/{id}/decisions | same single-decision semantics | same shared service as bulk, not a weaker alternate route |
| POST quotes/{id}/underwriting/evidence-files | bounded filename/type/actualmultipartbytes;201 fileId | underwriting-evidence-write; current active cycle; reused file validator/storage |
| POST quotes/{id}/underwriting/evidence | cycleId,fileId,requirement/condition,target,fingerprint,reason;201associationId | same owner/proof requirement; changes assurance |
| POST quotes/{id}/underwriting/evidence/{associationId}/withdraw | reason | active unbound cycle; immutable withdrawal, invalidates assurance |
| POST quotes/{id}/underwriting/evidence/{associationId}/reviews | accept/reject,expectedFingerprint,reason | underwriting-evidence-review; current same-cycle association; immutable sequence |
| POST referrals/{id}/conditions/{conditionId}/resolutions | evidenceAssociationId,outcome,reason | underwriting-decide; exact current decision/condition, no terms-changing shortcut |
| POST referrals/{id}/escalations | providerId,reason;201 escalationId | underwriting-escalate; authorised provider/binder/rule |
| GET escalations/{id}; GET escalations/{id}/messages | current summary and immutable scoped history | underwriting-read; parent quote bound to cycle |
| POST escalations/{id}/send | body,evidenceAssociationIds;202jobId | current escalation/submission; safe persisted request |
| POST escalations/{id}/responses | outcome,underwriterLabel,providerReference,body,receivedAt,evidenceAssociationId | underwriting-record-capacity; actual assigned authority/evidence, typed limits/conditions |
| POST quotes/{id}/terms/prepare | cycleId,ratingId,templateVersionId;201termsVersionId | quote-terms; typed immutable contractual payload; may prepare before signature proof |
| POST quotes/{id}/terms | termsVersionId,recipientContactIds;202deliveryId/jobId | quote-terms; safe current same-relationship recipients and required send proof |
| GET quotes/{id}/terms | current/history terms and delivery outcomes | quote-read/internal; safe agency projection separately defined |
| POST quotes/{id}/acceptances | cycleId,ratingId,termsVersionId,termsHash,assuranceHash,accepterLabel,acceptedAt,channel,evidenceAssociationId | quote-acceptance; exact current delivered valid terms |
| POST quotes/{id}/issue | cycleId,ratingId,acceptanceId,termsHash,assuranceHash,reason;201policyId/termId/versionId/transactionId/obligationId | policy-issue-within-authority; actual current multidimensional authority and all prerequisites |
| GET policies | q,product,agency,client,registration,state,inception-range,sort,cursor,pageSize | policy-read; real scoped current projections and protected filter-bound cursor |
| GET policies/{id}; GET policies/{id}/terms; GET terms/{id}/versions; GET versions/{id} | actual summary/term/version and first transaction/financial-obligation summary | policy-read; same-owner joins, immutable snapshot; no financial mutation capability |

Exact DTO schema/enum/length/nullability examples and any existing operationId adjustment are mandatory in the contract gate. Do not expose an old broad draft DTO as an implemented route. Extend client policy and agency-sharing read models using existing route conventions after confirming current source signatures; do not invent duplicate client/agency paths.

## 5. Locks, security and integration

Retain agency → current actor → relevant intake/review → quote → client/relationship ordering. New cycle/referral/evidence/terms/acceptance locks follow the quote root in deterministic ID order. Multiple agencies lock ordered IDs. No later handler may acquire an earlier root after holding a later one. For read-only scope use held consistency through materialisation; do not retain locks across network calls.

Internal servicing can capture/rate/submit/read; UW/senior can act only within effective authority; servicing cannot bind under the current roadmap acceptance contract. System-admin has no implicit underwriting. Broker identities receive safe own-agency policy/quote summaries only in this phase; no new broker capture/bind route or portal. Source permission possibilities are not automatic grants. Current stored agency/user/relationship state applies before replay and reads.

No support flags, credential/session material or private cross-agency context in pricing/provider/terms projections. Audit contains event-specific redacted identities/reasons, not raw files or secrets. All child IDs checked against same quote/cycle; not-found responses do not reveal foreign existence. Exact proof downloads reuse protected same-quote file routes after current auth.

## 6. Design verification and remaining gate

Require unit/property fixtures for hash partitions, state progression,14-day boundary, all authority dimensions and balanced money; realSQL constraints/immutability/migration/replay/late rollback/parallel different-key issue; provider crash and stale apply; both-product UI and current scoped policy discovery. Explicitly test proof completion does not force an endless new rating, signed statement preparation does not deadlock send, and proof changes after acceptance prohibit bind until fresh valid acceptance.

This design is accompanied by06-RULE-CATALOG,06-UI-SPEC, source task mapping and06-PLAN-REVIEW. Task06-01-01 produces and validates exact generated schemas/JSON-pointer fixtures before runtime execution. Plans06-02/05/07/08/10 apply feature-owned migrations before verification. Capacity extensions require exact dimension/submission/validity, and acceptance proof must be present before hashing assurance. No new runtime capability is enabled by these planning documents.
## 06-05 implementation handoff (verified in4ace720)

The proof/decision slice uses immutable `UnderwritingEvidenceEvent` rows for both
review and withdrawal, with explicit kinds and separate latest-review/withdrawal
pointers on the association. `QuoteConditionResolution` records both the exact
association and accepted review ID; later review changes or withdrawal invalidate
resolution without rewriting history. All pointers have same-owner composite
foreign keys and append-only/monotonic guards.

`UnderwritingPreparedTermsFence` keeps association TermsVersionId NULL until
06-08 creates prepared terms. That plan must replace the fence with same-quote,
same-cycle terms ownership and enable signed-statement conditions. A supplied GUID
cannot currently stand in for a prepared terms record.

The assessment now projects proofRequirements (purpose, stable target, optional
condition/terms identity, inputFingerprint and satisfaction), actual applied
endorsements and assuranceHash. Conditions expose child ETags; decisions expose
the stored query question. UI06 must consume these projections. Terms08 must use
actual applied warranty wording in the terms hash and current assurance in
acceptance, retaining the distinction from the unchanged pricing hash.

## Capacity escalation implementation (06-07)

CapacityEscalation is the mutable current pointer for one quote/cycle/referral.
CapacitySubmission, CapacitySubmissionEvidence and CapacityMessage are immutable,
with same-owner composite keys and SQL append-only guards. The selected provider
must own the retained binder. Submission context includes exact revision, pricing
hash, term, rule/dimension/target, selected proof IDs and versioned demo scenario;
it excludes full proposal, contacts, support flags and file bodies.

Six capacity-escalation/* SettingVersions define explicit fictional outcomes.
Each submitted request owns one durable outbox operation; retries retain its
submission, operation identity and attempt history. The source-aligned default
is two working days, using a Monday–Friday demo calendar and preserving London
wall-clock time across DST. A bank-holiday calendar is not configured. Schema2
settings support1–10workingdays. Original schema1 elapsed-hour defaults receive an
appended version only when they exactly match the original demo seed; operator
overrides and all recorded deadlines are preserved.
Duplicate provider events reuse the inbox; conflicting content is quarantined.
Superseded responses remain visible and cannot grant current authority.

A supplied response requires an actual uploaded, accepted file associated with
capacity-response and the exact CapacitySubmissionId/fingerprint, plus its current
accepted underwriting review. Record actual underwriter, reference, body and UTC
received time. Carrier conditions have an explicit decision row linked from the
message; they remain active independently of subsequent staff decisions. All
active conditions share the 100-condition quote-cycle bound. Withdrawal of relied
upon proof invalidates approval without rewriting correspondence or pricing.

A capacity extension overrides only its exact dimension, quote/cycle/submission
hash, target where applicable, amount/age/trade permission and validity interval
covering the whole term. It does not amend grants/binders or approve unrelated
referrals. Current identity/grants and provider availability precede receipt replay.
Read capability projections retain shared scope locks; they do not upgrade to
write locks after taking quote read locks. Mutations and workers retain their
established agency→identity→quote→work/child lock ordering.

The escalation page adapts prototype MTA actions to the actual new-business quote:
return to draft, revise captured risk and obtain a new rating. Literal MTA changes
remain Phase 7. Generic admin jobs remain diagnostic; capacity jobs use the scoped
job route, and recovery requires both integration-retry and underwriting-escalate.
Preparing terms, delivery, acceptance and policy issue remain subsequent slices.

## Quotation backend implementation (06-08)

Migration20260917013041 adds immutable product TemplateVersion, QuoteTermsVersion
and QuoteAcceptance plus mutable processing state on immutable QuoteTermsDelivery
requests. UnderwritingCycle owns exact current terms/delivery/acceptance pointers.
Composite keys and SQL guards enforce same quote/cycle/rating/delivery/proof
ownership, append-only history and terminal outcome provenance. The earlier
prepared-terms NULL fence is replaced by real ownership and proof-purpose checks.

Prepare clears current non-signature blockers and stores a structured contractual
payload: exact revision/rating/template, risk facts, cover, warranty wording,
prices and retained settlement. Documentary signature/acceptance proof is outside
that payload; reviewing it cannot rerate or recursively regenerate terms.
Preparing identical contractual content returns the same version. Warranty
changes produce a new version and clear current delivery/acceptance applicability.

Send snapshots actual active same-client/relationship contacts with valid email
addresses. It requires reviewed signed-statement proof for the exact terms and
current decisions/configuration. One durable quote-delivery work item retains
recipients, payload hash, scenario and assurance at send. The versioned demo
adapter supports success, rejection, transient-once and timeout-after-success;
its operation is persisted independently of current application. Current identity,
recipient values, proof, template, pricing and lease are rechecked on application.
Only an applied successful outcome is delivered. Nothing is transmitted externally.

Acceptance is a separate immutable command: exact current successfully delivered
terms, rating and assurance; named accepter, explicit received instant/channel and
actual reviewed acceptance-proof association. That proof exists before hashing
assurance; acceptance itself is excluded. Future/before-delivery/expired/foreign/
unreviewed/withdrawn proof and stale contractual contexts are denied. Later changes
remove current acceptance applicability without rewriting history. Policy issue
remains closed until its owning later slices.

Current template/contact options and protected independent history cursors are
exposed by GET quotation terms. Cursors bind to quote and delivery versions,
avoiding invalidation by unrelated database jobs. Exact current scope is checked
on every page. Delivery retry uses the scoped job route, both quote-terms and
integration-retry capabilities and bounded retained attempts/operation identity.
Generic admin discovery never confers business subject access.

The plan's ActorContext capabilities already existed, so they are reused. Backend
runtime verification is recorded in06-08-SUMMARY after the full gate; source-facing
quotation/acceptance controls and browser verification remain06-09, generic document
generation remainsPhase9 and final source acceptance remains06-14.
