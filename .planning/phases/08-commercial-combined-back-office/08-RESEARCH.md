# Phase8 — Commercial Combined implementation research

**Researched:** 2026-09-19. **Confidence:** high for repository integration and source scope; medium for proposed demo underwriting assumptions, which require executable examples. Research is complete enough for planning; no implementation or business approval is claimed.

## Recommendation

Keep Next.js/TypeScript/Tailwind, .NET10 and SQL Server2022. Extend the shared quote and servicing lifecycle with closed Commercial Combined product contracts and explicit product dispatch. Keep property risk in immutable JSON; add a narrow relational exposure projection for book aggregation. Do not build a second policy system or introduce a document database for this phase.

CC-01..04 are the completion target. CC-05 remains partial until Phase9 implements the generic incident/document operational journeys. Phase8 supplies tested CC payloads and document selection, not a claim that an outbox request has already produced a document.

## Evidence and source inventory

| Evidence | Finding | Planning consequence |
|---|---|---|
| `contracts/quote-control-ownership.json` |166 controls already assigned featurePhase8 | Preserve every source ID and dependencies; extend this ledger instead of a new denominator |
| `contracts/quote-question-catalogue.json` |109 deferred CC question definitions, including location/wage/loss modal questions | Promote with versioned applicability/options and exact persisted destinations; distinguish occurrence coverage from distinct questions |
| `docs/design/control-inventory.json` |60 `pCcPolicy` controls, many repeated fallback links from rendering invalid global tabs | Reconcile all60, explicitly separate valid CC actions, product-navigation fallbacks and Phase9/10 owners; do not manufacture60 CC workflows |
| `docs/design/source/prototype-template.txt` |Ten CC stages; property, liability/employee and BI tabs; CC readiness/referral branches | A generic JSON editor or renamed MT wizard will not meet the source |
| `QuoteRules`, `QuoteReadiness`, `QuoteBusinessRules`, `QuoteDriverRules` | MT-only product/driver assumptions | Explicit product strategy selection, fail closed for unknown product; existing MT tests remain unchanged |
| `QuoteRatingRules`, `UnderwritingConfiguration`, `CapacityAuthority` | Driver/vehicle/stock facts and authority dimensions are structural, not only UI labels | Add CC facts/configuration/referral/capacity dimensions; do not fill invented driver values |
| `PolicyIssueWriter`, `ServicingIssueService` | Existing atomic versions, money, documents, receipts and held authority | Integrate at shared issuance boundaries; preserve exact snapshot/provenance/journal contracts |
| `PolicyTemporalSelector` | Effective/known time, transaction sequence and slice ordinal determine applicable cover | Exposure must follow these same winners; mutable current pointers are insufficient |
| `SqlCommandBoundary` | Actor/route/idempotency-key application lock only | Does not serialize two different quotes competing for district capacity |
| `contracts/examples/commercial-combined.json` | Earlier illustrative policy JSON, not live quote capture | Preserve old schema/example validity; introduce explicit live CC capture/version shapes |

The prototype's CC branch is followed by MT checks outside the CC conditional. Record that source defect and implement only applicable CC checks. The sample policy's fixed district counts, financial balances and MEL values are not operational data.

## Domain model and schema decisions

The modular property, business interruption, employers' liability and public/products liability split is consistent with an established combined product. This supports the source's section model; it does not supply our rating or authority rules. [AXA Business Combined](https://www.axaconnect.co.uk/commercial-lines/branch-traded/business-combined/).

Use a discriminated CC draft branch with a new explicit capture version, retaining the existing MT branches and historic schemas. Bundled backend validation, generated frontend types and response validation must share this contract. Reject duplicate JSON keys, unexpected members, product mismatch and cross-item references. Drafts may omit unanswered facts; readiness validates applicable mandatory facts separately. Never convert unknown to false or zero.

| Risk area | Proposed persisted content and constraints |
|---|---|
| Proposer/business | Shared party/contact identity; CC business description, activities with percentages, turnover, trading history, prior names/insurer, territory split and declarations |
| Locations | Stable server-validated UUID; reference/address/full postcode, normalized district, occupancy, construction, protections, flood/subsidence and relevant question responses |
| Property | Per-location buildings, contents and stock SI; declared machinery/computers as specified contents breakdown, not extra SI silently counted twice; basis/excess/selected extensions |
| BI | Selected flag, basis, sum insured, indemnity months, time excess and source extensions; no BI rating or document section when not selected |
| Liabilities | Separate EL/PL/products selection, limits, basis/excess; ERN only where applicable; work away, heat, height, hazardous locations, products/territory facts |
| Wages | Stable row ID, catalogue category, headcount, employees/LOSC/BFSC amounts and notes; exact totals with explicit rating treatment |
| Losses | Stable row ID, date, type, optional location subject, description, paid/reserve, insured and status; consistent last-five-years declaration |
| Responses | All109 question IDs retained in catalogue with global/location/wage/loss subject scope, conditional applicability and stable option references |
| Cover | Typed selected sections, limits, excesses, endorsements/warranties; restrictions refer to existing risk subjects only |

Use decimal currency strings at API boundaries, decimal arithmetic in .NET and the existing exact-penny UI helpers. Aggregate using checked decimal sums. Bound row counts, text sizes, monetary values and percentage sums. Keep supplied `maximumEstimatedLoss` distinct from the calculated conservative demo proxy; it must never lower authority exposure automatically.

All data/schema decisions receive worked examples and negative cases before broad UI implementation. No migration changes historic JSON or rewrites previously published configuration. Migrations are additive; new optional shared metadata must permit old MT rows without weakening new CC invariants.

## Product dispatch and API integration

Prefer feature-local CC rules behind a small product-dispatch boundary. Preserve shared scope, transaction, revision, lease, rating job, evidence, internal referral, carrier, terms, acceptance, issue and journal orchestration. Avoid a speculative plugin framework.

The implementation must inspect every consumer of MT `RatingFacts`, referral risk and cover dimensions. Configuration publication and consumption must both select the CC schema. A CC allowlist entry is insufficient if downstream consumers still require drivers or motor proof.

Extend existing quote/draft endpoints with typed CC request/response branches where the operation already exists. Use dedicated CC collection operations only where location/wage/loss identity makes that necessary. Record exact existing route names and operation IDs in a Phase8 API surface contract before implementation. Commands retain current authorization before receipt replay, exact ETag/lease, idempotency-key request hash, bounded reason and closed problem responses. Never accept caller-supplied premium, book totals, product identity or authority outcome.

Add scoped exposure assessment reads under the quote/draft/policy record, returning district, effective interval, own proposed SI, book SI, capacity/headroom, observed time and configuration provenance. These are advisory snapshots. Issue computes authoritative exposure again. Whole-book aggregates are available only to internal users with the relevant underwriting capability; agency users receive their own exposure and a capacity outcome, not foreign policy counts or book totals. No endpoint reveals foreign policy/location IDs or addresses. A list filter never changes the underwriting calculation.

Exact replay of an already-issued command must return its receipt after current scope/grant checks; it must not fail because later unrelated issues consumed capacity. The fresh aggregate gate belongs only on a new effect, after replay lookup and before the atomic writer. An expired receipt still cannot recreate the issued effect.

## Demo rating and authority

Publish separate CC product/reference/question/rating/binder/authority/terms/template/renewal settings using missing-only initialization. Keep immutable IDs and hashes pinned in every result. Label calculations as demo assumptions in configuration and business-facing rating explanations.

Use a documented factor model: property buildings/contents/stock rates by accepted construction/occupancy band; selected BI SI with indemnity multiplier; EL employee and LOSC wage factors by category (BFSC separately declared and configured); PL/products turnover factors and limit factors; explicit source-risk loadings; minimum premium; configured fee/tax/commission. Round component factors to pennies using the established midpoint-away-from-zero rule, then use the shared term proration and posting routines. Do not silently adopt a real insurer's prices or infer tax law from a prototype.

Choose concrete seeded rates and golden calculations during the first contract plan, including positive, zero and negative MTA movement. Every supported selection must affect cover, price, readiness or a documented underwriting decision; no cosmetic questions with silently discarded answers. Unsupported selected cover fails readiness rather than pretending to insure it.

Initial prototype-based demo ceilings: single-location property SI £2.5m, largest-location proxy £2m, district property SI £40m; EL £10m and PL/products £5m sample limits. These are configurable fictional values. `locationSI = buildings + contents + stock`; `policyTSI = sum(locationSI)`; `demoMEL = max(locationSI)`. BI and liability limits are separate dimensions, not added into the property district bucket. Count distinct active policies for the district count and sum every applicable location there.

Map all source referrals: flood/history/zone, movement where subsidence selected, panels/timber/listing, waste/recycling, unoccupancy, heat/height/hazardous work, products/territory, health/safety, insolvency/declined terms and loss frequency. Separate informational loadings, internal referral, carrier exception and outside-appetite decline. CC evidence targets are exact locations/sections/losses; Motor Trade proof/NCD/driver evidence never becomes mandatory for CC.

Treat district aggregate as a hard book ceiling in this MVP. A single-risk carrier exception cannot enlarge it. Capacity changes require a separately published book limit; no request or response field can bypass the ceiling. Per-risk carrier permissions remain exact-subject, exact-cycle, dated and independently accepted.

## Dated book exposure and concurrency

### Storage

Add a stable `CommercialExposureBook` identity with product/provider scope and an explicit immutable mapping from each CC binder version. Do not use binder-version ID as the aggregation identity: publication of version2 must not make version1 exposure disappear. Multiple genuine books may be added later; the seed has one CC book.

Publish `CommercialExposureLimitVersion` separately for that book, with district/default scope, coverage-effective interval, publication provenance and exact limit. Select the applicable published limit at each evaluated interval using the assessment's known cutoff; pin its ID/hash in the decision evidence. Old binder versions cannot supply a competing larger ceiling. Missing or ambiguous applicable limits fail closed. Configuration publication must use the same exposure lock if it changes an operational book limit; Phase11 owns its administration UI.

Add an immutable, derived `CommercialExposureVersion` header for **every** issued CC policy version, including cancellations and versions with no property exposure. Link policy/term/transaction/version using compound foreign keys, record book, effective/processed provenance and source content hash. Add immutable location children keyed by header/location with normalized district and SI. Empty cancellation headers are essential: otherwise the prior property version remains falsely active. Unique version/location keys and database guards prevent duplicate or mismatched contributions.

Header projection fields also include term start/end, transaction kind/sequence and slice ordinal, verified against their immutable source. Book queries can then select temporal winners from the exposure projection without taking locks on unrelated live policy rows. The source writer verifies this metadata while holding its own policy transaction.

The projection is query data, not a second policy truth. Derive it inside the same transaction as the issued JSON, journals, outbox and receipt. Verify it against source JSON and the shared temporal selector. No mutable end-date edits on old contribution rows; later versions alter the selected winner. No arbitrary SQL imports of policy risk.

### Time semantics

For each effective instant E and known cutoff K, select the same policy term/version winner as `PolicyTemporalSelector`; count contributions only when its state is active. Scheduled future issues do not count now, but reserve their future intervals when another proposal is checked. Expired/cancelled winners contribute zero. The projection header must cover all transaction kinds needed for that selection.

For a proposed issue/MTA/renewal, overlay its complete prospective version/slice set on its own policy, replacing that policy's applicable contribution. Evaluate every relevant breakpoint across the proposed coverage interval: term starts/ends, existing issued effective dates, proposed slices and scheduled cancellations/renewals. Checking only the first day is insufficient. Same-district multiple locations sum; moving districts removes the old contribution only from the effective change onward. Early renewal must preserve the expiring term and avoid overlap/double-counting.

Evaluate the complete resulting state at each breakpoint, not a summed historical ledger. A future MTA cannot release headroom today, and backdated issue must check all affected known intervals. Keep current-date display distinct from historical E/K views.

### Lock protocol

For the MVP, serialize CC exposure-changing commands with **one transaction-owned exclusive CC exposure application lock**. This intentionally trades write throughput for a simple reviewable correctness boundary. Use a fixed canonical resource (well below255 characters), bounded timeout, and reject/rollback on every negative return code. SQL Server documents transaction lock lifetime and the need to handle negative/deadlock results explicitly. [Microsoft sp_getapplock](https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-getapplock-transact-sql).

Acquire that lock at the beginning of the authorized command transaction, before quote/policy/draft row locks. All CC new issue, MTA, renewal and cancellation writers follow the same order; no alternate seed/writer path may bypass it. The trusted product hint may select the lock but never authorize access. Keep current scope checks before receipt disclosure. After receipt lookup, a new effect rechecks the full dated book and writes projection plus policy state in the held transaction. Replay does not consume capacity twice.

Do not read-lock every foreign policy while holding a source policy lock: that invites lock inversion. Query committed immutable exposure metadata under the serialized writer boundary. Advisory reads need a coherent query/cutoff and must identify their observed time; they grant no reservation. Later optimization may partition locks by book/district with canonical ordering after dedicated concurrency tests.

An alternative indexed SERIALIZABLE range-lock strategy is valid but requires range-covering indexes and careful empty-range handling. The existing command-key lock alone cannot prevent aggregate write skew. Microsoft describes phantom prevention and index prerequisites for key-range locks. [SQL Server locking guide](https://learn.microsoft.com/en-us/sql/relational-databases/sql-server-transaction-locking-and-row-versioning-guide?view=sql-server-ver16).

## Policy UI, servicing and Phase9 contracts

Use the existing shell and responsive record layout. CC tabs: Overview, Risk Details, Cover, Property schedule, Liability & employees, Business interruption, then shared supported operational tabs. Remove Drivers/Vehicles/MID from CC. Bind all cards/tables to stored selected-version values, including two-location totals and calculated headroom. Historical pages must never replace their risk with a live draft.

CC adjustment editors use stable location/wage/loss IDs, explicit removal/clear and before/after values. Shared proposal slicing, expiring-version renewal base, cancellation notice/issue, exact financial movements and history remain authoritative. Scope all linked records. Generic Phase9/10 controls expose their honest unavailable state and owner; source demo links to a hardcoded MT policy do not belong in live CC records.

Document selection must be product/cover-aware. Existing `PolicyIssueWriter.DocumentKinds` unconditionally requests a certificate: CC needs an EL certificate only when EL is selected. Persist schedule/statement and applicable certificate requests against exact issued version/template; reuse the same rule for MTA, renewal and cancellation output. No claim of statutory compliance or delivered certificate is made by a queued request.

Define a closed versioned CC incident subject contract: property location/damage or liability section/employee occupation/third party, occurred time, policy/version and source hash. Validate subject existence and effective cover from that immutable version. Phase9 owns actual creation/status/TPA handoff. Product document payloads include property, BI, liabilities/wages and conditions without motor identifiers. Contract tests prohibit substituting current risk for selected historic risk.

## Validation Architecture

Reuse xUnit unit/integration, native SQL2022, root Node contract/source tests and existing frontend/browser harnesses. No new testing platform. Each implementation slice adds meaningful failing cases before rules and proves the fixed behavior. New infrastructure tests use isolated databases and dynamic HTTP ports; port5000 is explicitly rejected in browser fixtures.

Required coverage: all166 capture controls/109 questions reconciled; all60 policy-control records dispositioned; every CC readiness/referral branch; closed request/response and old snapshot compatibility; cross-product and foreign-item denial; save/resume/clear/removal; deterministic exact-money rating and all movement signs; independent evidence/authority/carrier/terms/acceptance; timed capacity breakpoints; concurrent issues near a ceiling where exactly one succeeds; rollback/no duplicate effect; cancellation release and expired/scheduled exposure; exact historic and expiring-base selection; actual desktop and390px browser issue/servicing readback; retained MT regression.

At phase closure run one full current-source backend gate, frontend lint/typecheck/build/tests, root contract/source suite, CC browser aggregate and retained underwriting/servicing aggregates. Capture strict TRX totals including real SQL cases, not only exit codes. The preceding phase's full backend took83minutes; use short targeted filters during development and one final full gate, not repeated full suites after every small task. Record actual runtime instead of promising sub-minute database feedback.

Repeat missing-only initialization and compare all retained table fingerprints; restart verified owned API/web processes with persistent keys and compare immutable CC/MT graphs using pinned E/K. Business review, assistive-technology UAT, hosted CI and Docker are separate unperformed checks unless actually run.

## Planning risks and explicit constraints

- The shared code is mature but structurally MT-specific. Contract/strategy integration deserves its own slices before enabling CC selection.
- Exposure projection must represent zero/cancelled versions and binder-version continuity; otherwise plausible aggregate tests can pass while real timelines fail.
- Never lock a book after acquiring policy locks in one path while doing the reverse in another.
- Do not silently round user monetary input, default unanswered required questions, or flatten per-location responses globally.
- Keep source coverage denominators unchanged and distinguish source fallbacks from real product behavior.
- Concrete configuration rates and exact API route additions remain the first implementation contract deliverable, with golden examples; they are not unresolved business questions.
- No frontend-code modifications, database reset, real provider communication, new IDP or operational Phase9/10 scope expansion.

## RESEARCH COMPLETE

Proceed to validation strategy, UI contract and checked sequential plans. This report records architectural choices; runtime claims require the planned evidence.
