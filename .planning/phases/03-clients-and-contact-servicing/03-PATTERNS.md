# Phase 3 code patterns

| New responsibility | Closest existing code | Reuse with these changes |
|---|---|---|
| Party records/mapping | Infrastructure/Persistence/FoundationRecords.cs, BackOfficeDbContext.cs | Separate PartyRecords/PartyModel files; retain common metadata, UTC checks, no cascade and generated migrations |
| Validated domain commands | Application/JobRetryBudget.cs; Infrastructure/Platform/SqlJobRetry.cs | Pure validation/transition rules plus SQL transaction service; no controller business logic |
| Audited client/contact/flag/match commands | SqlCommandBoundary, OperationalRetryEndpoints | Current capability and resource scope before replay; parent lock/precondition inside; extend saved ETag metadata compatibly |
| Client/relationship reads | OperationalReadEndpoints, OperationalPaging | Dedicated scoped projections, new cursor protection purpose, current scope fingerprint; no sensitive audit fields |
| Detail/list/form UI | components/admin-workspace.tsx, primitives.tsx; app/(workspace)/[section]/page.tsx | New explicit clients/[clientId] and matches/[matchId] routes; server actor guard and bounded client forms; use actual API DTOs |
| Blue record header/action rail | docs/design/source/prototype-template.txt recordHeader/render | Extract source dimensions/colors into reusable components; do not infer layout from generic dashboard cards |
| Integration tests | SqlFoundationTests, OperationalJobTests, BatchRetryTests | Fresh CoverMGA_Test_GUID per scenario, real cookies, concurrency and rollback; no new in-memory fallback |
| Browser evidence | scripts/verify-shell-browser.mjs, verify-operations-browser.mjs | New client journey; unique fictional names, preserve SQL history, local-only origin; no secret output |

Do not edit frontend-code. Do not duplicate auth, antiforgery or HTTP error plumbing. Avoid expanding admin-workspace.tsx for unrelated client features.
