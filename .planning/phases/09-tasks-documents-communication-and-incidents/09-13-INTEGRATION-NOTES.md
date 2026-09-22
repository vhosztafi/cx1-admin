# 09-13 integration notes from incident implementation

09-12 is complete and accepted.09-13 is now the active integration slice.

## Actual producer

- `IncidentService` is a partial Infrastructure service. `OperationalIncident` has
  PolicyId/ProductCode/Reference, CurrentRevisionId/CurrentResolutionId, State and
  rowversion. `IncidentRevision` stores exact closed draft JSON, SHA256, number,
  author and reason. `IncidentOccurrenceRecord` maps to IncidentOccurrenceResolution,
  storing original incident/revision, server KnownAt, state, JSON/hash. Normalized
  `IncidentResolutionSource` rows retain owned policy version/hash and time intervals.
  `IncidentEvidence` binds exact ready original-policy document versions to revisions.
- `IncidentRules.Draft/Missing` validate the closed draft and readiness. Subject facts
  may be incomplete; supplied IDs are validated against original owned policy history.
  The occurrence resolver validates the selected retained source before declaring
  readiness. An option selected for viewing never resolves ambiguous occurrence.
- `Resolve(actor,id,etag,log,key,token)` persists an immutable resolution. With log=true
  it requires complete facts and returns state logged, explicitly unsent. It creates
  no outbox/provider record. Description and occurrence clarification append revisions
  and reset current resolution; old resolutions remain queryable through scoped history.
- Current `Editable` blocks queued/handed-off.09-13 must refine failed-state correction:
  only a definite provider rejection permits corrected resubmission. Unknown or
  timed-out-after-success work must keep its exact operation and revision until resolved.
  Do not let generic failed propagation inadvertently authorize editing uncertainty.

## Claims consumer

Pin complete current revision and current resolved resolution under original authority
and head lock. Match policy/revision/source hashes and historical intervals; do not use
the policy's latest pointer. The posted handoff IDs are claims about selection, not
authority. Revalidate current identity/original scope before replay and provider effect.
Resolve and log-and-handoff must be one application transaction when integrated.

Mirror durable delivery's separation of provider effect and application transaction,
lease fencing, operation/scenario/request hashes and changed-result quarantine. Inspect
`MessageDeliveryWorker`, `DeliveryAuthority`, `SqlJobLeases` and `DemoProviderOperation`
signatures before implementing. Terminal exception needs one workflow task. No real
provider transport is authorised.

Evidence can include internal documents while a report is draft. Explicitly decide
and enforce which exact original versions can be disclosed in the administrator
projection; neither internal notes nor storage keys may leak. Preserve ready bytes and
current access checks at provider execution. Safe link metadata must retain exact version.

Commercial payload compatibility: call `CommercialIncidentPayload.CreateResolved`
for `commercial-incident-2`; retain legacy Create/Valid for version1. Its source bounds
must be the actual policy version validity bounds, not the exact occurrence point's
zero-length applicability interval. Never invent an observed time from a date or hint.
New claims adapter schema must accommodate the factual occurrence/resolution format.
Current generator `scripts/generate-adapter-contracts.mjs` still references legacy
`IncidentWrite` for claims requests and requires non-null money in claims results.
Version the new request shape explicitly and permit unknown money as null without
invalidating retained legacy envelopes. Do not silently replace the old request type.

Append administrator summaries under exact handoff/provider event/hash, with independent
asOf/receivedAt chronology. Unknown paid/reserved remain null. Contact and refresh use
current original scope; a later policy adjustment cannot retarget old incident work.

## UI / verification

`IncidentEditor` currently labels logged-unsent honestly. Extend with explicit
handoff/administrator summary actions through the final contracts. Frozen local command
includes original actor ID, body, key and ETag; uncertainty keeps it across retries and
blocks navigation. Do not recreate a key after an unknown result or reuse a rejected
handoff key for corrected input. Existing draft controls belong to09-12 and remain.

`OperationalIncidentBrowserTests` owns separate MT/CC databases, dynamic API/web ports
and actual SQL readbacks. Commercial fixture uses a real noon adjustment via optional
`localTime` and `inspectIssued` hooks in the existing servicing test scenario. The
worker routes requests to the owned dynamic API, because Next build rewrites retain
the build-time origin. Keep SQL acceptance sequential and do not modify unrelated
preview processes or retained CoverMGA_Demo. Phase9 demo migration remains09-17/18.
