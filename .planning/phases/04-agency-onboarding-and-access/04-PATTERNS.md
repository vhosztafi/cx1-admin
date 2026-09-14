# Phase 4 implementation patterns

| Responsibility | Existing analog | Required adaptation |
|---|---|---|
| Strict optional draft DTOs | Application/Parties/ClientIdentity.cs; openapi-form-contracts.mjs | Preserve all source modes; optional fields allow incomplete save, activation has separate complete validation |
| Aggregate storage and migrations | PartyRecords/PartyModel; MatchModel and SupportFlagModel | Extend same Agency IDs; split AgencyRecords/AgencyModel files; immutable evidence/terms/proposals plus mutable lifecycle tokens |
| Audited commands and overlap fences | SqlCommandBoundary; ContactService/SupportFlagService parent locks | Single agency-first lock protocol; full failure rollback; authorization before receipt lookup |
| Read projections/paging | PartyScope/PartyPaging; ClientEndpoints/MatchEndpoints | Dedicated AgencyScope and safe broker DTOs; current agency/role/grant fingerprint in cursors; filter before counts |
| Identity and invitation | LocalIdentityService/SqlTicketStore; PasswordHasher/UserCredential | Stored AgencyId and role scope validation on sign-in/ticket retrieval; one-time token consumption, no secret receipts |
| Durable demo notifications/checks | Platform job leases, diagnostic provider/dispatcher and records | New agency-specific kinds and protected payloads; preserve operational fencing/recovery guarantees; no external calls |
| List/detail/wizard | clients/matches routes, RecordHeader, Panel/DataTable/Paging, retained form receipts | Add /agents, /agents/new, /agents/[agencyId], /agents/[agencyId]/onboarding, /agents/[agencyId]/sharing; source six-step wizard and314px rail |
| Dialogs | workspace navigation native dialog; prototype aguser/invite | Reusable labelled native dialog with initial focus/Escape/return focus; freeze closure while command outcome uncertain |
| Tests/seeds | Client/SupportFlag/Match real SQL fixtures and repeatable demo seeds | New isolated Agency tests, preserved existing demo identities, explicit second reviewer and deterministic notifications; no reset |

Do not expand the unrelated admin-workspace component or rewrite Phase3 services wholesale. New files belong under Application/Agencies, Infrastructure/Agencies, API agency endpoint modules and components/agencies. Extend existing identity/persistence/platform seams only where the new invariants require it. Sequential execution avoids shared-file conflicts.
