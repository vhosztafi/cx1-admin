# 05-07 progress — 2026-09-16

Status: in progress, not accepted.035be54 adds pure input rules; f480e6e adds lookup storage and its migration. No lookup endpoint, provider, worker or UI is enabled.

`QuoteLookupRules` derives allowlisted address/vehicle/licence queries from actual saved insured/driver/premises/vehicle targets. Child identity must occur exactly once in the correct collection; insured targets reject child IDs. Bounded ASCII inputs normalize for queries without rewriting declarations. A canonical whole-subject fingerprint includes the trusted capture pins and target identity. Selection can detect any target edit or changed pin; unrelated subject edits do not change the fingerprint. Manual recovery requires a bounded meaningful reason. Six explicit demo scenario names are allowlisted, with no URL/provider escape hatch.

All549backend unit tests pass, including10newcases,0skips: `.local/phase5-lookup-rules-20260916`, corresponding.log. No SQL/API/browser or fresh full backend acceptance is claimed for this unconnected kernel. Last full application acceptance remains05-06's633/68SQL. No owned previews/tests remain. Sales snapshot unchanged.

## Next implementation

1. Storage prerequisite complete in f480e6e: QuoteLookup/QuoteLookupSelection records, ownership constraints and migration. Store private query/outcome data in quote-owned storage; outbox payloads and generic attempts should contain only opaque IDs. Pin scenario/reference versions and source revision/item fingerprints.
2. Build the scoped command service using QuoteScope and ExecuteAuthorizedAsync. Current held authority precedes replay. Request deduplication is distinct from command receipts. Selection must recheck source revision, input fingerprint and quote ETag, then append a revision and provenance atomically. Extract the existing QuoteService append helper carefully rather than calling SaveAsync in a nested transaction. Manual decisions must be persisted and never inferred from declarations.
3. Extend platform leases for a dedicated lookup kind, durable deterministic provider outcomes, private result application and restart/race fencing. Diagnostic result routes must never expose lookup data. Existing SqlJobLeases currently permits only diagnostic-probe and agency-notification, and Superseded currently maps to invitation-superseded; do not blindly reuse that code for quote semantics.
4. Implement the closed HTTP contracts, real SQL/API tests and actual lookup/manual controls. Then fresh full backend results plus UI/build/Chrome checks per05-07-PLAN before completion. No QUO signoff before05-11; overall readiness remains closed.

Potential refinements must stay explicit: licence number is `risk.drivers[].licence.number`; address lookup targets insured/driver/premises. The pure rules are not authorization and must only run after service scope checks. Whole-subject fingerprints are intentionally conservative and can invalidate a lookup after an unrelated field within that same subject changes.


## Verified lookup storage prerequisite

Added QuoteLookup and QuoteLookupSelection models and migration20260916104914_QuoteLookupStorage. Query/results are quote-owned; the work record holds opaque identity. Composite keys bind lookup to source quote/revision and selection to lookup/source fingerprint/new revision in the same quote. Unique request hashes and one selection per lookup provide storage prerequisites for command deduplication. SQL checks bind target shape, bounded JSON outcomes and exclusive candidate/manual decisions. Triggers prevent input deletion/change, terminal outcome replacement and selection history mutation; source JSON membership and work/scenario ownership are checked on insert. Candidate decisions require a real ID in a succeeded stored outcome, and target revisions must be later than the source.

Focused real-SQL storage test passed in `.local/phase5-lookup-storage-focused-20260916`; final fresh full suite passed in `.local/phase5-lookup-storage-final-20260916` and matching.log: **644=549unit+95integration,69realSQL,0skips**. Result assertion644/69passed; integration6.6742minutes. The process completed; no active test/preview remains. EF model/snapshot check passed in `.local/phase5-lookup-model-check.log`. This migration has only been applied to isolated test databases, not CoverMGA_Demo. Runtime service/worker/API/UI remain absent; full readiness remains closed.


The final SQL test also rejects replacement of a completed lookup outcome. It verifies target membership, same-quote new revisions, exact source fingerprint, fabricated pending candidate selections, duplicate selections and immutable input/decision history. The initial EF command used the API as startup and reported its missing Design package; corrected to the existing Infrastructure design-time factory, without adding a dependency. No demo database reset or migration, endpoint activation, browser claim or final QUO acceptance. Next: scoped command service and private durable provider/worker integration, followed by HTTP/UI and their race/restart/security/browser tests.
