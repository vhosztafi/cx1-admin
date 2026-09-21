# Phase9 pattern map

Inspected2026-09-21. Proposed Operations files are new outputs, not assertions that symbols already exist. Follow current source at implementation; do not copy product-specific stale-state rules without adapting their ownership.

| New responsibility | Existing analog | Required adaptation |
|---|---|---|
| OperationalScope/subject registry | Infrastructure/Policies/PolicyScope.cs; Quotes/QuoteScope.cs; Parties/PartyScope.cs; Agencies/AgencyScope.cs | Resolve one typed parent and revalidate current relationship/identity, under held transaction. Client subjects must be relationship-specific. Registry never grants permission. |
| Task/file/message/incident mutations | Infrastructure/Platform/SqlCommandBoundary.cs | Use ExecuteAuthorizedAsync, authorization before receipt lookup, handler ETag validation after replay, no provider/file-system side effects inside SQL handler. |
| Persistence/guards | Persistence/AgencyFollowUp.cs; ServicingEvidenceRecords.cs and matching model/migrations | StoredRecord versus MutableRecord, typed FKs, NO ACTION deletes, unique source-event/version sequence and append-only guards. New migration plus snapshot, actual SQL verification required. |
| API endpoint modules | Api/PolicyEndpoints.cs; QuoteEndpoints shared error/ID handling | Require capability, closed DTO/query parsing, CSRF on mutations, typed safe Problem response, binary download headers. Wire Program/DI and generated OpenAPI. |
| Cursor lists | Api/PolicyDiscoveryEndpoints.cs and PartyPaging | Scope-bound query/cursor, stable ID tie-breaker, actual creator filtering, no unscoped counts. |
| Durable delivery/provider | Infrastructure/Policies/ServicingDeliveryWorker.cs; Platform/SqlJobLeases.cs | Separate persistent provider effect and result application; exact operation/hash/scenario/lease; current recipient authority; message/document versions instead of servicing terms current-cycle identity. |
| Existing files bridge | Persistence/AgencyEvidenceRecords.cs; QuoteEvidenceRecords.cs; ServicingEvidenceRecords.cs | Retain SQL byte arrays and original scope. New local storage abstraction does not migrate/delete them. |
| Document request/source | Policies/CommercialDocumentRequestPayload.cs; Application/Policies/CommercialDocumentPayload.cs; Persistence/PolicyRecords.cs | Consume exact pinned source/template, persist new immutable actual bytes, no mutation of old request envelopes. |
| Incident temporal resolver | Application/Policies/CommercialIncidentPayload.cs and existing temporal selection | Preserve date/approximate precision; exact-instant helper alone is insufficient; pin knowledge cutoff and resolution. |
| UI recovery | components/underwriting/decision-command.tsx; lib/quotes.ts | Fixed request body/key on uncertain retry, actor recheck, stale input retained, focus restored; no fake completed response on click. |
| Shared view layout | components/primitives.tsx; record-header.tsx; app/globals.css | Preserve source density/palette; scoped operational tabs and actual records rather than fixed demo references. |

Concrete anchors:

```csharp
// SqlCommandBoundary: authorization is part of the same transaction before replay.
if (authorize is not null) await authorize(db,cancellationToken);
var existing=await db.Set<IdempotencyRecord>().AsNoTracking().SingleOrDefaultAsync(x => x.ActorScope==actorScope && x.Route==identity.Route && x.Key==identity.Key,cancellationToken);
```

```csharp
// PolicyScope: parent identity graph is checked, not merely a policy GUID.
if (policy.SourceQuoteId != owned.Quote.Id || policy.AgencyId != owned.Quote.AgencyId || policy.ClientId != owned.Quote.ClientId || policy.RelationshipId != owned.Quote.RelationshipId || owned.Quote.BoundPolicyId != policy.Id)
    throw new QuoteOperationException(404, "policy-not-found");
```

```csharp
// AgencyFollowUp: existing event provenance already has stable uniqueness.
row.HasIndex(x=>new{x.AgencyId,x.EvidenceId,x.Purpose,x.DueOn}).IsUnique().HasFilter("[EvidenceId] IS NOT NULL");
```

```tsx
// DecisionCommand: distinguish attempted uncertain requests from local failures.
const retain = recovering || attempted && uncertainQuoteFailure(failure);
```

Paths above are relative to backend/src unless an apps/backoffice component/lib is named. Record exact final public signatures and migrations in OPERATIONS-CONTRACTS and each summary. Existing retired prototype/API stubs do not prove a runtime route exists.
