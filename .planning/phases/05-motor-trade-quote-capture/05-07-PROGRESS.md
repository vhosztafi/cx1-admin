# 05-07 progress — 2026-09-16

Status: in progress, not accepted. Implementation035be54 introduces pure application rules only; no lookup endpoint, provider, worker, persistence or UI is enabled.

`QuoteLookupRules` derives allowlisted address/vehicle/licence queries from actual saved insured/driver/premises/vehicle targets. Child identity must occur exactly once in the correct collection; insured targets reject child IDs. Bounded ASCII inputs normalize for queries without rewriting declarations. A canonical whole-subject fingerprint includes the trusted capture pins and target identity. Selection can detect any target edit or changed pin; unrelated subject edits do not change the fingerprint. Manual recovery requires a bounded meaningful reason. Six explicit demo scenario names are allowlisted, with no URL/provider escape hatch.

All549backend unit tests pass, including10newcases,0skips: `.local/phase5-lookup-rules-20260916`, corresponding.log. No SQL/API/browser or fresh full backend acceptance is claimed for this unconnected kernel. Last full application acceptance remains05-06's633/68SQL. No owned previews/tests remain. Sales snapshot unchanged.

## Next implementation

1. Add QuoteLookup/QuoteLookupSelection records, ownership constraints and migration. Store private query/outcome data in quote-owned storage; outbox payloads and generic attempts should contain only opaque IDs. Pin scenario/reference versions and source revision/item fingerprints.
2. Build the scoped command service using QuoteScope and ExecuteAuthorizedAsync. Current held authority precedes replay. Request deduplication is distinct from command receipts. Selection must recheck source revision, input fingerprint and quote ETag, then append a revision and provenance atomically. Extract the existing QuoteService append helper carefully rather than calling SaveAsync in a nested transaction. Manual decisions must be persisted and never inferred from declarations.
3. Extend platform leases for a dedicated lookup kind, durable deterministic provider outcomes, private result application and restart/race fencing. Diagnostic result routes must never expose lookup data. Existing SqlJobLeases currently permits only diagnostic-probe and agency-notification, and Superseded currently maps to invitation-superseded; do not blindly reuse that code for quote semantics.
4. Implement the closed HTTP contracts, real SQL/API tests and actual lookup/manual controls. Then fresh full backend results plus UI/build/Chrome checks per05-07-PLAN before completion. No QUO signoff before05-11; overall readiness remains closed.

Potential refinements must stay explicit: licence number is `risk.drivers[].licence.number`; address lookup targets insured/driver/premises. The pure rules are not authorization and must only run after service scope checks. Whole-subject fingerprints are intentionally conservative and can invalidate a lookup after an unrelated field within that same subject changes.
