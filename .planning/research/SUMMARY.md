# Focused architecture research

Researched 2026-09-13. Recommendations below are design conclusions for this project, not claims that Microsoft prescribes this domain model. Research conducted inline; no external agents or services received repository content.

## Storage recommendation

Use SQL Server with relational lifecycle/finance tables and policy snapshots in `nvarchar(max)` constrained with `ISJSON`. Index normal relational search columns; use selected typed computed JSON columns only when measured queries justify them. Microsoft documents these storage and indexing options. Native JSON availability differs by platform, so v1 should not depend on it; reconsider against the selected deployment edition during Phase 1. [Microsoft: storing JSON documents](https://learn.microsoft.com/en-us/sql/relational-databases/json/store-json-documents-in-sql-tables?view=sql-server-ver17).

For this workload, adding a separate document store would create another persistence boundary without resolving an identified MVP requirement. Keep the storage choice reversible through application contracts rather than generic database wrappers.

## Concurrency and transactions

EF Core supports optimistic concurrency tokens, including SQL Server rowversion. Use this to reject stale updates, and give users a reload/compare path. A visible editing lease helps collaboration but does not replace database concurrency enforcement. [Microsoft: concurrency conflicts](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).

EF Core supports database transactions. For our domain, issue must atomically create an immutable policy version, the insurance transaction, its accounting obligations, audit and durable work records. External delivery happens after commit and must be retry-safe. Do not hold a database transaction open during an adapter call. [Microsoft: transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions).

## API contracts

ASP.NET Core provides OpenAPI document generation. Use a reviewed contract and generated TypeScript client, with domain commands rather than allowing arbitrary writes to status fields. Contract examples and error responses must be tested. [Microsoft: OpenAPI support](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/openapi/overview?view=aspnetcore-10.0).

Proposed baseline is .NET 10 / EF Core 10. Pin exact supported patch versions and verify local SDK/package compatibility in Phase 2. Select Next.js/React/Tailwind versions from official docs and installed runtime at that time; the reference snapshot's versions are evidence, not an instruction to copy its UI dependencies.

## Source-derived features and pitfalls

- `pPortal()` is internal data visibility reference, not a portal build.
- `pCcPolicy()` explicitly uses a shared policy/transaction/version model with different product risk sections.
- `AUTHORITY()` defines several dimensions per product; a single premium threshold is insufficient.
- `pAsAt()` distinguishes effective date from processing date. Drafts must never appear in the issued timeline.
- `dateRules()` uses hardcoded dates. Replace them with tested date rules; do not copy sample-date membership tests.
- `pAccounting()` separates insurance transactions and accounting movements. Receipt allocation, refunds and bordereau errors must affect stored records.
- `pReporting()` routes multiple report cards to the same sample report; each named category needs a meaningful dataset and filters.
- Customer support flags are person-level, internally restricted, excluded from rating/referral decisions and insurer bordereaux.
- The policy sample contains dates inconsistent with its displayed term. Reconcile demo dates and totals; preserve intended workflow rather than accidental sample contradictions.
- The funnel has string-or-number option IDs and distinct raw/converted forms. Map explicit supported fields into a new schema without assuming transport semantics or editing the snapshot.

## Build order and validation

First establish schemas, state transitions, permission matrix, commands and worked issue examples. Then build a persistent foundation and party records. Implement quote binding as the first complete insurance journey, followed by servicing, product variation, operational workflows and finance/reporting. Unit tests accompany rules; SQL Server integration tests prove rollback, conflicts and duplicate-command behaviour. Do not use an in-memory provider as evidence of SQL Server transaction correctness.

## Remaining design work

Phase 1 must produce the ERD/data dictionary, JSON schemas and examples, full OpenAPI contract, state-transition tables, permission matrix, financial calculation examples, field mapping, screen/action inventory, and demo scenario acceptance matrix. This research is not a substitute for those deliverables.
