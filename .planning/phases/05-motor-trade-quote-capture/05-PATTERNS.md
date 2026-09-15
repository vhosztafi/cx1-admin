# Phase 5 closest implementation patterns

All new paths/symbols below are planned, not already implemented. Read current files before using them; do not infer signatures from this table.

| New responsibility | Existing code to reuse / constraint |
|---|---|
| Quote command service and receipts | Infrastructure/Platform/SqlCommandBoundary.cs: ExecuteAuthorizedAsync, authority before receipt, same-key replay and atomic audit. Application/ActorContext.cs: add explicit internal quote capabilities without broadening agency identity. |
| Held quote identity | Infrastructure/Agencies/AgencyScope.cs, AgencyUserAuthority.cs and Identity/IdentitySnapshot.cs: current stored scope and agency-first locks. MatchService.cs supplies agency/intake/review/client ordering to extend safely. |
| Versioned quote storage | Infrastructure/Persistence/AgencyTermsModel.cs and agency terms migrations: immutable history, composite ownership, exact decimals. New quote revision schema differs; do not reuse agency terms rows for risk JSON. |
| DTO/validation | Application/Agencies rules and scripts/generate-policy-contracts.mjs: strict input contracts, omitted answers, exact source options, semantic rules beyond JSON syntax. Policy draft schema is the starting point, not a full quote authority boundary. |
| Queries/cursors | Api/PartyPaging.cs, ClientEndpoints.cs, Infrastructure/Agencies/AgencySharingPaging.cs: current scope before counts, bounded queries and fingerprinted cursor. Add quote registration search without join duplication. |
| Demo lookups | Platform/SqlJobLeases and agency notification worker/receipts/attempts: durable states and idempotent outcome application. Keep quote result authorization distinct from admin diagnostics. |
| Evidence | Infrastructure/Agencies/AgencyEvidence services/models: bounded file bytes, hash/provenance, input fingerprints and safe downloads. Scope files to quote and revision/item. |
| Frontend capture | apps/backoffice/components/agencies wizard/evidence/users/terms components: source layout, busy refs, exact pending command and stale recovery. Shared client resource/paging and server-actor internal-page guard. |
| Discovery and source links | components/clients/client-detail.tsx and match-review.tsx: replace only actual quote unavailable projection; keep policies unavailable. Agency sharing/context has one common safe DTO boundary. |
| Tests | backend/tests/BackOffice.IntegrationTests/AgencyExternalApiTests.cs, AgencyActivationDecisionTests.cs, AgencyActionProjectionTests.cs: real accepted cookies, SQL races, rollback and authorized projections. scripts/verify-agency-suite.mjs: no-reset evidence labels and retained regressions. |

The snapshot frontend-code has no callable transport contract and is never imported as application code. Its domain rules and canonical question sources are read-only evidence. No new dependency is required unless05-01 proves an existing serializer/schema mechanism insufficient; document any addition before installing it.
