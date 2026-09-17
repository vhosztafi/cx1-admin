# Phase 7 handoff — issued policy boundary

Phase 6 implements Motor Trade new business from rating through exact acceptance
and atomic first issue. Consume 06-VERIFICATION and 06-14-SUMMARY before treating
final acceptance as complete. The sales funnel remains reference-only.

## Preserve the existing graph

Policy holds trusted client/agency/relationship/product ownership and a current
term pointer. PolicyTerm retains London local intent plus resolved UTC start/end,
product version and current version. PolicyTransaction records effective and
processed instants separately, actor/reason/operation identity and exact source
quote/revision/cycle/rating/acceptance. PolicyVersion retains immutable snapshot
bytes/hash and transaction/term/policy ownership. Composite FKs enforce same-owner
pointers; unique keys enforce one first issue per quote and one new-business
transaction per policy. PolicyRegistration is keyed by (VersionId, RiskItemId),
not an Id column, and indexes the actual immutable version's registrations.

The current snapshot is the closed `issued-quote-1` contract in
`contracts/schemas/issued-policy.schema.json`. It preserves original capture JSON
and the accepted resolved sections, endorsements, warranties, money and provenance.
Do not reinterpret old bytes through the separate future-servicing policy schema.
Versioned readers must continue to support this first-issue shape. Do not rebuild
historical snapshots from the current quote, current terms or current configuration.

## Extend constraints deliberately

Phase 6 SQL permits only new-business transactions and requested first-issue
documents. Phase 7 requires an additive migration for new transaction kinds,
servicing provenance and chronology; later Phase 9 requires document outcome
states. Never disable the owner/immutability/posting guards to make new commands
work. Retain migrations and prove upgrades preserve old snapshot and journal hashes.
New servicing drafts, editing leases, renewal/cancellation and effective-date
reconstruction are not yet implemented. Their existing design-only endpoints must
remain closed until their services, persistence and tests exist.

Use explicit effective and processed chronology for as-at policy reads. Existing
first-issue child endpoints validate every policy/term/version/transaction or
obligation ID; their current uniform first-issue view is not a complete historical
reconstruction algorithm. Current discovery joins owned current term/version and
uses half-open coverage dates. Broaden its status rules only with the new lifecycle.

## Authority and atomic effects

Current stored identity, role, agency, relationship and applicable underwriting
grant are checked under the held scope before successful receipt replay. A stale
ETag may recover an already committed identical command only after current access
is reauthorized. Fresh commands must match exact current versions and hashes.
Maintain established deterministic lock ordering and the agency/quote/cycle fence.
Servicing authority is not implied by having read access or a senior assignment.

Capacity withdrawal/reopening keeps monotonic submission/response pointers and all
messages, but draft state removes their authority. Late worker results cannot
reactivate a withdrawn request. New submissions require current responses and
invalidate prior acceptance. Conditions require current-purpose reviewed evidence
and actual resolution; previous similar decisions confer no authority.

Each issue commits its policy graph, IssueFinancialObligation/components, sealed
balanced Journal/JournalLine, three PolicyDocumentRequests, durable `policy-document`
outbox work, audit/activity and bound quote link in one transaction. There is no
collected cash, generated document or live send. Phase 7 financial deltas must use
the same penny-exact posting boundary; reversals/refunds create new obligations,
not edits to sealed lines. Phase 9 consumes the retained request/template/payload
hash/version/work identities; Phase 10 owns collection, allocation and statements.

## Discovery, sharing and regression

Internal policy discovery and client Policies/Activity open actual policy records.
Quote/policy discovery cursors bind current actor/filter/order/as-of and relevant
record version/count fingerprints. Session/job maintenance must not invalidate an
otherwise stable list; material record changes must. Own-agency sharing exposes
only id/reference/clientName/productCode/state/startsAt/endsAt. Never add hidden
risk, registration search/counts, evidence or finance to that projection. Generic
shared tasks remain Phase 9, Commercial Combined Phase 8 and global search Phase 12.

Retain both-product issue/retry/rollback/authority tests, all 37 prior browser
journeys and the carrier-conditions issue journey. Compare policy graphs and exact
history across owned-process restart with fresh login. Preservation evidence must
contain validated named hashes, never equal error output. Human business and
assistive-technology UAT, hosted CI and Docker remain separate unperformed checks.
