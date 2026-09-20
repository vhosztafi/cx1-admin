# Commercial Combined data and API contract

Version: commercial-combined-capture-1 / issued-commercial-1. Status: Phase08-01 contract implementation; operational API and UI activation follow the owning plans. This document does not claim CC can yet be quoted or issued.

## Product boundary

The existing Motor Trade draft/issued schemas and format issued-quote-1 remain unchanged. New CC schemas are separate closed branches, selected by trusted product identity. They are not permissive alternatives attached to the live MT API until08-02 implements capture and08-09 implements issue. No driver, vehicle, NCD, MID or motor-trader-proof dependency belongs in CC. The source wage category “Drivers” describes employees and remains valid; it is not a motor driver schedule.

`scripts/generate-commercial-contracts.mjs` generates the CC schema definitions and two-location draft fixture. It is called by the policy contract generator. `scripts/openapi-commercial-combined.mjs` installs prefixed CC components and three explicitly pending exposure routes. The existing operational proposal schemas are not broadened during this prerequisite plan.

## Capture storage

| Object | Contract |
|---|---|
| Root |schemaVersion1.0, format commercial-combined-capture-1, productCode commercial-combined; optional incomplete insured/termIntent/risk/cover. The schemaVersion matches the existing QuoteRevision_Versions storage guard. No actor, policy identity, premium, capacity, district total or result fields. |
| Insured |Entity, names, company number, correspondence address/contact. Party/client/agency IDs remain the scoped quote envelope. |
| Term intent |Existing London local dates/times and explicit fold offset choices; resolve chronology/DST in the shared term rules. |
| Business |Description, start date, turnover, VAT declaration, stable activities and percentage basis points; no caller-computed wage total. |
| Locations |Stable ID, reference/address, one of the seven source occupancy values, SI components, supplied loss estimate, declared demo flood zone, sprinklers and scoped construction/protection responses. Normalize postcode/district server-side; district is not a writable field. |
| Wages |Stable ID, one of eight exact source categories, employee/LOSC/BFSC amounts, notes and scoped headcount response. |
| Losses |Stable ID, source type, optional same-proposal location, date, declared amount, optional paid/reserve, source status and circumstances/insured response. Repudiated remains the source status, not a renamed decline. |
| Liability |EL/PL/products limits, ERN, maximum height and hot-work procedures. EL selection is the sole question prototype.quote.36ef01068295; do not store a second contradictory selected flag. |
| BI |Basis, SI, indemnity months, declaration-linked flag and named supplier/customer dependencies. BI selection is the sole question prototype.quote.7660fc5eb42e. |
| Cover |Scoped source responses and optional contractWorks selected/SI/excess. Issued sections/wording are derived by the issue service, never caller-supplied. |

Every item has a stable UUID before persistence. Reject duplicate IDs across location/wage/loss/activity/dependency collections, including different casing. Loss locations must exist in the same held proposal. A removed location with referenced losses requires explicit reassignment/removal; never silently orphan the loss. Duplicate question IDs within one response set are invalid even if their values differ. Identical question IDs across distinct locations are legitimate.

Optional omission means unanswered or cleared, not false or zero. PUT proposal replaces the submitted proposal at the exact ETag; absent optional fields are cleared, empty arrays remove their items, and null is invalid unless explicitly defined. Save may retain incomplete business readiness but rejects structural/reference/ownership errors. Readiness reports subject and field paths; it does not silently repair inputs.

QuestionSetVersion is commercial-questions-1; references pin commercial-reference-1. The109 source question IDs remain stable and are closed by owning response container, kind and exact option value/label/version. All source choices are preserved. General source occupancy, eight wage categories and12 loss types are also closed. Trusted publication/currentness checks remain mandatory in .NET; syntactically valid reference data is not proof of current configuration.

The source ledger records seven supplementary policy display facts absent from the109 questions: VAT, contract works, hot-work procedures, BI declaration linkage, named BI dependencies, sprinklers and declared flood zone. These extend capture without changing the original109 denominator. The declared zone is a demo underwriting input, not a claimed flood lookup.

## Semantic and readiness rules

AJV validates closed structure, type, exact option contracts, currency precision and product separation. `scripts/commercial-combined-invariants.mjs` supplies executable cross-field examples after schema validation; it is not an HTTP authorization or complete underwriting engine.08-02 implements the full .NET readiness and reference boundary against the same cases.

- Normalize UK postcode syntax, including GIR0AA; derive outward district. This is syntax normalization, not address existence verification. Invalid syntax cannot enter an unknown aggregate bucket.
- Location SI must be positive when complete. `SI = buildings + contents + stock`; machinery/computers are a declared breakdown within contents, never extra SI. Reject a completed breakdown above total contents.
- `TSI = sum(locationSI)` and `demoMEL = max(locationSI)`. The supplied loss estimate is stored separately and cannot lower the authority basis.
- All percentage values use integer basis points. Completed activity percentages total10000; incomplete drafts remain editable. Overseas/USA-Canada percentages cannot jointly exceed10000.
- EL Yes requires applicable ERN, limit and positive employee/LOSC wages. EL No requires explicit clearing of EL-only details before readiness; it never causes an EL certificate. BI No similarly requires clearing inactive BI details. No hidden stale detail can become insured or rated.
- Contract works selected requires SI/excess; deselection clears its details. Supplier/customer dependencies have stable IDs and valid selected extension limits.
- Source readiness includes proposer names, sufficient business description, turnover, positive-SI locations, electrical inspection confirmation and consistent five-year losses. Unknown evidence/experience is not an inferred No or zero.
- Source referrals cover PR04/05/08/11/12/14/17/18/21, AU05/06, LI03/05/08/11/12/15/18, UW18/20/30. Both declared work flags and the wage categories for height/heat/away work inform the typed facts. Inconsistent answers require clarification rather than lowering the risk.

## Demo rating assumptions

The executable independent figures are in `contracts/examples/commercial-combined-rating.json`. These are fictional parameters; publication will pin them in08-05. No real insurer rate or production tax determination is asserted.

Annual premium starts with£250 plus component charges. Buildings5bps, contents12bps, stock20bps, selected BI10bps multiplied by indemnity period. Manual-on-premises employees25bps and LOSC35bps; BFSC is declared but not charged under this demo assumption. PL turnover4bps and products3bps at£5m limits. EL basis is£10m. Minimum£500; maximum£100,000. Fee£75 new business/renewal,£25 per MTA transaction. Tax1200bps and agency commission1500bps are demo configuration.

The two-location example produces£4,195 premium +£503.40 tax +£75 fee =£4,773.40 gross, commission£629.25, net agency due£4,144.15. With EL and BI deselected and details cleared:£2,770 premium,£3,177.40 gross. For an exact half-term annual change of+£200/£0/−£200, the movement is+£100/£0/−£100 premium; one£25 fee produces gross+£137/+£25/−£87. These examples independently fix signs and fee count; actual civil-term fractions use the existing shared London rules.

Apply rates per component with midpoint-away-from-zero rounding to pennies, then sum. Apply documented construction/flood/loss loadings once against the component subtotal, then minimum. Selected optional goods-in-transit, money, glass and contract works have explicit configured charges; BI extensions likewise require published limit/factor mappings before rating can proceed. Do not accept a source cover selection and silently leave it unpriced/uninsured. Configuration publication must fail if a supported option lacks its cover/rating/readiness disposition.

Per-category employee/LOSC basis-point pairs: clerical5/10; warehouse15/20; drivers20/25; woodworking30/40; height45/55; heat-away50/60; manual-on-premises25/35; manual-away35/45. Relevant high-risk categories also trigger the independent referral/authority gates. All supported category factors are published, not inferred from display labels.

## Exposure database projection

| Record | Keys and invariant |
|---|---|
| CommercialExposureBook |Stable product/provider book identity; all successive binder versions map explicitly to the same book. |
| CommercialExposureLimitVersion |Book, district/default scope, coverage-effective interval, exact limit and immutable publication provenance. Select one applicable published limit at each evaluated interval and pin ID/hash; missing/ambiguous selection blocks. |
| Binder/book mapping |Immutable binder-version to book association; no version publication can reset historical aggregate exposure. |
| CommercialExposureVersion |Unique issued version; compound policy/term/transaction/version linkage, book, source hash, effective/processed time, term bounds, transaction kind/sequence and slice ordinal. Cancellation and zero-property versions require headers too. |
| Exposure locations |Unique header/location; normalized district and exact property SI. Append-only source-derived data; never an alternative editable policy record. |

Use the existing temporal winner semantics for E/K, including transaction sequence and slice ordinal. Count only active winners; distinct policy count and every selected location's SI are separate totals. Scheduled future cover reserves its future interval, not today's. Replace the proposal's own complete prospective contribution and evaluate every affected term/version/proposal breakpoint. Backdated changes must check the whole affected known timeline. Drafts have no issued contribution.

Acquire one canonical transaction-owned exclusive CC exposure lock before quote/policy/draft row locks on all CC issue/MTA/renewal/cancellation and operational book-limit publication paths. Negative lock return means rollback/conflict. Current scope/grant precedes receipt disclosure; only new effects recheck capacity. An issued receipt replay must not fail because unrelated later business used headroom. Writer, immutable projection, balanced journal, outbox and receipt commit atomically.

The initial demo book ceiling is£40m per district, with£2.5m single-location and£2m largest-location proxy per-risk thresholds. The book ceiling is hard; a per-risk carrier response cannot enlarge it. Book queries use immutable projection metadata rather than locking unrelated policy rows.09's exactly-one concurrent-issue test,12's MTA/issue race and14's timed cancellation release are required.

## API surface and activation

All paths below are actual URLs; OpenAPI stores them relative to server `/api/v1`.

| Route | Ownership and contract |
|---|---|
|POST /api/v1/quotes|Existing create identity envelope;08-02 adds trusted CC capture product. Capability quote-capture;201, idempotency key. |
|GET /api/v1/quotes/{quoteId}|Existing current scoped read;08-02 adds discriminated CC proposal branch. No-store. |
|PUT /api/v1/quotes/{quoteId}/proposal|Existing save operation;08-02 activates CommercialCaptureDraft union after service implementation. Exact If-Match/key; no computed fields. |
|GET /api/v1/quotes/{quoteId}/readiness|Existing scoped readiness; typed CC field/subject blockers, no forged client outcome. |
|POST /api/v1/quotes/{quoteId}/rate|Existing quote-rate capability and durable job;08-05 selects CC rules. |
|POST /api/v1/quotes/{quoteId}/issue|Existing issue command;08-09 applies current acceptance/authority and atomic capacity check. |
|PUT /api/v1/drafts/{draftId}/proposal|Existing policy-draft-write, If-Match/key/lease;08-11 adds CC proposal union. |
|GET /api/v1/quotes/{quoteId}/commercial-exposure|getCommercialQuoteExposure; quote-read parent scope;08-10 activation. |
|GET /api/v1/drafts/{draftId}/commercial-exposure|getCommercialDraftExposure; policy-read parent scope;08-10 activation. |
|GET /api/v1/policies/{policyId}/commercial-exposure|getCommercialPolicyExposure; policy-read parent scope;08-10 activation. |

Exposure GET query: optional effectiveAt and knownAt instants; bound valid dates and reject future knowledge beyond the service's observed time.200 returns closed CommercialCaptureExposure, advisory=true and explicit observation/cutoffs. Internal whole-book fields additionally require internal actor plus underwriting-read; agency DTO exposes only own proposed SI and outcome. No policy IDs, location IDs, foreign addresses or list-filtered book totals.401/403/404/400 follow current conventions;503 represents unavailable configuration/calculation rather than fabricated zero capacity. All responses no-store. New issue capacity conflict is409 with a safe reason and effective interval, not a list of competing risks.

New mutation schema members follow existing400 malformed/401 unauthenticated/403 visible forbidden/404 non-disclosing foreign/409 domain conflict/412 stale/422 semantic/428 precondition/429 throttling/503 transient conventions. Current authority is evaluated before replay. Keep exact request/key/ETag/lease for uncertain-response retry.

## Issued risk and operational payloads

`commercial-combined-issued.schema.json` defines issued-commercial-1 with existing shared insured identity, term, premium/settlement and provenance envelopes, plus distinct CC risk/cover. It does not relax issued-policy.schema.json or old MT snapshots. Full readiness and exact selected-cover reconciliation precede snapshot creation; schema validity alone does not authorize issue.

Product schedule/statement and selected EL certificate requests pin exact version/template/content hash. Wording uses the shared code/version/text shape. Cancellation/MTA/renewal reuse this product selection. A queued request is not a generated/downloadable/delivered document.

CommercialCaptureIncidentPayload defines property location/damage or liability section/employee occupation/third-party context, occurrence time, policy/version and source hash. Phase08-15 validates it against immutable risk and coverage. Phase9 owns incident persistence/status/TPA handoff and generic rendering/delivery. CC-05 remains partial until that workflow passes. Finance cash balances remain Phase10; opening charges and issued credit obligations must not be labelled collected/refunded.

## Verification ownership

### Implemented capture boundary (08-02)

The shared quote create, save, get, readiness, revision and discovery routes now accept the explicit CommercialCaptureDraft branch. `QuoteCaptureProposal` is a closed union of the retained Motor Trade draft schema and the CC draft. Product selection and discovery include `commercial-combined`; the selected published product and matching question/reference family are checked inside the held command transaction. Neither a client-supplied product code nor valid JSON grants capture access.

`CommercialCaptureRules.Prepare(string?, QuoteVersionPins)` returns the canonical proposal, retained term intent and an empty motor registration projection. `Validate(string, QuoteVersionPins)` enforces the bundled schema and rejects empty/duplicate item IDs, duplicate question IDs, foreign loss-location links and invalid UK location postcodes. IDs are compared as GUIDs. `NormalizePostcode(string)` returns a normalized postcode and district for later exposure use; capture preserves the declared string rather than silently rewriting a saved answer. The existing one-MiB, depth, duplicate-property, canonical hash and version-pin rules are retained.

`CommercialCaptureReadiness.Assess(JsonElement, DateOnly)` handles incomplete commercial facts independently of Motor Trade. Every one of the109 source questions retains its owning proposal/item container. Present item rows require their scoped questions; no empty collection invents item answers. BI extension questions depend on BI selection, subsidence excess on selected subsidence, tenant/distance details on location occupancy answers, interested-party details on the mortgage answer, and adverse-history details on their source answers. Health/safety dropdown No, outstanding actions, unenforced PPE and outstanding inspector recommendations require details using their exact published option values. Previous insurer is optional because the source has no prior-insurance selector. No current answer is inferred from omission. These are initial demo capture rules; referral and proof decisions remain separate.

The first incomplete proposal can save; readiness additionally checks proposer/company/contact details, business start/activities, location values/protections, selected EL/BI/contract works, wages and loss history. Positive location SI,100% activity allocation, contents breakdown and retained deselected-cover conflicts are explicit blockers. Whole-book aggregation is not calculated or accepted by this boundary.

`CommercialCaptureSeed.SeedAsync(BackOfficeDbContext, CancellationToken)` requires the existing initialization transaction. It publishes CC product v2 with bundled `commercial-questions-1` / `commercial-reference-1`, schema1.0 and capture format1, then appends capture/distribution settings only when current valid nonempty capture and existing CC distribution permit the initial addition. The `commercial-capture-initialized` marker prevents later re-grants. Existing product/setting/agency-term versions are never rewritten and no agency product permission is added. The full demo initializer enables this seed; isolated earlier-phase fixtures can leave `includeCommercialCapture` false. No migration or new persistence table is required: immutable QuoteRevision and existing receipt/scope constraints store the CC JSON.

Until08-05/06, underwriting returns an unavailable-product blocker and `canRate=false`; the quote view exposes `canAttachEvidence=false` for CC. This does not change the retained Motor Trade flags. The frontend capture stages are the08-03/04 owners, so API tests are not evidence of browser completion. No demonstration database was reset or reseeded during08-02 verification.

08-01 tests prove closed schemas, exact source identities, selected cross-field invariants, privacy DTOs, old MT schema preservation and independent golden money examples. They do not prove runtime CC readiness, SQL concurrency, delivery or business UAT. Those are explicit later plans, ending in08-16 current-source full regression and actual restart/preservation. No frontend-code change or demo reset is authorized by this contract.

### Business and loss capture UI (08-03)

Quote creation and the existing quote/view/edit routes dispatch explicitly to `CommercialWizard` and `CommercialReceipt` for Commercial Combined. `QuoteView<TProposal>` retains the Motor Trade default type; the shared create/save command builders accept the explicit CC proposal union without changing receipt, ETag or idempotency handling. The shared history and lifecycle actions depend on quote identity/capabilities rather than a Motor Trade proposal shape.

`commercial-capture.ts` provides immutable field and stable-row editing, ownership-aware loss links and source-scoped question responses. `CommercialCatalogue` contains the exact published question/reference labels and closed options. `CommercialBusinessStage` captures proposer names, entity/address/contact details, business history/declarations, turnover/VAT and stable activities. `CommercialLossStage` captures loss date/type/optional location, amount/paid/reserve/status/circumstances and its scoped insurance declaration. Dialog edits are applied explicitly; saving creates the persistent revision. Empty, explicit No and zero remain distinct. Numeric text that is invalid remains visible and blocks saving; removing its owning row removes that obsolete validation state.

The source's fifth commercial proposer choice, `charity-or-trust`, is now included in the CC draft/issued insured schema. This is a commercial underwriting classification; it does not change the shared client-account legal entity contract or any Motor Trade snapshot. Loss readiness uses the actual persisted field `risk.losses[].occurredOn`, including the future-date check. Both corrections have regression tests. No new table or migration is required.

The CC editor retains the exact request body/key/ETag after an uncertain result, including an intervening denial. It rechecks the original actor and current quote access before displaying recovery. Stale saves retain the user's input, with read-only comparison and explicit discard/load. Saving and navigation do not request rating. Stages4–10 and positive location-selection interaction coverage are owned by08-04; proof and underwriting remain08-05/06. All ten stages are named, with unfinished stages disabled until their implementation.

### Complete capture stages (08-04)

All ten stages are now enabled. `CommercialLocationStage` and `CommercialItemDialog` store stable location identities, full addresses/occupancy, the15 scoped construction/protection questions, declared flood zone/sprinklers, buildings/contents/stock and supplied maximum-loss estimates. Construction/protection tables read the actual location answers. Property totals use integer pennies; an unanswered component makes the total incomplete, rather than silently adding zero. The largest complete location sum insured is labelled as the demo MEL proxy; it is not the supplied estimate or a whole-book capacity result.

`CommercialLiabilityStage` captures fixed source limits, ERN, numerical working height, hot-work procedures, employee/business declarations and stable wage categories with employee/LOSC/BFSC amounts, notes and scoped headcount. `CommercialCoverStage` captures property bases, contents breakdown, BI basis/sum/period/declaration and named supplier/customer dependencies, source extensions, contract works and excess/material-fact declarations. The source's fourth BI basis is stored distinctly as `estimated-gross-profit`. Shared client/MT contracts and historic snapshots are unchanged; there is no new table/migration.

`commercialQuestionApplies` mirrors the backend source conditions. Subsidence questions/excess follow explicit selected cover; flood-history reference choices2/3 require details, as do applicable Yes answers. BI extensions, interested-party/tenant/distance details and adverse business/health details follow their exact source controls. Hidden retained answers remain inspectable and explicitly clearable, including invalid buffered text. `clearCommercialSection` removes only the reviewed EL, BI or contract-works details; it does not select/deselect another section. EL clearing includes wages, its limit and ERN. BI clearing includes dependencies and its extension answers. The UI never defaults unanswered questions to No.

`commercialDecimalInput` accepts a finite0–1000 metre height with at most two decimal places; `commercialPostcodeInput` mirrors the server syntax while retaining declared casing/spacing. `commercialInputStage` routes invalid inputs to their actual owning stage. These are user feedback helpers; the closed server schema, stable-subject checks and authoritative readiness still apply to every save.

The full isolated browser acceptance command is `node scripts/verify-commercial-capture-browser.mjs` (or `--stage full`). The retained `--stage business-loss` mode remains available. Full acceptance checks every109 question ID and dropdown options, per-item API readback and SQL revision presence, two-location/wage editing/removal, owned/foreign loss links, explicit deselection/clearing, exact amounts, saved readiness=true, validation focus,390px reload and retained MT creation/editor. Proof, calculated premiums, book exposure and issue remain the separate later plans. The166 source-control denominator is preserved; downstream proof/rating controls retain their original owners and are not claimed operational by capture completion.

## 08-05 — Commercial rating and publication

`CommercialUnderwritingConfiguration` validates the closed `commercial-underwriting.schema.json` rating/binder/authority definitions. `generate-commercial-underwriting.mjs` generates that schema and `commercial-underwriting-demo.json`; normal contract generation includes both. Definitions pin the CC product/reference identity, version and effective interval. Every wage category and source cover selection has an explicit disposition. Goods-in-transit/money limits and glass selections match the original catalogue exactly; missing mappings cannot publish. BI bases, time excesses, property valuation/theft bases, index linking and other recorded cover options use the configured section rate without an additional charge in this fictional model. This does not waive their separate wording/referral/evidence requirements.

`CommercialRatingRules.Calculate` consumes typed `CommercialRatingFacts`, resolved London term, approved commission and optional approved minimum. `CommercialUnderwritingInput.Project` first revalidates the complete saved proposal, catalogue/stable identities and readiness, then projects actual locations, employee/LOSC/BFSC categories, selected BI/extensions/liability/optional cover and source construction/flood/loss loadings. No driver facts or caller premium overrides are accepted. All four BI bases use the same configured SI rate; indemnity factors are12:1.00,18:1.35,24:1.70,36:2.30. EL£5m uses0.90 and£10m1.00; the source PL/products limits have explicit factors. Named suppliers/customers require matching named dependency limits, aggregate capped at£250,000; their rate is12bps. Unspecified suppliers use£100,000 at15bps, denial of access£100,000 at10bps and loss of attraction£50,000 at10bps. These amounts and rates are published configuration, not insurer terms.

Component charges round individually, including each location/wage row; duration/limit multipliers apply before that component's penny rounding. Construction/flood/loss loadings apply once to the same rounded component subtotal, followed by the minimum. Shared London civil-day proration handles DST/leap/partial days. Tax and commission round separately; one£75 new-business fee is added. The published£25 adjustment/£75 renewal fees are reserved for the owning servicing plans. `RatingFactor.multiplierBasisPoints` is optional; omitted for legacy Motor Trade results so durable provider serialization remains compatible. The API factor schedule permits700 rows to cover100 locations and100 wage rows without truncation; prices remain canonical two-decimal strings.

Publication adds CC product v3 (`commercial-combined-underwriting`) and independent rating/binder/authority versions. Published v2 remains capture-only and is never silently upgraded. Approved agency terms must explicitly adopt v3. The missing-only `CommercialUnderwritingSeed` marker preserves withdrawal/retirement and existing definitions, settings, credentials and grants. It adds no approved agency terms or staff decision grant. `DemoDatabase.InitializeAsync` wires the new seed after its capture/shared dependencies; tests may opt in with `includeCommercialUnderwriting`. No initialization of the live demo was performed during this slice.

Migration `CommercialUnderwritingDefinitions` adds CC-specific JSON limit checks to existing rating/binder/authority tables, preserving the original MT checks and immutable version triggers. A narrow product-validity exception permits one compatible capture-only CC edition alongside its separately adopted underwriting edition. Two underwriting editions, unknown kinds or mismatched provider/capture pins cannot use this exception. Both CC product kinds acquire immutable-definition and final-retirement guards. No new business table is introduced; whole-book exposure remains08-08/09.

Existing POST `/quotes/{id}/rate`, rating jobs, GET underwriting/ratings/history and return-to-draft remain the public routes. The internal stored format `commercial-underwriting-input-1` contains its separate commercial projection and no Motor Trade input. Current scope precedes receipts; the worker rechecks revision, product/terms/configuration/runtime/scenario and requesting authority before applying a durable result. Stale completions remain history and grant no current rating. Rating readback exposes actual factors, expiry and version/hash provenance. `CommercialRatingSummary` uses those saved values, keeps current status synchronized and supports the shared exact-retry action dialog.

The rating assessment explicitly closes submission, evidence decisions, carrier/terms/acceptance and issue until their CC implementations in08-06/07/09. Legacy Motor Trade-only consumers reject the new stored format before reading product-specific facts. A valid price is not within-appetite or approval. The original source-control/referral owners are retained;05 introduces shared rating behavior and does not claim the06 decision branches completed. `--stage rating` extends the actual SQL/Chrome capture journey with rating, closed response-schema validation and desktop/390px persisted-price checks. Its validator accounts for binary floating-point division noise for valid decimal heights while still rejecting a third decimal place; server decimal validation remains exact.


## Commercial adjustment contract

Commercial adjustments use the existing `/drafts/{id}` lease/revision, rating, evidence, referral, carrier, terms, acceptance and issue routes. Their persisted cycle format is `commercial-servicing-rating-input-1`; each cumulative dated slice contains the typed commercial projection and no Motor Trade input. The complete commercial risk is priced annually, then shared servicing arithmetic calculates the signed change over the remaining London civil days. One GBP25 adjustment fee is pinned through an independent `commercial-servicing-rating` setting and must equal the published commercial rating definition. Tax and commission remain signed movements; posting uses the existing settlement obligations and balanced journal rules.

Proof purposes and numerical carrier extents retain commercial subject identities across every applicable slice. A carrier response does not replace internal approval, current proof, delivery or acceptance. Commercial templates are published independently and fictional delivery retains its own result. Live proof reviews, grants, carrier conditions and acceptance are reassessed before issue; only immutable proposal projections and parsed immutable carrier responses are cached inside one held decision context. Current response selection, expiry and condition proof are checked again on every evaluation.

Issued adjustments use the closed `issued-commercial-servicing-1` snapshot schema with servicing decision/revision/input provenance. Earlier snapshots are immutable. Each dated version receives its own exposure header and decision, schedule and statement; an employers liability certificate is requested only for a slice where that cover is selected. No commercial MID intent is created. The UI freezes the product identity with its issue retry and validates the corresponding document/MID receipt shape.
