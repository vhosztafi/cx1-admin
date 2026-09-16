# Phase 6 typed demo rules and API examples

Planning contract, 2026-09-16. All prices and underwriting limits below are fictional configuration for repeatable demonstration. They are not production insurance rules. Source values take priority where explicitly identified; new assumptions are named. No live provider or legal/tax assertion is made.

## Closed configuration and input mapping

Schema version 1 uses closed objects. RatingRuleVersion carries productCode (motor-trade-road-risks or motor-trade-combined), version, basePremium, minimumPremium, driverAdditionalPremium, vehicleAdditionalPremium, stockRateBps, premisesPremium, claimsLoadingBps, taxRateBps, fee, quoteValidityDays and maximumAnnualPremium. Money is nonnegative GBP decimal strings with exactly two places and bounded decimal(19,2); rates are integers 0..10000; validity is integer 1..365. Reject missing/unknown fields, unsupported schema versions and nonfinite/overflow values before publication/use.

Resolve semantic inputs from the existing typed Phase 5 proposal: product, local term dates, active unique drivers, active insured vehicles, Combined stock limit, premises and reported claims. 06-01 must publish an exact JSON-pointer map against current schemas; do not approximate a missing field with zero. Price input excludes names, contact/support flags, screening data and document bytes; it includes only validated underwriting risk and retained ownership/version references. Collections sort by stable IDs before hashing. Limits/claims data must not be inferred from display strings. No hidden default on an unrecognised product/cover/trade value.

| Setting | Road Risks demo v1 | Combined demo v1 |
|---|---:|---:|
| basePremium | 600.00 | 800.00 |
| minimumPremium | 600.00 | 800.00 |
| driverAdditionalPremium (after first) | 50.00 | 50.00 |
| vehicleAdditionalPremium (after first) | 25.00 | 25.00 |
| stockRateBps | 0 | 20 |
| premisesPremium | 0.00 | 100.00 |
| claimsLoadingBps (any declared claim) | 1000 | 1000 |
| taxRateBps (fictional demo assumption) | 1200 | 1200 |
| fee | 35.00 | 35.00 |
| quoteValidityDays (prototype) | 14 | 14 |
| maximumAnnualPremium | 1000000.00 | 1000000.00 |

Formula: round components to pennies, halves away from zero. Base + max(0, drivers-1)*driver amount + max(0, vehicles-1)*vehicle amount + rounded applicable stock limit*stockRateBps/10000 + applicable premises component. Multiply subtotal by (10000+claimsLoadingBps)/10000 only when a claim is declared; round once, then enforce minimum. Both existing annual and short-period termIntent variants are supported. For annual terms charge the annual premium. For short-period terms multiply the minimum-adjusted annual premium by local civil duration divided by local civil duration to the start anniversary (365 or366 days), round the premium once, then compute tax and commission; fee applies once. Civil duration uses decimal local DateTime ticks/TimeSpan.TicksPerDay after existing London gap/fold resolution, so DST does not add a premium day and partial days are explicit. Retain annual premium, exact fraction and term premium separately; authority premium-limit uses annual premium. A180-day term from2026-01-01 with annual1200.00 produces term591.78, tax71.01, fee35.00 and gross697.79. Use checked decimal arithmetic; match the existing design-rules financial contract that short-period end cannot exceed the start anniversary. A captured longer draft must show an explicit term-eligibility blocker and require revision, not silently price as annual. This restriction is sourced from tests/policy-contracts.test.mjs and must be reconciled by06-01 fixtures. Tax=round(premium*taxRateBps/10000); fee separate; commission/fee share always from applicable independently approved agency terms, never duplicated in rating configuration. Preserve each factor, exact input and applied rule in result JSON.

Worked fixtures: Road Risks one driver/vehicle/no claim => premium600.00, tax72.00, fee35.00, gross707.00. Combined one driver/vehicle, stock150000.00, premises, no claim => premium1200.00, tax144.00, fee35.00, gross1379.00. Same Combined with claim => premium1320.00, tax158.40, gross1513.40. Financial posting fixtures separately exercise 10% commission and20% fee share using approved FINANCIAL-EXAMPLES. They do not silently replace existing agency terms.

## Authority and referral catalog

All authority predicates are conjunctive. Each actor grant lists every applicable dimension; an absent, null, malformed or unknown dimension denies that decision. Read/capture/rate grants are distinct from approve/issue grants. System-admin and servicing have no implicit approve/issue authority. Effective intervals are half-open; binder covers the entire policy interval and provider/product must be active/published. A binder hard exclusion cannot be overridden by any internal grant.

| Rule code / dimension | Default demo boundary | Referral/decision behavior |
|---|---|---|
| premium-limit | UW2500.00; senior5000.00; binder5000.00 | Compare annual GWP inclusive; above actor but within binder needs higher authority; above binder needs explicit provider extension |
| stock-limit | UW100000.00; senior125000.00; binder125000.00 (source) | Combined150000.00 produces separate stock referral. Only matching provider extension can cover excess |
| vehicle-limit | UW35000.00; senior50000.00; binder50000.00 (source) | Maximum declared vehicle value, never aggregated premium substitute |
| trade-restriction | no salvage/breaking under base binder (source) | Closed allowed trade set derived from proposal options; excluded trade needs supported carrier extension or explicit risk revision, never implicit approval |
| driver-age | minimum25, maximum75 inclusive (demo assumption) | Age at inception; one referral per offending stable driver; only grant explicitly permitting that range can approve |
| licence-years | minimum2 (demo assumption) | One driver-targeted referral; unknown/inconsistent chronology blocks rating, known low experience refers |
| conviction-history | any current declared conviction (demo assumption) | One targeted review; grant boolean reviewConvictions must be true; retain actual typed facts |
| claims-history | any declared claim (demo assumption) | Price factor and independent referral; grant reviewClaims required |
| cover-restriction | each selected cover must be in versioned product/binder allowlist | Unknown cover blocks pricing; known outside authority refers independently; loss/damage/indemnity limits use typed per-cover map |
| driver-proof | current required licence/proof from Phase5 requirement catalog | Screening does not approve; latest exact-target UW review required |
| premises-security | Combined premises-security proof (source) | Current exact-purpose evidence and accepted review required |
| signed-statement | signed statement of fact for prepared terms (source) | Blocks send/accept/issue, not initial rating or terms preparation |

06-01 enumerates every current source trade, cover and evidence requirement into closed fixtures and pointer mappings. It must reject omitted dimensions rather than using this table as permission to drop source cases. Actor grants may allow wider driver review than standard UW but never exceed binder's typed absolute extent. No generic manager override. Seed at least one narrow and one broad internal authority profile; preserve existing identities/credentials and operator changes. Use dedicated new published demo product/rule versions, not rewriting historical draft definitions pinned by existing captures. Explicit refresh to a supported published version is tested and visible; repeat seed never resurrects retired/revoked configuration.

## Condition and endorsement catalog

| Code | Kind / typed payload | Resolution and contractual effect |
|---|---|---|
| provide-driver-proof | documentary {driverId, requirementCode} | Same-target screened evidence plus accepted UW review; changes assurance only |
| provide-premises-security | documentary {premisesId} | Same-premises proof plus review; changes assurance only |
| provide-signed-statement | documentary {termsVersionId, termsHash} | Exact prepared version proof plus review; no circular re-rating |
| overnight-security | warranty {premisesId, wordingVersion:"1"} | Closed demo wording “Vehicles kept at the declared secured premises overnight.” Explicit recorded acknowledgement with evidence; add applied endorsement and new TermsHash |
| named-drivers-only | warranty {driverIds, wordingVersion:"1"} | Nonempty unique current stable driver IDs; closed wording with rendered driver names; new TermsHash and fresh acceptance |
| revise-stock-limit | risk-change {maximumAmount} | No direct premium/limit patch. Return to draft, capture changed stock, rate new cycle; old condition remains historical |
| revise-vehicle-limit | risk-change {vehicleId, maximumAmount} | Same explicit revise/new-cycle process |

Endorsements have stable code/version, rendered wording, risk targets, source decision and applicability. Unsupported arbitrary descriptions/effect JSON are rejected. Request-information outcome uses a required question and typed documentary condition. Reopen requires reason/current authority, preserves prior decisions and invalidates assurance; it cannot reopen bound policy. Conditions attached to carrier approval use this same typed catalog.

## Capacity outcomes and deterministic scenarios

Closed outcomes: approve, approve-with-conditions, query, decline. Submission includes exact referral/cycle/pricing hash and requested dimension limits. Approval must return an explicit typed authorisedLimits subset for the referred dimension, exact submission hash, validity interval, provider reference and named demo underwriter. It does not waive unrelated proof/other referrals. Extended limit must cover requested value and term; source binder stays unchanged. A manual supplied response additionally requires actual attached evidence/current recording authority. UI and audit distinguish supplied response from deterministic demo provider result.

Persist scenarios approve-stock-150000, conditional-security, query-proof, decline-trade, transient-then-approve and conflicting-duplicate. Each uses stable event identity and immutable request/response/attempt. Timeout/crash-after-success and stale completion are fault injection tests, not extra live providers. Provider extension applies only to the specified quote/cycle/submission; never modifies an actor grant globally.

## Exact command examples and constraints

Common strict request envelope: strong quote If-Match, Idempotency-Key and CSRF header; quote identity comes from route, actor from current session. Child ETags appear inside commands and do not replace quote ETag. IDs are UUIDs; hashes lowercase64hex; UTC times carry offset. Reason1..2000 trimmed; free correspondence1..8000; names1..200; provider reference1..100. Unknown keys rejected. Nonempty unique selected IDs at most50. Payloads bounded by existing application limits; file routes reuse existing byte/type bounds. Response includes resulting quote ETag separately from child ETags. Runtime capability remains unavailable until owning handler is implemented.

```json
{"cycleId":"11111111-1111-4111-8111-111111111111","decisions":[{"referralId":"22222222-2222-4222-8222-222222222222","etag":"\"AAAAAAAAAAE=\"","outcome":"approve-with-conditions","reason":"Reviewed the premises evidence","conditions":[{"code":"overnight-security","premisesId":"33333333-3333-4333-8333-333333333333","wordingVersion":"1"}]}]}
```

RateRequest: {revisionId, reason}; immutable current server config/terms chosen under lock, no client premium/config override. PrepareTermsRequest: {cycleId, ratingId, templateVersionId}; template must be active typed demo template. SendTermsRequest: {termsVersionId, recipientContactIds}; current same-relationship contacts only. AcceptanceRequest: {cycleId,ratingId,termsVersionId,termsHash,assuranceHash,accepterLabel,acceptedAt,channel,evidenceAssociationId}; channel=email|written|telephone, with actual supporting evidence for each. Reject future time, before completed delivery, expired rating and foreign/withdrawn proof.

Acceptance evidence association must exist and be reviewed BEFORE computing/storing acceptance; creating the acceptance itself does not alter AssuranceHash. Reusing signed statement as acceptance evidence requires explicit current evidence of acceptance, not an inferred customer decision. Change to proof/review afterward changes assurance and requires a new acceptance, even when TermsHash is unchanged.

IssueRequest: {cycleId,ratingId,acceptanceId,termsHash,assuranceHash,reason}; output includes policyId,policyReference,termId,versionId,transactionId,obligationId,documentRequestIds and quote ETag. No requested reference, posted total, debtor or issue status accepted from client. Problem errors:401 expired identity;404 unknown/foreign resource;403 current permission denied;412 stale quote/child version;409 obsolete context/already issued conflicting command;422 field/business blockers;503 durable retry unavailable. A successful identical receipt replays only after current auth; a fresh different-key issue of an already-bound quote returns existing identity or consistent409 without extra effects.

## Schema and migration gates

Five bounded schema slices: configuration/cycle/rating/referral shell in06-02; decisions/evidence/conditions in06-05; capacity in06-07; terms/delivery/acceptance in06-08; policy/financial/document requests in06-10. EF creates timestamped migrations and updates BackOfficeDbContextModelSnapshot. SQL checks enforce nonempty intervals, valid JSON, amount ranges and current-pointer ownership. Immutable domain records are protected by existing write guards plus direct-SQL constraint/trigger pattern selected in06-02; application-only promises are insufficient. Financial balance is checked inside the same held transaction before commit; append-only posted journals cannot acquire later lines. Composite unique/FKs prevent cross-cycle/current-child pointers. At most one first issue per source quote independently of idempotency key.

For each group run isolated fresh-SQL migration/constraint tests and then `dotnet run --project backend/src/BackOffice.Api --no-restore -- --initialize-demo` against the preserved CoverMGA_Demo before browser verification. Capture applied migration IDs and before/after retained-row counts. No database reset, destructive push or unreviewed operator data rewrite. Planned endpoints/forms use exact generated schemas produced in06-01; schema examples here are a design gate, not claims that the public OpenAPI has already changed.


## Source-to-runtime input gate findings (06-01 preflight)

06-INPUT-MAP.json binds19semantic inputs,73actual reference bindings,86source question rows and8trusted option collections to the committed capture contracts.18paths exist; risk.losses does not exist in the current ready proposal and MUST NOT be read as an empty invented array. Read actual named-driver losses and insurance/claims declarations. Driver basis is answered through MTS-06-Q01, with Q02 positive additional any-driver count and Q03/Q04 permitted ages; current ready examples do not contain driverBasis. Mixed basis means unique named drivers plus declared additional drivers. Vehicle count can be zero when unspecified; no invented first vehicle. Select limits using pinned collection+value numericValue metadata, never by parsing a label or treating the option ID as money.

The source quote's displayed cover/rating/referral tables add obligations beyond the source button inventory:

- UW-22: business trading fewer than five completed years at inception. Use risk.business.startedOn; separate reviewTradingHistory authority and provide-trading-history documentary condition with actual CV/trading evidence. A missing or future start is invalid input, not automatic approval.
- UW-09: valeting of requested/declared vehicles above50000. Evaluate current activity share and maximum actual vehicle/customer/own requested indemnity; it is independent of GWP. Preserve source rule code and reason.
- Source rating factors: versioned valetingLoadingBps1200, youngDriverLoadingBps1800 for youngest named/permitted any driver below25, and noClaimsDiscountBps800 for eligible at-least5claims-free years. Resolve current contradiction-free declarations first; proof remains an assurance blocker. Compute each of these three adjustments independently on the pre-loading subtotal, rounded once per component; add to subtotal together with the separate claims loading, then minimum and term proration. No claims discount when a declared claim invalidates eligibility. Do not replace at-least5 with an invented exact5.
- W-07: explicit versioned minimum-security warranty in applied endorsements; overnight-security is its typed demonstration condition, with wording and proof retained. This source code must appear in the UI and issued terms, not disappear behind an unrelated label.
- Tools/equipment: source5000.00limit/250.00excess and212.58demo component. Add toolsPremium212.58 to versioned rule config; charge only selected tools section. Explicit limits remain separate authority checks.

### Missing requested sections must be captured, not fabricated

Current quote-ready Cover contains responses and European cover but no stock/custody, premises-section or tools/equipment requested sums. Existing issued-policy Cover.sections is system-owned; do NOT accept it as user-authored issued cover. Extend quote proposal with optional typed `cover.requestedSections`: closed discriminated items for stock-custody, premises and tools-equipment. Fields: stable id, code, selected; selected items require positive decimal limit and nonnegative excess, stock also anyOneVehicleLimit, premises also nonempty unique current premisesIds. Unselected items reject amounts/target IDs. Each kind occurs at most once. Combined permits all three; RoadRisks permits tools only. Existing road-risk cover continues to derive from trusted MTS-05 answers and must not have a competing second input.

Existing historical drafts remain readable/capture-ready under their retained configuration; missing section declarations block progression when the new published underwriting version requires them, with field-specific action.06-03 validates and persists them through ordinary immutable revision/save/clone/matching rules;06-04 adds source-aligned requested-section controls to Cover and review, including explicit Not selected choices. No existing record is silently defaulted to100000/150000 or inferred from vehicle register sum.06-01 owns additive generated draft/ready contracts, positive/negative request examples and exact projection to issued system-owned sections.06-02 owns pure selected-section/target/duplicate/amount tests.06-05 owns final applied endorsements/warranties. Existing sales funnel stays unchanged.

Original baseline worked prices assume no valeting/young-driver/discount/tools adjustment and explicit relevant section selection. Additional fixtures must independently cover each source factor, business-age referral, mixed/any-driver basis and each requested-section kind before06-01 can complete. These are source-completeness corrections within approved scope, not deferred enhancements.
