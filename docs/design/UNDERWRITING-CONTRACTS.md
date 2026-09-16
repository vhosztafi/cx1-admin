# Underwriting and first-issue contracts

Phase6 contract implementation. These generated contracts and fixtures do **not** enable runtime underwriting, issue, policy reads or new capture fields. Runtime remains closed until the owning service/API/UI plans pass. Both MotorTrade products are covered; Commercial Combined and servicing drafts keep their separate later-phase contracts. No live provider, message or payment is involved.

## Sources and generation

`scripts/underwriting-contract-model.mjs` contains closed schema constructors for versioned rating/binder/authority, condition unions and requested sections. `generate-underwriting-contracts.mjs` writes the config schema and eight fictional definitions with financial examples. `generate-quote-contracts.mjs` adds requested sections **after** partial-draft softening; `generate-policy-contracts.mjs` invokes that generator. `openapi-underwriting.mjs`, called after the existing quote module, replaces existing new-business operation contracts and adds missing operations while preserving their IDs and separate policy-draft contracts. `pnpm contracts:generate` runs the full generator sequence.

Authoritative JSON outputs: `contracts/openapi.json`, `contracts/schemas/underwriting-config.schema.json`, `quote-draft.schema.json`, `quote-ready.schema.json`, `contracts/examples/underwriting-demo.json`, `contracts/examples/underwriting-api.json` and updated `contracts/examples/api/core.json`. The request examples cover every new Phase6 JSON mutation. Multipart proof uploads reuse the existing10MiB file/type contract. No generated schema accepts system-owned price, actor, state or issued endorsement fields as arbitrary capture input.

## Ownership and exact versions

Quote is the new-business concurrency root. `UnderwritingContext` binds quote/cycle/revision, retained client/relationship, product, agency terms, rating rule, binder and authority version plus pricing input hash. Root UUID equality is required as well as hash equality: identical risk under a different owner/revision is a different contractual context.

- PricingInputHash covers canonical pricing-risk projection, context/config and resolved term. Exclude support flags, secrets and presentation labels. Rating view's full internal proposal is provenance for authorised staff; it is **not** permission to send all identity/contact fields to a provider.
- TermsHash covers rating, contractual cover, applied endorsements/conditions, commercial terms and template version. Documented proof completion alone must not trigger re-rating.
- AssuranceHash covers applicable proof/review, referral, condition and carrier decisions. Acceptance proof must already exist/reviewed before computing it; acceptance itself is excluded from that hash. Any later relevant proof/decision change requires fresh acceptance.

All mutations use strong owning **quote** If-Match, Idempotency-Key and CSRF. Child ETags are explicit request fields: referral decision `etag`, `associationEtag`, `conditionEtag` or `escalationEtag`. Current stored identity/agency/grants are checked before successful receipt replay. Repeated same key/body returns original identity only after current authorisation; mismatched body/key, stale root/child context and foreign ownership fail safely. `UnderwritingCommandResult` contains child identity, quote identity and quoteEtag; no sensitive decision snapshot is stored as a generic command receipt. Child detail ETags are obtained from scoped GET. Upload, requested-work and issue receipts use the same quote token convention.

Business semantics are not JSON Schema guarantees. UTC instants/UUID syntax, required fields, discriminators, unknown-key rejection and bounds are schema concerns. Current ownership, chronology, allowed transition, effective dates, all authority dimensions, condition applicability and binder extension must be checked under the held SQL transaction. Responses are no-store; failures use400/401/403/404/409/412/422/428/429/503 as applicable. Never reveal a foreign record through count, cursor, filename or error wording.

## Operation / DTO mapping

All paths below have `/api/v1` prefix. Generated schemas list exact property bounds, required arrays and optional fields. Each request below is closed.

| Operation / path | Request schema | Response / owning plan |
|---|---|---|
| rateQuote POST quotes/{quoteId}/rate | UnderwritingRateRequest: revisionId,reason |202 UnderwritingWorkResult;06-03 |
| submitQuote POST quotes/{quoteId}/submit | UnderwritingCycleRequest: cycleId,reason | UnderwritingCommandResult;06-03 |
| returnQuoteToDraft POST quotes/{quoteId}/return-to-draft | UnderwritingCycleRequest | Same; supersedes applicability, preserves history;06-03 |
| refreshQuoteUnderwritingVersion POST quotes/{quoteId}/underwriting/refresh | UnderwritingRefreshRequest: revisionId,productVersionId,confirmedTermsVersionId,reason | Same; explicit new immutable revision;06-03 |
| getQuoteUnderwriting GET quotes/{quoteId}/underwriting | None | UnderwritingAssessment: current context/state, assignment, blockers and action capabilities;06-03 |
| getRating GET ratings/{ratingId} | None | UnderwritingRatingView: exact input/factors/components/config/expiry;06-03 |
| decideQuoteReferrals POST quotes/{quoteId}/referral-decisions | UnderwritingDecisionRequest: cycleId, selected decisions | Atomic all-or-none receipt;06-05 |
| decideReferral POST referrals/{referralId}/decisions | UnderwritingSingleDecisionRequest | Same shared service; body referralId must equal route;06-05 |
| listReferrals / getReferral / listReferralDecisions | Scoped quoteId for list; cursor/pageSize for histories | Typed referral, decisions and conditions;06-05 |
| uploadUnderwritingEvidenceFile POST quotes/{quoteId}/underwriting/evidence-files | Reused bounded multipart fileName/contentType/file |201 identity receipt;06-05 |
| attachUnderwritingEvidence POST quotes/{quoteId}/underwriting/evidence | UnderwritingEvidenceAttachRequest: cycle,file,requirement,fingerprint,reason and applicable target/condition/terms |201 identity; actual same-quote bytes;06-05 |
| reviewUnderwritingEvidence / withdrawUnderwritingEvidence | UnderwritingEvidenceReviewRequest / UnderwritingEvidenceWithdrawRequest | Identity receipt; immutable review/withdrawal;06-05 |
| listUnderwritingEvidence / listUnderwritingEvidenceEvents | Scoped parent and paging | Screening distinct from review, exact fingerprints and actor/reason/time history;06-05 |
| resolveReferralCondition POST referrals/{referralId}/conditions/{conditionId}/resolutions | UnderwritingConditionResolutionRequest | Typed proof/outcome/reason;06-05 |
| createEscalation POST referrals/{referralId}/escalations | UnderwritingEscalationCreateRequest: cycle,referralEtag,provider,reason |201 identity;06-07 |
| sendEscalation POST escalations/{escalationId}/send | UnderwritingEscalationSendRequest: cycle,escalationEtag,body,evidence IDs |202 durable work;06-07 |
| recordCapacityResponse POST escalations/{escalationId}/responses | UnderwritingCapacityResponseRequest |201 identity; manual supplied proof/current recording authority;06-07 |
| getEscalation / listEscalationMessages | None / protected paging | Typed current context/correspondence/provenance;06-07 |
| prepareQuoteTerms POST quotes/{quoteId}/terms/prepare | UnderwritingPrepareTermsRequest: cycle,rating,template |201 immutable prepared payload;06-08 |
| sendQuoteTerms POST quotes/{quoteId}/terms | UnderwritingSendTermsRequest: termsVersionId, nonempty unique recipientContactIds |202 durable demo delivery;06-08 |
| listQuoteTerms GET quotes/{quoteId}/terms | Independent terms/deliveries/acceptances cursors; bounded pageSize | Typed immutable terms, actual delivery and acceptance history;06-08 |
| recordQuoteAcceptance POST quotes/{quoteId}/acceptances | UnderwritingAcceptanceRequest: cycle,rating,terms ID/hashes,accepter,time,channel,evidence |201 identity;06-08 |
| issueQuote POST quotes/{quoteId}/issue | UnderwritingIssueRequest: cycle,rating,acceptance,terms/assurance hashes,reason |201 UnderwritingIssueResult;06-11 |
| getPolicy GET policies/{policyId} | None | FirstPolicyView: exact snapshot/term/transaction, financial component/journal summary and document requests;06-11 |
| listPolicies / listPolicyTerms / listPolicyVersions / getPolicyVersion | Existing scoped routes; expanded discovery query | Actual policy identities, immutable versions;06-11/13 |

Quote capture summary/view and status-filter contracts now enumerate progressed states. That does not enable these states in existing SQL;06-02 migration and06-03 readers/commands must implement them. All newly replaced/added Phase6 operations carry `x-runtime-status: phase-6-pending`; remove/change it only when verified runtime supports the operation. Existing design-only operations elsewhere retain their own phase ownership.

## Source input map and requested cover

The reviewed `.planning/phases/06-underwriting-and-first-policy-issue/06-INPUT-MAP.json` records19semantic mappings,73source reference bindings,86source question rows and8trusted option collections. Its quote-ready hash is the pre-extension source baseline; direct schema-path tests validate current mappings. One intentionally missing path, risk.losses, is documented and never interpreted as an empty factual history. Actual driver losses and declarations remain authoritative.

| Meaning | Current source / interpretation |
|---|---|
| Term | `/termIntent`, annual or short-period; London gap/fold resolution, explicit offset when needed; anniversary cap from existing design financial rules |
| Business trading age / UW-22 | `/risk/business/startedOn`, completed years at inception; below5 requires reviewTradingHistory and actual proof |
| Trade / valeting / UW-09 | `/risk/business/activities`, stable ID, trusted mtOccupations value and turnoverBasisPoints; reconcile declaredActivitySplit; valeting and vehicle/customer-limit exposure above50000 independently refer |
| Named/mixed/any-driver | `/risk/responses` question MTS-06-Q01; Q02 positive additional any-driver count, Q03/Q04 allowed ages. No invented risk.driverBasis field |
| Actual drivers/vehicles | `/risk/drivers` and `/risk/vehicles`, stable IDs and typed dates/limits; preserve unspecified-vehicle intent rather than fabricating a vehicle |
| No claims | `/risk/previousInsurance/noClaimsYears` plus noClaimsYearsBasis exact/at-least; proof is an independent assurance requirement |
| Road cover / limits / excess | `/cover/responses`, MTS-05-Q01/02/03/04; use pinned collection/value numericValue and dependent excess catalog, never labels or numeric option IDs as amounts |
| Salvage | Business response prototype.quote.ba9d4158ae2c; explicit binder restriction independent of label and GWP |
| New requested sections | `/cover/requestedSections`, stock-custody/premises/tools-equipment. Stable id/code/selected. Selected requires positive limit/nonnegative excess; stock needs anyOneVehicleLimit; premises needs current unique premisesIds. Unselected rejects amounts/targets. Unique kind, up to3 |

RoadRisks permits tools only among the new requested sections; Combined permits all3. Existing road-cover answers remain the sole road-risk input. Missing requestedSections remains valid for historical capture; a newly adopted underwriting configuration requires explicit applicable selections before pricing. Duplicated IDs across different section kinds, foreign premises targets and contradictory declarations require semantic validation even if a JSON shape is valid. User fields never directly set issued cover.sections: server projection emits only selected, validated, approved sections, preserving IDs/limits/excess/targets in the contractual lineage. Endorsements and warranties derive from immutable decisions, not capture payloads.

## Fictional configuration and pricing

Closed config requires schemaVersion1, product, immutable version and effective interval. Rating includes base/minimum/maximum, per-driver/per-vehicle amounts, selected stock/premises/tools components, claims/valeting/young-driver/NCB factors, tax/fee,14-day expiry, age/trading thresholds and pinned valeting trade IDs. Authority/binder limits require every dimension and explicit closed cover limits; missing/null is never unlimited. Cross-field interval, age range, grant<=binder and published/current checks belong to server rules. Fixture grants never imply that system-admin or servicing can bind.

Base demo prices are600RoadRisks/800Combined, additional driver50 and vehicle25, stock20bps and premises100 for selected Combined cover, tools212.58 only if selected. Claims loading10%, valeting12%, youngest named/permitted driver below25 adds18%, eligible>=5claims-free years discounts8%; each adjustment uses the pre-loading subtotal, rounded separately. No NCB discount with conflicting claims history. Apply effective minimum premium from the current approved agency-term override when permitted, otherwise the rule minimum; preserve that source/version in factors. Apply minimum before term proration. Annual uses full amount; short-period uses London civil duration divided by start-anniversary civil duration with checked decimal arithmetic and halves away from zero. Tax/commission derive from term premium; approved agency commission/fee-share/collector/settlement terms are authoritative. No binary floating point in production money.

Golden baseline: Combined1200premium+144tax+35fee=1379gross; net agency due1259 at10% commission. With20% fee sharing, net1252/insurer1224/retained28. Separate or direct collection: debtor1379, insurer1224, retained28 and broker127.180-day short period yields591.78premium/71.01tax/697.79gross. Source factor examples and independent arithmetic tests accompany config fixtures; runtime unit tests must still implement/prove the actual rule engine in06-02.

## Decisions, proof and capacity

Closed outcomes approve/conditional/query/decline/reopen preserve reasons and actor authority. Conditional/query decisions require nonempty typed conditions; plain approve cannot smuggle conditions or arbitrary premium. Catalog: driver proof, premises security, signed statement, trading history, W-07 overnight-security warranty, named-drivers warranty, explicit revise-stock/revise-vehicle. Risk-changing conditions require a new draft/revision/rating; they cannot directly overwrite sums. Selected bulk decisions validate all root/child versions and dimensions before any effect.

Supplied capacity responses require cycle, escalation ETag, exact submission ID/hash, named providerUnderwriter, reference/body/time and actual evidence. Approval additionally requires validity interval and explicit authorisedLimits; conditional requires typed conditions. A decline/query cannot carry approval extent. Typed extensions cover only named amount/age/trade predicates for that submission; they never widen an actor grant globally. Unsupported hard exclusions remain blocked; an extension cannot override an unrelated limit or proof requirement. Deterministic provider outcomes use persisted request/event/attempt identities and are labelled demo-provider, never staff impersonation.

## Terms, acceptance, issue and persistence

Prepare contractual terms before collecting the signed statement for that version; missing signature must not deadlock preparation or cause a re-rating loop. Send only after all required proof/non-signature decisions, valid current pricing/config and safe scoped recipient contacts. Queued is distinct from successfully delivered demo output. Acceptance records named accepter, explicit received time/channel and reviewed evidence against current exact delivered terms and assurance; reject future, pre-delivery, expired, foreign or obsolete context. Successful send never implies acceptance.

Issue re-evaluates current authority and every prerequisite under held scope. At most one first issue per source quote, independently of command key. Policy, term, issued snapshot, transaction, exact balanced journal/obligations, audit and unique document/outbox requests share one SQL transaction. Failure after financial writes must roll all back. Existing successful same-key receipt replays identities only after current authorisation. Amount due is not payment; document request is not a generated file. FirstPolicyView includes actual identity lineage and component totals for future servicing; no cash is created here.

Fresh product/rule/binder/authority seed versions must be additive and effective for the entire insurance term. Do not publish/rewrite old draft product versions pinned by existing captures. Explicit refresh request chooses a permitted same-product published version and confirmed current agency terms, validates all existing answers and creates a new immutable quote revision with current config pins. Historical versions/readiness remain unchanged. Later runtime plans own schema migrations and actual upgrade/restart proof.

## Verification and limits

Tests compile every API component/request/response strictly and cover all new JSON mutation fixtures, missing/unknown fields, current-context shapes, CSRF/key/ETag requirements, typed conditions/provider outcomes and safe issue identities. Source tests reconcile all selected question rows/reference bindings and211candidate controls' Phase6 operation dependencies. Financial golden tests use independent integer-penny totals and the prior financial design rules. JSON tests do not prove authorisation, chronology, transactions, provider fencing or browser behavior; the following runtime plans must supply real unit/SQL/API/UI/restart evidence. Full generic document generation, policy servicing, finance screens, CC and global reporting remain with their named phases.
