# Architecture direction and Phase 1 design brief

Status: researched proposal, not an implemented contract. Phase 1 must settle column definitions, JSON schemas and OpenAPI before implementation. Scope decisions are in PROJECT.md and defaults in ASSUMPTIONS.md.

## Solution boundaries

```text
apps/backoffice/                  Next.js, TypeScript, Tailwind UI
backend/src/BackOffice.Api/       HTTP, authentication, OpenAPI, composition
backend/src/BackOffice.Application/ use cases, authorisation policies, ports
backend/src/BackOffice.Domain/    value types, lifecycle and calculation rules
backend/src/BackOffice.Infrastructure/ SQL Server/EF Core, files, demo adapters
backend/tests/                   unit and SQL Server/API integration suites
contracts/                       OpenAPI, JSON schemas, examples, generated TS client
docs/                            design, setup, demo and operations documentation
```

Use feature folders for Parties, Distribution, Quoting, Underwriting, Policies, Operations, Finance and Administration. Domain does not reference infrastructure or web frameworks. UI calls public API contracts, never reads the database directly. One API deployment initially; a hosted worker drains durable work with leases and retry rules. Extract separate services only if later requirements justify the operational cost.

## Relational ownership proposal

| Area | Main records | Required relationships/invariants |
|---|---|---|
| Identity | User, ExternalIdentity, Role, Team, Session, ApprovalRequest | Stable internal user ID independent of future Entra subject; internal/agency roles separated |
| Parties | ClientAccount, Contact, Address, SupportFlag, MatchReview | Flags belong to people; a single primary contact per account; recorded match decisions |
| Distribution | Agency, AgencyContact, AgencyUser, AgencyProduct, Agreement, Invitation | Agency access and commission terms effective-dated; activation prerequisites |
| Configuration | ProductVersion, SchemeVersion, CapacityProvider, AuthorityRuleVersion, TemplateVersion | Issued records pin relevant configuration versions |
| Quoting | Quote, QuoteRevision, RatingResult, Referral, ReferralDecision, Acceptance | Rating and acceptance tied to exact revision and terms |
| Policies | Policy, PolicyTerm, PolicyDraft, PolicyTransaction, PolicyVersion | Terms belong to policy; versions belong to term and issuing transaction; issued content immutable |
| Operations | Task, Note, Thread, Message, Document, Incident, ClaimsSummary | Explicit linked-record relationships and visibility; document points to source/template versions |
| Finance | FinancialEntry, Invoice, Receipt, Allocation, Refund, Reconciliation, BordereauBatch/Row, Period | Decimal amounts; append-only posted movements; unique operation keys and allocation constraints |
| Platform | AuditEvent, OutboxWork, AdapterAttempt, IdempotencyRecord, EditLease | Audit and queued effects commit with business change; no duplicate business effect |

Use foreign keys and constraints for authoritative relationships. Avoid an unconstrained generic entity-type/entity-id link table as the sole integrity mechanism; Phase 1 selects typed link tables for cross-record tasks/documents.

## Policy JSON proposal

Keep identity/status/search keys, term dates, issuing transaction, version sequence and concurrency token in relational columns. JSON stores the exact contractual risk and coverage snapshot.

```json
{
  "schemaVersion": "1.0",
  "productCode": "motor-trade-combined",
  "productVersionId": "<uuid>",
  "insured": { "clientId": "<uuid>", "legalName": "Example Motor Traders Ltd" },
  "risk": {
    "activities": [],
    "premises": [],
    "drivers": [],
    "vehicles": [],
    "tradePlates": [],
    "declarations": {},
    "previousInsurance": {},
    "lossHistory": []
  },
  "cover": { "sections": [], "endorsements": [], "warranties": [] },
  "premium": { "currency": "GBP", "components": [], "totalPayable": "0.00" },
  "provenance": { "source": "backoffice", "ratingResultId": "<uuid>" }
}
```

This illustrates the envelope only and is not a valid completed policy fixture. Each product has a discriminated schema with required fields. CC replaces Motor Trade risk sections with locations, protections, liabilities, wageRolls and businessInterruption. Every repeated risk object gets a stable ID for amendments and comparisons. Support flags, internal notes, password/session material and private referral commentary do not enter the policy risk snapshot.

Store issued JSON as original bytes/text with a content hash where useful for evidence. Schema changes introduce new versions and readers/mappers; never rewrite historical issued content in place. Store labels and relevant reference/configuration versions so later option edits do not silently change a historical schedule. Search projections for registrations and other array items are relational, keyed by policy/version and rebuilt/tested against snapshots.

## Version and time semantics

Distinguish Policy (continuity), PolicyTerm (coverage interval), Transaction (business event), Version (issued snapshot) and Draft (proposed change). Processing timestamps are UTC instants. Effective dates/times use explicit policy-local interpretation. Define term interval inclusivity, midnight boundaries, leap years and daylight-saving tests in Phase 1.

Effective-date queries consider only issued changes that apply at the requested contractual date. Processing-date queries show the issued knowledge at that timestamp. Requests combining both axes need an explicit contract. Drafts are excluded. Backdated processing must not overwrite the record of when a change was actually issued. Future-effective changes require selection of the applicable version, not simply the largest version number.

Initial rule: reject adjustments earlier than the latest effective issued change, with a clear unsupported-out-of-sequence response. Within the supported ordering, backdating requires authority and a reason. Phase 1 validates this against the prototype and documents examples; no implicit rebasing is allowed.

## Commands and concurrency

Suggested API surface under `/api/v1`:

| Resource | Commands / reads |
|---|---|
| clients, agencies | Create, amend, search, contacts, flags, onboarding, invitations, sharing preview |
| quotes | Create, save revision, rate, submit, send, accept, withdraw, clone, bind |
| referrals | Assign, query, decide, escalate, record capacity response |
| policies | Read term/version, as-at query, comparison, linked records |
| policy-drafts | Create MTA/renewal/cancellation draft, edit, lease/takeover, rate, accept, issue, abandon |
| tasks, messages, documents, incidents | Create/amend/workflow commands, upload/download, send, handoff/status |
| finance | Post receipt, allocate/unallocate, reconcile, approve refund, execute demo payment, close period |
| bordereaux | Create batch, validate, correct/exclude, export, submit, status |
| admin, reports, search | Effective-dated configuration, approvals, audit, filtered projections and exports |

Final paths and DTOs belong in Phase 1 OpenAPI. Do not expose arbitrary `PATCH status=Issued` operations. Use `If-Match` with rowversion-derived opaque ETags on mutations; define 428 missing precondition, 412 stale version, 409 domain conflict, and a consistent validation Problem Details response. All commands enforce actor role, agency scope and field visibility on the server.

Issue/payment/submission commands require scoped idempotency keys. Store request hash and resulting record IDs in the same commit; the same key plus different input is rejected. Concurrent first requests need a unique constraint and tested recovery. Avoid irreversible financial actions inside automatic retries without a business operation key.

## Atomic issue and external work

1. Re-read and check draft revision, base version, dates, authority, evidence, current rating and matching acceptance.
2. Within a database transaction, reserve/check policy aggregate concurrency and idempotency, append policy transaction/version, update projections and add balanced financial obligations.
3. Write audit and durable requests for document creation, delivery and reporting in the same commit.
4. After commit, workers execute deterministic adapters. Record each attempt, retry timing, final outcome and linked exception task. Retrying a worker cannot issue a second policy or pay a refund twice.

When an external step fails, the issued contract remains issued and shows outstanding operational work. An issue database failure rolls back its business records together. The API returns actual domain state, never a success toast detached from storage.

## Test and demo design

Use unit tests for lifecycle transitions, authority dimensions, stale-rating invalidation, dates, premium/rounding and allocation constraints. Use real SQL Server integration tests for uniqueness, optimistic conflicts, rollback, projection updates and idempotency. Use component tests for capture validation and browser scenarios for complete business journeys and permissions.

Seed at least: multiple agencies and each internal role; both MT products and CC; draft/sent/referred/bound/withdrawn quotes; active/future/renewing/cancelled policies; several versions; the five-change MTA with missing licence and stock-authority referral; blocked and approved refunds; a duplicate match; support flags; document history; unmatched receipt; invalid bordereau; failed MID/claims/delivery jobs. Use a fixed demo clock configurable for deterministic runs. All financial totals must derive from actual fixture movements.

## Phase 1 exit evidence

Produce concrete schema files and valid/invalid fixtures, the ERD/data dictionary, full OpenAPI with error cases, state/permission matrices and a prototype control inventory. Walk one quote issue, two concurrent edits, a backdated MTA, renewal, cancellation/refund and CC location change through all affected records. Record rounding and temporal expected results. No implementation starts from this high-level brief alone.
