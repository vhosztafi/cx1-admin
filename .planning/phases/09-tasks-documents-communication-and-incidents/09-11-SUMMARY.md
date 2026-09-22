---
phase: 09-tasks-documents-communication-and-incidents
plan: '11'
status: complete
completed: 2026-09-22
requirements-completed: []
---

# Historical incident occurrence resolution

The occurrence producer now resolves factual date/time precision against owned,
retained policy history at a bounded knowledge cutoff. It distinguishes a single
applicable source, multiple versions, partial cover, no cover and incomplete facts.
Incident revision persistence and UI consume this producer in09-12.

## Delivered

- IncidentOccurrenceRules preserves a full London date/approximate interval, including
 23/25hour DST days. Exact instants must agree with the local date. ExactLocal rejects
 nonexistent times and requires a valid offset for repeated times. Approximate hints
 serialize as HH:mm, not a fabricated exact observed instant or tolerance window.
- IncidentOccurrenceResolver.Resolve/ResolveHeld holds original typed policy and
 current internal actor authority, reuses PolicyTemporalSelector and splits at real
 retained term/version boundaries. Actual stored JSON SHA256 is checked. Future
 knowledge is rejected; potentially future occurrence intervals remain incomplete.
 No mutable current version pointer supplies historical cover.
- IncidentSubjectRules validates closed MT/CC branches and supplied retained vehicle,
 driver, property location, selected cover and EL wage identities. Missing selections
 may remain incomplete; foreign/incompatible selections fail. Subject validation
 after a single temporal source resolves cannot turn ambiguous history into cover.
- CommercialIncidentPayload.CreateResolved emits commercial-incident-2 preserving
 observed occurrence, applicability window, knownAt and exact source identity/hash.
 Existing exact-only Create/Valid and commercial-incident-1 remain compatible.
- Internal incident capabilities and DI are registered without broadening policy
 access. The resolution schema carries candidate source hashes and an optional
 factual window; OpenAPI/TypeScript regenerated. Final routes/revision IDs belong12.

## Evidence

.local/phase9-11-final-strict verifies42 unique passing cases/2realSQL/no skips from
unit-format-green34 and temporal-sql8. The latter contains6 deterministic temporal
composition checks and2 actual SQL scenarios, not8 SQL scenarios.

Coverage includes full-day/approximate DST length, gap/overlap offsets, exact date
agreement, midday adjustment/cancellation, backdated processing knowledge, expiry,
future/missing facts, real MT/CC issued source IDs/hashes, original actor/parent
scope, foreign driver/location, incompatible products, unselected cover and EL wage
identity. Existing commercial payload tests pass unchanged. Initial6RED→6GREEN,
new commercial payload1RED→GREEN, and HH:mm mismatch1RED→GREEN are retained.

## Consumer contract and limits

ResolveHeld requires an open command transaction and returns a value.09-12 owns the
immutable incident revision and persisted resolution IDs/binding;09-13 owns provider
handoff and must validate complete facts/current authority/same resolution. A source
candidate or resolved applicability is not a claim coverage decision. Historical
ambiguity blocks handoff but does not prevent draft factual capture. No incident UI,
provider handoff, humanUAT, retained demo migration or new DB table is claimed here.
No frontend-code or retained keys changed. Next:09-12 saved incident forms/revisions.
