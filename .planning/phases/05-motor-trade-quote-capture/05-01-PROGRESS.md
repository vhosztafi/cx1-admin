#05-01 progress — source and strict quote contracts

In progress; no quote endpoints enabled and no plan/requirement completion claimed.

## Completed first contract slice

- Added typed optional canonical DriverOccupation, CriminalConviction, CountyCourtJudgment and VehicleModification definitions, preserving stable child IDs. Driver source limits5occupations/100criminal/100CCJ entries retained. Existing issued policy examples still validate; optional additions do not rewrite prior snapshots.
- Added generate-quote-contracts.mjs invoked by the canonical policy generator. Quote draft/ready write shapes allow only Motor Trade capture data and explicit local term intent. They exclude premiums, settlement, provenance, product-version/agency/client ownership IDs and caller-supplied evidence IDs. Trusted envelope/UTC fields are assembled by the future service.
- Partial quote children require IDs; selected references and scalar answers remain fully typed/versioned. Missing answers can be omitted. Structurally complete short-period terms need entered end date/time; runtime DST/chronology remains a domain validation obligation.
- Four new semantic contract tests cover forbidden data, child identity/typed answers, repeated history dates/money/sentences and both product fixture conversions. First strict-Ajv compile found missing local property declarations in the conditional end-field schema; corrected and reran. All85contract/design tests, OpenAPI lint and949control/328operation source checks pass. No backend/UI source changed; baseline329backend/57SQL and30frontend is retained, not falsely rerun.

## Required next

1. Finish exact canonical mapping for all255source occurrences and364candidate controls, including product applicability, UI-only/deferred ownership and fixed option/reference definitions.05-SOURCE-AUDIT remains enumeration, not signed contract mapping. Preserve source limits and document any named demo assumptions.
2. Complete repeated annual-European cover/other missing paths and conditional semantics; add duplicate JSON key/question/item IDs, reference whitelist and cross-item validation. JSON Schema shape checks do not enforce those by themselves.
3. Specify canonical/runtime request DTOs, complete product/question/reference configuration, quote-ready semantic checks and all new OpenAPI operations. Match existing source operation IDs. Do not expose unfinished APIs.
4. Run exhaustive source drift/option/conditional tests and full contract validation; only then summarize05-01 and proceed05-02 persistence. No reset/funnel changes or new approvals.


## European cover structure slice —2026-09-15

Read the actual extras domain/interfaces/schema and mapped source MTS-11-Q11/12/14–20. Added separate stable-ID annual and temporary European-cover records to canonical and quote schemas. Temporary trips retain registration, local dates, versioned area/cover/usage selections and unique driver-ID selections; UI isDraft and derived driver-name arrays are forbidden. Nine exact child-field paths are recorded in contracts/quote-field-mapping.json and mirrored in the audit. The mapping is explicitly partial; remaining source fields/catalogues/domain constraints are not certified.

Three added tests cover malformed dates/duplicate selections/UI-state injection/missing IDs, complete trip shape requiredness and exhaustive comparison of these nine source occurrences against resolvable schema paths. Contract tests use committed contracts/design evidence rather than depending on .planning artifacts. Full validation passes88cases/OpenAPI/949controls/328operations. Existing policy examples stay valid. Domain trip chronology/term bounds, current same-proposal driver membership and named/any-driver applicability still require semantic validation; structural schema success is not readiness. No backend/UI code, endpoints, seed resets or funnel changes; baseline backend/frontend evidence is unchanged.

NEXT finish remaining246field occurrence mappings plus364control ownership, versioned catalogues and semantic rules, then full quote OpenAPI.05-01 remains in progress; no plan/phase acceptance inferred.


## Driver field mapping slice —2026-09-15

Mapped all55individual-driver source occurrences MTS-06-Q08–62 to concrete structural paths or explicit typed answer IDs; retained original option-collection provenance. Mapping now covers64of255occurrences (driver basis Q01–07 is still pending). Tests resolve every mapped path in the generated schema and verify source ownership/path/type without importing planning files. Reference catalogue membership, applicability and complete readiness remain pending, so this is shape coverage only.

Source inspection caught three potential lossy conversions: Driving Licence Issue is not date of test; claimStatusID is a versioned declared reference, not automatically the normalized loss state; house/street/city are separate captured address components. Added issuedOn, declaredStatus, explicit address components and conviction disqualified boolean to retain them independently. Existing canonical fields remain intact and existing policy snapshots validate. Source driver residency and age restriction questions use identified date/boolean/reference answers, not label-based coercion.

Full90contract/design tests/OpenAPI/949controls/328operations pass after regeneration. No backend/UI edits, migrations, seeds or active previews/tests. NEXT remaining191field mappings and364control dispositions, complete catalogues/semantic validation/quote OpenAPI. No05-01completion or runtime readiness claim.


## Complete source field shape mapping — 2026-09-15

All 255 funnel source occurrences now have exact canonical paths and tested scalar, reference or identified-answer kinds. Added the remaining 191 mappings: 52 ordinary/specified vehicle fields, 68 business/activity/declaration/portfolio fields, and 71 proposer/premises/cover/driver-basis/trade-plate/extra/material-fact fields. Both source vehicle sets share stable-ID canonical vehicle rows, with specifiedVehicleIds retaining subset identity. Declaration detail and portfolio detail/percentage mappings retain their controlling question IDs.

Optional schema extensions preserve registration versus manufacture year, manual body/engine descriptions, declared owner type versus normalized ownership, lease duration, vehicle flags, proposer names/company-type reference/mobile, fractional premises years, shared worksite and NCB versus prior-policy expiry. Marketing scopes and contact methods remain separate multi-reference answers. Policy examples continue to validate without rewriting their data. Portfolio percentages use integer basis points; scalar values cannot be arbitrary JSON. Existing business rules, reference metadata and source UI quirks are not silently treated as runtime implementations.

Six new tests cover exhaustive 255-field path/type/source provenance resolution, both vehicle sets, declaration/portfolio condition provenance, percentage bounds/units, independent vehicle facts, consent multi-selection and NCB dates. The exhaustive test found the original nine European mappings omitted explicit null optionCollection provenance; corrected those records and their audit copies. Full validation: 96 contract/design cases, OpenAPI lint and 949 controls/328 operations. No backend/UI edits or database reset; previous backend/frontend evidence remains unchanged.

NEXT: classify all 364 candidate controls, build complete pinned question/reference catalogues (including source limits, dynamic option derivation and product applicability), implement semantic rejection contracts for unknown/duplicate keys/questions/items and invalid cross-links/chronology/DST, finish quote OpenAPI request/response definitions, then rerun the complete gate. Shape mapping is complete; 05-01 is still in progress and no runtime readiness or phase acceptance is claimed.


## JSON and semantic identity contract slice — 2026-09-15

Added scripts/quote-semantic-contract.mjs as an executable design contract, not a running API. The future .NET request boundary must implement the same checks after schema validation. Strict parsing enforces a 1 MiB UTF-8 payload and maximum 64 container levels, rejects duplicate object keys including escaped-key equivalents, and retains standard JSON grammar rejection. It does not silently keep the last conflicting property.

Semantic checks enforce globally unique child UUIDs across all collections, case-insensitive UUID equality, current-proposal typed links for specified vehicles/vehicle owners/European trip drivers/loss risk items, and unique referenced IDs. Identified supplemental questions are allowlisted by exact mapped parent scope and answer kind; duplicates are rejected within each response set while the same question may legitimately occur for two different drivers. The expected question-set version is an explicit trusted argument; a missing/changed supplied version yields an issue. Returned issues contain codes and JSON pointers, not captured answers.

Seven new tests cover parser grammar/Unicode keys/UTF-8 boundaries/depth, global nested identity, orphan and wrong-type links, UUID casing, wrong-scope/unknown/duplicate questions, kind/version mismatches and legitimate repeated question scopes. Full validation passes103contract/design tests, OpenAPI lint and949control/328operation source checks. No backend/UI/API implementation or database effects; previous backend/frontend evidence remains baseline. These helpers are design acceptance evidence only, and must be ported and independently tested at the real .NET boundary before quote writes open.

NEXT complete364control dispositions, pinned question/reference option catalogues, conditional readiness/chronology/DST/source bounds and complete quote OpenAPI;05-01 remains in progress. All255field shapes are mapped. Reference label/value/version membership is still pending, as are product-specific readiness and actual runtime enforcement.


## Pinned source reference identity catalogue — 2026-09-15

Located the bundled source snapshot at frontend-code/public/api/options.json (read only). generate-quote-reference-data.mjs now produces contracts/reference-data/motor-trade-source.json with a content-derived version mt-source-b79d995c5a10ddd7, full SHA-256 provenance, all51direct option families and25nested families (17own-indemnity excess sets and8young-driver CC/indemnity sets), preserving labels, value types, order and source metadata. All57source reference field occurrences bind to explicit collections. Null optionCollection in the historical mapping stays intact as source provenance; the new catalogue resolves those missing source bindings explicitly.

Reference validation checks exact typed value, approved collection, pinned version and label, rejecting duplicates in multi-selection answers and unsupported reference paths/scopes. Three dynamic questions (own indemnity excess, young-driver indemnity and CC) fail closed without trusted selected-collection context. Context is keyed by exact instance JSON pointer, not only question ID, so different drivers cannot share another driver's age band. Selection/limit derivation must still be implemented from pinned current-proposal facts, not caller input; catalogue membership does not imply product eligibility.

Six tests verify source hash/metadata/row preservation across all76families, exhaustive57field bindings, value type and forged-label/version/collection rejection, multi-selection duplicates/scope, missing/wrong dynamic context and separate driver contexts. Full validation passes109contract/design tests, OpenAPI lint and949controls/328operations. No actual quote API or backend/UI changes; previous backend/frontend evidence remains baseline.

NEXT364control dispositions, typed product/question configuration and dynamic eligibility/conditional readiness (chronology/DST/source bounds), then complete quote OpenAPI and actual .NET parity. The pinned funnel reference identity snapshot is available; prototype-only reference fields and all runtime readiness requirements remain part of the unfinished05-01gate.


## Term intent and conditional answer design validation — 2026-09-15

Added quote-term-contract.mjs using London timezone data to resolve allowed offset candidates, rejecting nonexistent spring times, requiring explicit offset choice for repeated autumn times, rejecting a mismatched supplied offset, and retaining incomplete intent as readiness issues. Complete annual terms use the existing calendar anniversary/leap-day rule; annual end date/time cannot be caller-overridden. A derived annual end that falls into a gap is reported against start-time input, and a repeated end needs endUtcOffsetMinutes. Short-period chronology compares resolved UTC instants, including an end with an earlier displayed clock time during the repeated hour. Inputs are not mutated. Source/product term bounds still need configuration; this helper is design evidence, not a .NET API.

Added mapped conditional-answer checks for the ten declaration pairs, business association details and portfolio child questions. Required text cannot be whitespace; retained inactive children are surfaced rather than deleted, and orphan child answers require their controller. Portfolio readiness requires at least one category, positive integer basis-point shares and an exact10000total. Independent question requiredness, further product/driver/cover conditions and source bounds remain pending.

Nine new tests cover date grammar, London DST gap/repetition/offset cases, incomplete terms, leap and offset-changing anniversaries, derived end failures, resolved chronology, every declaration pair, business scope and portfolio totals. Full118contract/design tests, OpenAPI lint and949control/328operation source checks pass. No backend/UI/DB effects; prior runtime evidence remains baseline.

Inspected364candidate control groups and confirmed the existing prototype-quote-questions.json records product-specific stages (including Commercial Combined), while prototype-detail-questions.json contains additional driver declarations. Those prototype-only question IDs still need integration with the allowlisted question configuration; current funnel identity mappings alone are not complete back-office coverage. NEXT reconcile those definitions and364control dispositions, finish dynamic selectors/source bounds and complete quote OpenAPI before05-01can pass. No plan/phase acceptance claimed.


## Prototype supplemental question integration — 2026-09-15

Added generate-quote-question-catalogue.mjs and generated quote-question-catalogue.json plus reference-data/motor-trade-capture.json. The three existing prototype question fixtures contain164definitions. Source product-stage evidence and explicit detail-modal containers identify55Motor Trade supplemental questions and109Commercial Combined questions retained with Phase8 ownership. All255funnel source mappings remain, giving310active mapping records. Premises detail questions apply only to Motor Trade Combined; CC location/wage/loss detail questions do not enter Motor Trade allowlists.

The combined catalogue adds12prototype reference families with exact source options/ordinal identity, alongside the57funnel bindings, and uses a content-derived shared question/reference version mt-capture-e410d61471e481ed. The original bundled-source catalogue remains independently pinned. quoteMappingsForProduct returns only approved Motor Trade product question mappings and rejects unsupported products; the future boundary must select this trusted allowlist before question validation. Reference membership, question scope and product scope checks remain separate required gates.

Four new tests cover all164definition dispositions, every310mapping path in the real quote schema, product-specific question allowlists including CC exclusion and Combined-only premises, and all12prototype option families/forged-label rejection. Full122contract/design tests, OpenAPI lint and949controls/328operations pass. No backend/UI/DB changes or actual quote API. Original prototype-required flags are retained as evidence, not blindly enforced for conditional children.

NEXT finish exact364control dispositions (164question definitions are only part of that inventory), reconcile overlapping prototype/funnel answers and complete product requiredness/dynamic option selectors/source bounds; then quote OpenAPI and real .NET enforcement parity.05-01 remains in progress with no phase/requirement acceptance. Generator order: policy contracts, source reference data, combined question catalogue, validate-contracts.


## Complete candidate control ownership — 2026-09-15

Added generate-quote-control-ownership.mjs, contracts/quote-control-ownership.json and docs/design/QUOTE-SOURCE-COVERAGE.md. All364candidate controls retain source identity and explicit feature/product ownership:183Phase5,166Phase8,14Phase12 and1unavailableFleet. Product-stage evidence determines CC branches; location/wage/loss modal controls remain Phase8 and premises details are Combined-only. Global dashboard/search/report controls and advanced-search entry points remain Phase12. Audit metadata mirrors the reviewed dispositions.

Operation dependencies are separate from feature ownership: shared quote/policy rows retain Phase6policy and Phase7history dependencies; rateQuote stays Phase6 while save/validate belong to Phase5. Capture product-version reads are a Phase5 requirement, distinct from Phase11administration. Prior client/agency reads remain prior-phase dependencies. Fleet has no active Phase5operation. Historical source field bindings are preserved but are not certified as current capture-write paths.

Four tests cover all364source identities and operation dependency lists, CC/modal ownership, unavailableFleet/global controls, and shared capture-versus-rating/policy branches. Full126contract/design tests/OpenAPI/949controls328operations pass. No backend/UI changes, DB effects or phase acceptance. NEXT resolve overlapping answers, product-required fields/dynamic selectors/source limits and exact prototype field bindings, then complete quote OpenAPI and composed valid fictional proposals.05-01 remains in progress.


## Trusted dynamic option context — 2026-09-15

Added quote-dynamic-options.mjs to derive context for the three dynamic source reference questions from the pinned catalogue and current proposal, never from caller-supplied labels/metadata. Own-indemnity excesses resolve to the selected parent family; Third Party Fire & Theft excludes own limits above15000 and Third Party Only marks a retained excess inactive. Young-driver CC/indemnity collections use each driver's age at the local policy start, calendar anniversaries with leap-day clamping and the source age bands. Retained age-based choices at25or older are inactive.

Indemnity selection also checks the pinned numeric policy limit. Customer-vehicle limits contribute only when a trusted declared activity has customerLOI, and complete activity/cover/limit context is required before resolving indemnity. Unknown, missing or forged selections do not become zero/default limits. Returned instance-specific collection context and issues must both be consumed by the future boundary; reference membership alone does not override an eligibility issue. This slice validates present dynamic answers; missing-answer requiredness remains part of the full product readiness catalogue.

Six new tests cover ages/leap boundaries, trusted own-excess parents, fire/theft and third-party applicability, separate driver bands/age25, policy-limit bounds, missing activity dependencies and customer-limit eligibility/type rejection. Full132contract/design tests/OpenAPI/949controls328operations pass. No actual .NET quote endpoint/UI/DB changes; baseline runtime tests not rerun. NEXT complete overlapping-answer reconciliation, full product requiredness/source limits/canonical prototype bindings and quote OpenAPI/composed fixtures;05-01remains in progress.


## Overlapping cover declaration reconciliation — 2026-09-15

Added quote-cover-reconciliation.mjs for four common cover facts: cover level, own-vehicle limit, customer-vehicle limit and excess. Prototype ordinal selections are mapped explicitly to business meanings; bundled-source selections resolve via pinned metadata. Equal meanings reconcile even when IDs differ; conflicts produce issues on both declared fields and no resolved fact. Unresolved/forged references cannot be silently replaced by another declaration or an assumed zero. Original proposal selections remain unchanged.

Source inspection confirmed an important mismatch: prototype limit ID2means10000while bundled own-indemnity ID2means2500. Some prototype limits have no equivalent bundled option (for example customer7500); the helper preserves that business fact without inventing an option ID or claiming eligibility. Complete product configuration and the composed readiness pipeline still need to decide eligibility and integrate resolved facts with dynamic source selectors. Other overlapping declarations remain to be reconciled.

Four tests cover every prototype cover ordinal meaning, equal meanings/different IDs, same-ID/different-meaning conflicts, missing/type/label/unknown collection rejection and input preservation. Full136contract/design tests/OpenAPI/949controls328operations pass after guarding non-array inherited collection names. No .NET/API/UI/DB effects. NEXT remaining overlap/requiredness/source bounds/canonical prototype bindings plus quote OpenAPI and composed fixtures;05-01remains in progress.


## Core quote API contract slice — 2026-09-15

Added openapi-quotes.mjs as a final generator extension. Core create/save/withdraw/clone operations now use strict capture DTOs and ID-only receipts with existing CSRF, idempotency, ETag and Location conventions. QuoteCaptureProposal references the capture-only draft schema, not PolicyProposal. Reads expose a dedicated captured quote/revision/readiness shape with trusted scope/version metadata. Added listQuoteProducts (required relationshipId) and getQuoteRevision (quote/revision path IDs); total operations330. validateQuote retains its reviewed operation ID but becomes GET/readiness, removing the obsolete POST/validate contract. The existing /compare route is retained with both revision IDs required. Runtime status remains planned-phase-05.

Tests cover incomplete create/save versus forbidden policy ownership/premium fields, identity-only receipts and write protections, scoped product/revision reads and uncached read-only readiness. Existing filter traceability caught state versus status query drift; corrected to the source status name. Review also caught shared error-response object mutation while adding no-store headers; clone response objects so unrelated endpoints remain unchanged. All139contract/design tests/OpenAPI/949controls330operations pass after regeneration. No actual API routes or backend/UI changes; runtime baselines unchanged.

NEXT finish lookup/evidence/comparison API details and complete readiness/overlap/source-limit contracts with composed valid fixtures. Core OpenAPI work does not complete05-01; no phase/requirement acceptance claimed.


## Quote lookup and evidence API contracts — 2026-09-15

Added openapi-quote-support.mjs, invoked by the quote contract extension. Address/vehicle/licence requests have discriminated target/query shapes, current revision/fingerprint and an explicit deterministic-demo scenario allowlist. Candidate selection identifies a persisted lookup/candidate; manual entry has typed values, target and mandatory reason with optional prior lookup ID. No arbitrary provider URL, result JSON or premium fields are accepted. Lookup reads bind quote/revision/fingerprint, distinguish pending/succeeded/no-match/failed and require same-kind nonempty candidates only for succeeded outcomes.

Added quote-owned evidence-file upload/list/download contracts (PDF/PNG/JPEG/text,10MiB maximum, safe download headers). Evidence attestation binds file/revision/requirement/fingerprint and optional risk-item identity; forged verification/status/storage paths are forbidden. Reused existing list/attach/withdrawQuoteEvidence operation IDs with the new scoped shapes. Writes retain quote ETag/CSRF/idempotency and return only IDs; evidence withdrawal explicitly uses the evidence ETag while holding owning quote authority. Scoped file/lookup responses inherit no-store/planned-phase05 metadata. Total336operations.

Four new API tests cover wrong lookup targets/provider URL injection, manual/candidate discrimination, pending/failed or wrong-kind candidate rejection, evidence identity and upload/write/download controls. Updated the older evidence test to distinguish quote-owned fileId from later policy-draft documentVersionId. Strict Ajv initially caught missing array types in candidate-state conditionals; fixed and reran. Full143contract/design tests/OpenAPI/949controls336operations pass. No actual quote runtime endpoints, uploads/provider calls, DB or UI changes. NEXT comparison contract details and complete readiness/overlap/source-limit/prototype-binding composition before05-01completion.
