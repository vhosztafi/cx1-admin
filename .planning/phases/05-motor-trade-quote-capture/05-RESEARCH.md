# Phase 5 research — quote persistence and capture

Researched 2026-09-15. Architecture recommendation established; detailed source-control/field audit and data/API/UI contracts are still required before executable plans. No quote implementation or plan approval is claimed by this document.

## Existing evidence

The current code has ProductVersion, agency terms/grants, client relationships, match intake/reviews, command receipts and identity scopes. There is no implemented Quote or QuoteRevision entity/service yet (source search of backend/src). The Phase1 data model explicitly assigns immutable quote revisions and a current-revision pointer, so this phase should extend that design instead of introducing a second draft store.

`docs/design/FUNNEL-MAPPING.md` records255 field occurrences and14 source steps, including nested driver histories, conditional declaration details, distinct owned/specified vehicle roles and typed reference values. `RawForm`/`RawDriver` permit unknown keys, numeric strings and dates; current driver/vehicle Yup schemas also contain documented legacy-message/behaviour uncertainties. These are reference evidence, not safe server DTO definitions. Motor Trade Combined property sections need prototype coverage beyond the Road Risks funnel.

## Decisions and rationale

| Decision | Rationale / implementation implication |
|---|---|
| SQL2022 hybrid relational/JSON | Retain SQL ownership, foreign keys, rowversions and transactions; store bounded valid JSON snapshots. No need for a new database engine or SQL2025-native JSON dependency. |
| Append revisions | Save each accepted edit as an immutable revision with quote-scoped sequence, canonical hash, schema and pinned product/question/reference versions. Atomically update the current pointer and search projection; preserve prior declarations. |
| Separate readiness | Shape-valid incomplete risk saves remain resumable. Complete-risk validation checks question applicability, allowed references and cross-item constraints; it is not equivalent to rating eligibility. |
| Relational discovery | Quote identity/state/relationship/product/current revision are indexed columns. Registration arrays need an owned relational projection rebuilt in the same transaction, rather than unbounded JSON scans or trusting a client-supplied search string. |
| Typed API boundaries | Existing DTOs use strict inputs and explicit projections. Reject unknown members and server-owned approval/rating fields, preserve missing versus false and exact money strings. Runtime semantic validation supplements JSON syntax/schema checks. |
| Current authority before replay | Reuse SqlCommandBoundary.ExecuteAuthorizedAsync and agency-first scope locking. Define a consistent quote/match/relationship lock order and race tests before enabling commands; a stale successful receipt cannot bypass revoked access. |
| Version-pinned adapters | Reuse existing durable worker/attempt conventions for allowed postcode, vehicle and driver lookups. Persist request/outcome/source/version and explicit selected candidate or manual declaration. No provider result implicitly verifies user answers. |
| Match progression fence | Add a real QuoteId/FK and prevent the existing MatchService from reopening/reassociating a progressed quote. Define the allowed draft boundary and the future Phase6 transition interface now, with concurrent tests. |
| Clone remapping | Retain original quote/revision provenance, create a new quote, remap stable item IDs and internal cross-references consistently, remove nontransferable evidence/lookup/rating/acceptance state, and revalidate relationship/product access. Final rules need explicit schema audit. |

## Official technical verification

Microsoft documents SQL Server's relational/JSON storage and indexing support. For this repository, keep `nvarchar(max)` plus checks and indexed relational projections; computed scalar indexes may be used where a stable frequently queried path is justified. [Store JSON documents](https://learn.microsoft.com/en-us/sql/relational-databases/json/store-json-documents-in-sql-tables?view=sql-server-ver17), [Index JSON data](https://learn.microsoft.com/en-us/sql/relational-databases/json/index-json-data?view=sql-server-ver17).

EF Core supports optimistic concurrency tokens such as SQL Server rowversion. Existing held SQL transactions still need explicit command authority and ordering; rowversion alone does not enforce cross-aggregate business rules. Keep MARS disabled because EF savepoints are incompatible with it. [Concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency), [Transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions).

System.Text.Json can reject unmapped members via Disallow. This supplies one strict DTO guard, not a replacement for validating dynamic versioned question IDs, applicable answers and referenced risk items. [Unmapped members](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/missing-members).

## Required detailed design work next

1. Enumerate the prototype quote list/detail/capture/actions and all255 funnel occurrences; map exact fixed options, repeated records, conditionals and source ambiguities. Preserve all fourteen source steps in the mapping without requiring fourteen identical back-office routes.
2. Set quote/revision/product/relationship ownership, item-ID/reference invariants, bounded JSON sizes, unique indexes, canonical hashing, duplicate JSON property policy, temporal input conversion, clone remapping and withdrawal semantics.
3. Specify endpoint requests/responses, capability/scope, ETag resource, ID receipt, errors, paging/search indexes, safe agency projection and client/match links. Separate saved draft readiness from future rating/bind states.
4. Define pinned demo product/reference/question/lookup configuration and realistic fictional complete/incomplete examples for both products. Distribution grants on draft product definitions do not establish rating readiness.
5. Write UI-SPEC, PATTERNS, VALIDATION and manageable vertical plans, then review requirement/control/dependency coverage before implementation. QUO-01 policy discovery remains with Phase6 and must be recorded as partial until real policy records exist.

## Validation targets

Unit/schema: complete versus incomplete, unknown/sensitive keys, exact money/typed references, local time boundaries, child IDs/cross-references, conditional requirements, driver/vehicle/history rules, clone isolation and revision hash invalidation. SQL/API: immutable revisions/current pointer, rollback and same-key races, suspended agency/current role fences, quote/match progression race, owned search/count/cursor boundaries and durable lookup recovery. Browser: both products, real captured fields/children, save/resume/reload, manual lookup path, stale/uncertain recovery, clone/withdraw/history, actual client/agency navigation,314px rail and390px containment. Repeat Phase4's retained regressions and final no-skip gates.

Baseline entering Phase5:329backend including57realSQL;81contract tests;30frontend tests;20browser journeys and15data-set restart check pass. Native SQL2022 is verified; hosted CI, Docker runtime and human UAT remain unperformed.
