---
phase: 09-tasks-documents-communication-and-incidents
plan: '01'
status: complete
completed: 2026-09-21
requirements: [OPS-01, OPS-02, OPS-03, OPS-04, OPS-05, OPS-06, OPS-07, OPS-08]
---

# 09-01 — Source reconciliation and closed operational contracts

Completed design/contract slice only. Runtime OPS requirements remain open. Source ledger preserves118 original controls(62direct,34explicit inherited plus mapped consumers),509 raw display occurrences,33 overlapping commercial claims occurrences and10 supplemental branches. Every control/display/branch has an implementation owner; future Phase11/12 consumers are explicitly retained. No source IDs were regenerated and frontend-code is unchanged.

New scripts/operations-contracts.mjs exports operationalDefinitions(), operationSchema(), relocateOperational(value), writeOperationalContracts(). It produces contracts/schemas/operations.schema.json and contracts/generated/operations.ts. scripts/openapi-operational-runtime.mjs exports addOperationalRuntimeContracts(context), applied after legacy form modifiers in generate-openapi.441 operations now describe the full planned interface;19 new routes carry contract-only status. OPERATIONS-CONTRACTS.md records the endpoint permission/ETag/idempotency matrix, typed parent/FK constraints, lock ordering, immutable provenance, delivery recovery, local file bridge and occurrence precision.

Task DTOs use typed user/team/unassigned assignment, source type union, awaiting-information and derived overdue. Update cannot reparent. Incidents accept incomplete product-specific facts, preserve unknown/date/approximate precision, and reject caller version authority or motor fields on CC. Internal/agency thread and generated-document audiences are closed alternatives. Pack versions/recipients and bulk per-row ETags are bounded. Draft message read/write both permit empty content; send readiness belongs to the service. Original pre-runtime stub components are retained but final routes use Ops-prefixed contracts. Existing issued JSON and functional underwriting/servicing interfaces are unchanged.

## Verification and review

- Initial new schema tests RED on missing artifact; document-audience negative RED in.local/phase9-01-audience-red.log before fix.
- Final root Node suite411/411, zero skipped/failed: .local/phase9-01-final-root.log. Includes9 operational contract and3 source tests plus retained domain/API invariants.
- OpenAPI validation exit0: .local/phase9-01-final-openapi.log,87 warnings(mostly retained unused components). An earlier valid-result shutdown crashed in Windows libuv; explicit retry succeeded. No lint error was suppressed.
- Generated operations.ts compiled using installed TypeScript5.9.3 CLI --noEmit --skipLibCheck --target ES2022. pnpm exec failed to resolve the Windows tsc shim; direct installed CLI passed.
- Global coverage949/949 reviewed controls,5 conditional rules and441 operations; acceptance matrix regenerated. git diff --check passed.
- Reviewed source bindings, product-specific branch checks, generated final schemas and runtime-status separation; corrected task comment history, typed owner fields, date/occurrence mapping and preview operation. No unresolved HIGH/CRITICAL finding in this bounded contract slice.

No SQL/browser tests were run for this schema-only plan; no migrations, DI or runtime endpoints exist yet. Plan09-02 owns the typed registry, tasks, scope, SQL boundaries, endpoint wiring and real-SQL negative/concurrency tests. Later plans own PDFs, UI and adapters. Human UAT remains unperformed. Do not mark OPS or compound CC-05/POL-01 complete from these tests.
