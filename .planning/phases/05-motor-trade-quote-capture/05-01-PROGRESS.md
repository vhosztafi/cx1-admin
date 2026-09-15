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
