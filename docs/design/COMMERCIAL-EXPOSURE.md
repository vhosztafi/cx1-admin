# Commercial Combined exposure storage

Implemented in Phase 08-08. This is an internal storage/application contract. Phase 08-09 owns the authenticated issue command, shared book fence, complete issue transaction, exact receipt and scoped aggregate API; Phase 08-10 owns policy presentation. Exposure assessment is an observation, not a capacity reservation.

## Immutable records

| Record | Retained fields and rules |
| --- | --- |
| CommercialExposureBook | Id, ProductId, ProviderId, Code, CreatedAt, CreatedBy. One stable book per commercial product/provider, independent of binder version. |
| CommercialExposureBinder | Id, BookId, BinderVersionId, CreatedAt, CreatedBy. A binder maps once to the same product/provider book. Published successor binders retain that book. |
| CommercialExposureLimitVersion | Id, BookId, District, Version, Amount, EffectiveFrom, EffectiveTo, PublishedAt, SupersedesLimitId?, PublicationJson, ContentHash, CreatedAt, CreatedBy. District is a canonical UK outward district or the exact default marker `*`. Amount is nonnegative GBP with two decimal places. |
| CommercialExposureVersion | Id, BookId, BinderVersionId, PolicyId, TermId, TransactionId, VersionId, SourceHash, TermStartsAt, TermEndsAt, EffectiveAt, ProcessedAt, TransactionKind, TransactionSequence, SliceOrdinal, LocationsJson, CreatedAt, CreatedBy. One header per issued version, including cancellation and zero property. |
| CommercialExposureLocation | ExposureVersionId, RiskItemId, District, SumInsured. Composite primary key prevents duplicate locations. SQL inserts the complete child set in the header INSERT transaction. |

All five tables reject updates/deletes. Compound version provenance and the immutable policy source verify the complete policy/term/transaction/version identity, exact hash, dates, kind and ordering. Non-cancellation sources require `issued-commercial-1` and their originating underwriting/servicing binder. Cancellation uses the verified base version's book/binder and an empty header. Location IDs, normalized districts and buildings + contents + stock must match the source snapshot. BI, liability, wages, MEL, and other cover are excluded from property accumulation.

SQL owns child materialization. A caller cannot leave a header with missing children, append an unowned child, change an amount, or delete a historical child. Source validation or child insertion failure rolls back the complete header insertion. No historical interval is closed by updating a row.

## Limits and knowledge

PublicationJson has schemaVersion `commercial-exposure-limit-1`, bookId, district, version, amount (fixed two-place string), effectiveFrom, effectiveTo, publishedAt, publishedBy, supersedesLimitId and reason. ContentHash is SHA-256 of exact UTF-8 JSON. SQL checks the columns against that publication and its author. Replacement must reference an earlier version in the same book and district/default scope, published no later than its replacement.

At effective instant E and knowledge cutoff K, consider publications with PublishedAt <= K and EffectiveFrom <= E < EffectiveTo. Applicable district-specific limits take precedence over defaults. An applicable explicit descendant replaces its ancestors only within its own interval. An expired temporary replacement therefore restores its still-applicable ancestor. Multiple competing applicable descendants remain ambiguous; neither larger amount nor later version is silently preferred. Missing/ambiguous applicable limits block assessment. Every result pins the selected limit ID and hash.

CommercialExposureSeed.SeedAsync requires a held initialization transaction. It adds missing book/binder mappings and an initial fictional £40m default only when a book has no limit publication. Its fictional publication/effective start is the initial binder's effective start and its end is that binder's effective end. Later binder publication does not extend a limit or modify history: a later uncovered interval remains blocked until explicitly configured. SetPublication produces the exact retained JSON/hash. Repeated initialization preserves existing publications and source records.

## Application operations

- CommercialExposureProjection.AppendAsync(db, versionId, binderVersionId, actorId, token) requires a held transaction and creates the verified header/children without committing. Duplicate projection is rejected; the enclosing issue command owns idempotency.
- ReadAsync(db, bookId, knownAt, token) returns immutable CommercialExposureSlice records using exposure tables only. LimitsAsync returns the visible published limit records. Neither query joins mutable foreign Policy rows.
- CommercialExposureRules.Snapshot(source, bookId, effectiveAt, knownAt) selects active term/version winners using the existing PolicyTemporalSelector order: active term start, effective date, transaction sequence, slice ordinal, version ID. Scheduled, expired and cancelled winners contribute zero.
- Assess(existing, proposed, publishedLimits, bookId, policyId, from, to, knownAt) replaces the complete own-policy timeline for [from,to). Known future own changes must be represented explicitly. It checks existing/proposed term and version boundaries and visible limit boundaries, summing all positive same-district property locations and counting distinct policies.

CommercialExposureInterval returns From, To, District, ProposedPropertySum, OtherPropertySum, ResultingPropertySum, PolicyCount, LimitVersionId?, LimitHash?, Limit?, Headroom?, Blocker?. Allowed requires at least one interval and no blocker. Closed blockers are `commercial-exposure-limit-missing`, `commercial-exposure-limit-ambiguous`, and `commercial-district-capacity-exceeded`. Invalid/incomplete source timelines throw before assessment. The public API must expose aggregates only after its current scope checks; internal slices contain policy IDs and are not API DTOs.

## Atomic first issue (08-09)

`CommercialExposureLock.AcquireAsync(db, token)` requires a held transaction and acquires the canonical transaction-owned exclusive resource `CoverMGA.CommercialExposure` before parent scope locks. Its timeout is five seconds; negative acquisition returns become `commercial-exposure-busy` (409). `RequireAsync` and SQL insert guards reject exposure/limit/decision writers lacking that fence. Initialization uses the same ordering. Administrative limit publishers must acquire it before reading their publication scope.

The existing `POST /api/v1/quotes/{quoteId}/issue` retains its exact input, ETag, CSRF and idempotency contract. Current scope and authority precede saved receipt lookup. Only a new effect checks accepted terms/proof and reassesses all dated book breakpoints. `WithinAuthority(assessment, actorLimit, binderLimit)` also blocks `commercial-district-authority-exceeded` without replacing the published limit pins. A book publication cannot extend staff or binder authority.

`CommercialExposureIssueDecision` stores Id, ExposureVersionId (unique), AssessedAt, DecisionJson, DecisionHash, CreatedBy and CreatedAt. Its JSON format `commercial-exposure-decision-1` contains bookId, policyId, versionId, sourceHash, assessedAt, from, to, authorityGrantId, authorityVersionId, actorDistrictLimit, binderDistrictLimit and the complete assessed intervals. DecisionHash is SHA-256 of the exact UTF-8 JSON. It is immutable and inserted inside the existing issue transaction before finance/documents/receipt completion. The selected grant is the same authority retained in the policy snapshot.

Commercial successful issue receipts and first-policy readbacks add `commercialExposureDecisionId`. Receipt references use `PL-CC-` and ten digits. Documents contain schedule/statement plus certificate iff employers' liability is selected. `commercial-combined-issued.schema.json` defines the closed source snapshot with `snapshotFormat: issued-commercial-1`; all money strings retain exact decimal semantics.

A capacity 409 carries `commercialCapacity` with format `commercial-issue-capacity-1`, bookId, observedAt, authorityLimit, intervals (at most100), and truncated. Each offending interval contains district, startsAt, endsAt, ownSumInsured, otherSumInsured, resultingSumInsured, policyCount and code; a selected publication adds publishedLimit, effectiveLimit (minimum published/staff/binder), headroom, limitVersionId and limitHash. Money is a fixed two-place string. No foreign policy IDs are returned. The issue route's existing internal capability and current parent scope remain mandatory. Advisory exposure routes are implemented in08-10.

## Verification boundaries

Pure golden tests cover scheduled/current/expired/cancelled winners, early renewal, future and backdated adjustments, same-time ordering, same-district distinct-policy totals, moves, zero property, complete overlays, limit replacement/expiry/ambiguity and malformed inputs. An independent integration-project test compares the exposure algorithm with PolicyTemporalSelector at every effective/known boundary and adjacent tick.

Real SQL tests use accepted commercial source provenance, prove source/child/hash/timeline rejection, atomic rollback, append-only history, dated publication readback and stable successor-binder book membership. Retained Motor Trade storage and upgrade tests verify unchanged source bytes and credentials. Actual Commercial Combined cancellation issuance remains the later cancellation plan's responsibility; this slice tests cancellation selection and rejection of a forged cancellation header, without bypassing existing policy source constraints.
