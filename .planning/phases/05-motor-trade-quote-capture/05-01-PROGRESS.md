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
