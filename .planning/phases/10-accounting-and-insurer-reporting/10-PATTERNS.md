# Phase 10 implementation patterns

Read these concrete analogs before the relevant plan. New finance file names are provisional until the contract plan resolves signatures; do not copy business semantics from an unrelated aggregate.

| New responsibility | Closest existing analog | Preserve / adapt |
| --- | --- | --- |
| Financial persistence, precision, FK and guards | `backend/src/BackOffice.Infrastructure/Persistence/IssueFinancialRecords.cs`, `IssueFinancialModel.cs`, `Migrations/ServicingPostingJournalGuards.cs` | Existing source-component journal remains immutable; add cash records and constraints separately; use decimal(15,2), no action FKs, sealed-row guards. |
| Posting/period lock | `backend/src/BackOffice.Infrastructure/Policies/AccountingPeriods.cs`; `PolicyIssueWriter.cs`; servicing/cancellation posting services | Existing transaction-held London date selection; acquire the same period lock before new posting and close. Preserve first-issue legacy projection. |
| Idempotent commands and current scope | `backend/src/BackOffice.Api/ServicingIssueEndpoints.cs`, `CancellationOperationsEndpoints.cs`; `docs/design/API-CONVENTIONS.md` | Current actor/record authority before replay, strong ETag and command hash, audit plus outbox within one SQL transaction. |
| Persistent adapter with recovery | `backend/src/BackOffice.Api/ClaimsDispatcher.cs`, `MidDispatcher.cs`; corresponding infrastructure receipt/attempt records | Operation identity pinned; provider outcome independent of local application; changed duplicate quarantined and uncertain retry recovers. No real network transfer. |
| Versioned artifact and download | `DocumentEndpoints.cs`, document/file records and `apps/backoffice/lib/documents-api.ts` | Exact source/version and immutable bytes/hash; current authority on every download. Statement/CSV are own artifact kinds. |
| Finance endpoints | `backend/src/BackOffice.Api/PolicyEndpoints.cs`, `AgencyTermsEndpoints.cs`, `OperationalPaging.cs`, `Program.cs` | Separate read/command modules, scoped page/count filters, DI registration and OpenAPI/TS generation from actual endpoints. |
| Finance UI | `apps/backoffice/app/(workspace)/[section]/page.tsx`, `components/operations/*`, `components/policies/*`, `lib/operations.ts` | Existing shell, focus/error/loading patterns, saved record navigation and no success before persisted response. |
| SQL/process restart verification | `backend/tests/BackOffice.IntegrationTests`, `scripts/assert-test-results.ps1`, `scripts/verify-operational-suite.mjs` | Actual SQL concurrency, unmodified retained demo roots and exact discovered/executed inventory. |

Resolve real signatures, route registration and migration naming from the current source immediately before each edit. Additive migrations must apply to both empty and preserved seeded databases; generated model snapshot and API contracts are part of owning plans.
