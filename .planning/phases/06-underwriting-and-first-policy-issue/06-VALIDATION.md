---
phase: 06
slug: underwriting-and-first-policy-issue
status: draft
nyquist_compliant: false
wave_0_complete: false
created: 2026-09-16
---

# Phase6 validation strategy

This is the planning contract, not passing Phase6 evidence. Detailed PLAN task/wave IDs must be added after plans are written and reviewed. Existing infrastructure is sufficient; no new test framework is required.

## Infrastructure and sampling

- xUnit unit/realSQL/API projects in backend/BackOffice.slnx; Node contract/frontend tests; ESLint, TypeScript, production Next build and actual Chrome journeys.
- Quick feedback: affected pure unit tests or Node contracts/frontend tests after a substantive change. Typical pure tests finish within60seconds; SQL timing depends on migration/fixture scope and must not be misreported as sub-minute.
- Every implemented backend slice: fresh full `dotnet test backend/BackOffice.slnx --no-restore --logger trx --results-directory .local/phase6-<slice>-<fresh>` with COVER_SQL_TEST_CONNECTION configured for the nativeSQL2022 instance. Existing minimum680tests/85realSQL,0skips; raise only after measured passing counts. Do not reuse or combine stale TRX directories.
- Result gate: `./scripts/assert-test-results.ps1 -ResultsDirectory .local/phase6-<slice>-<fresh> -MinimumTests 680 -MinimumSqlTests 85`. Expected full baseline16minutes; CI SQL timeout30minutes. Linux baseline678/83 excludes the two existing Windows-only cases.
- Contract changes: `node scripts/validate-contracts.mjs`; frontend slices: `pnpm web:test`, `pnpm web:lint`, `pnpm web:typecheck`, `pnpm web:build`, then real Chrome on API5087/Next3100. Build/start BACKOFFICE_API_ORIGIN=http://127.0.0.1:5087.
- Before final verification: one complete both-product Phase6 browser runner retaining all17quote+20agency journeys, fresh full regression, final screenshot review and actual identified process restart without DB reset.

## Semantic verification map

New test names below are intended outputs, not existing symbols. Exact file/task mapping is completed in the plans; tests must execute meaningful behavior rather than asserting implementation text.

| Feature / requirement | Threat | Secure behavior and required evidence | Planned test family |
|---|---|---|---|
| Source/contracts / UWR01–07 | T06-source HIGH | Every relevant control/option has explicit current or later owner; strict requests reject system-owned identity/state/price and wrong reference/ownership. Existing design-only operations cannot imply runtime availability. | tests/underwriting-contracts.test.mjs; source audit gate |
| Rating / UWR01 | T06-rate HIGH | Same exact proposal/rule input produces equal components; source14-day expiry boundaries; rounding and rule pinning; no support-flag inputs; incomplete shape/invalid term/matching/vehicle provenance blocks. | QuoteRatingRulesTests; QuoteRatingTests |
| Configuration / UWR01–03 | T06-config HIGH | Capture-enabled draft metadata cannot rate; missing/unknown dimensions deny; current and retained term grant distinction; retired product/provider/binder and effective-date boundaries. | UnderwritingEligibilityTests |
| Rating worker / UWR01 | T06-worker HIGH | Duplicate dispatch, provider-success-before-apply crash, expired lease and conflicting callback cannot duplicate effects; stale result remains history and never changes current pointer. | QuoteRatingWorkerTests |
| Revision/cycle / QUO06 | T06-stale HIGH | Same hash/different revision ownership, explicit reopen, re-rating, terms refresh, evidence and condition changes invalidate old applicability/acceptance; stale worker cannot reopen closed/bound state. | QuoteUnderwritingLifecycleTests |
| Evidence / UWR02–03 | T06-proof HIGH | Screened attached file is not underwriter approval; accepted/rejected decisions bind exact attachment/fingerprint; revoked or stale proof blocks; missing proof and stock limit remain independent. | QuoteEvidenceReviewTests |
| Referral / UWR02–03 | T06-authority HIGH | Each relevant dimension and exact boundary checked against effective actor/product/binder authority; servicing/system-admin cannot gain implicit authority; selected bulk decisions all-or-none under versions. | QuoteReferralTests; QuoteReferralApiTests |
| Conditions / UWR03 | T06-condition HIGH | Typed condition identities; same-quote evidence; unsatisfied condition blocks; changing contractual cover/endorsement changes terms hash and requires new acceptance. | ReferralConditionTests |
| Carrier / UWR04 | T06-carrier HIGH | No internal actor impersonation; typed outcome/reference/underwriter/time/body/proof persisted; query/decline/pending blocks; dedupe/retry/restart and stale responses retained without authorizing current cycle. | CapacityEscalationTests; CapacityApiTests |
| Terms/delivery / UWR05 | T06-delivery HIGH | Safe current recipients and immutable contractual payload; queued versus delivered truth; exact retry and durable outcome; changed current terms/config require explicit new cycle. | QuoteTermsTests; QuoteDeliveryTests |
| Acceptance / UWR05–06 | T06-accept HIGH | Exact current rating/revision/terms/evidence context; expired/future/foreign/unsent/obsolete acceptance rejected; named accepter/time/channel/proof retained; no arbitrary status patch. | QuoteAcceptanceTests |
| Posting / UWR07 | T06-finance HIGH | GBP exact pennies; balance per journal; net/separate commission, fee share and direct debtor all reconcile to approved examples; unique source transaction; no negative debit/credit or fabricated payment. | IssuePostingRulesTests; IssuePostingStorageTests |
| Issue / UWR06–07 | T06-issue CRITICAL | Re-evaluate all prerequisites under held authority, not UI flag; multiple keys concurrently create one policy/version/transaction/posting; failure after financial write before audit/outbox rolls everything back; current auth precedes replay. | QuoteIssueTests; QuoteIssueApiTests |
| Immutable policy / UWR07 | T06-history HIGH | Same-agency/client/term/transaction/version constraints, immutable JSON/hash and source lineage, current pointer ownership; additive migration preserves old quotes/evidence. | PolicyStorageTests |
| Discovery / QUO01,CLI01,AGY04 | T06-disclosure HIGH | Real policy IDs, filters/order/paging/registration, scoped client links and common safe agency projection; accepted agency cookies prove foreign and hidden-field/count/cursor denial. | PolicyDiscoveryTests; PolicySharingTests |
| UI / all | T06-recovery HIGH | Actual persisted journeys for both products, dirty/stale/uncertain exact body/key/ETag retention, safe retry, keyboard/focus,314pxrail/390pxcontainment; no fake carrier/delivery success. | Dedicated verify-underwriting-*-browser.mjs scripts |
| Restart / all | T06-restart HIGH | Actual process restart preserves issued snapshot bytes/hashes, decisions/acceptance, posted totals, evidence and attempts; fresh scoped authentication reads same records. | verify-underwriting-restart scripts |

## Wave0 and execution gates

- [ ] Complete source/data/API/UI designs before generating runtime commands.
- [ ] Every new test family has an owning PLAN task with executable automated command and explicit fixture creation.
- [ ] Pure domain tests precede public handlers; SQL migrations and model snapshot tested on isolated owned databases and applied additively to the preserved demo before browser checks.
- [ ] Extend worker kind allowlists/dispatch/retry permissions intentionally; unsupported kinds remain denied.
- [ ] Retain all Phase5 regression scenarios and recheck actual matching/closure race against the real progression command.
- [ ] No skip/watch flags or real external calls; no placeholder evidence counts as success.
- [ ] Final measured test minima and CI rejection cases/YAML updated when justified.

## Human/hosted limits

Human business and assistive-technology UAT remainsPhase13 when available; agent screenshots and keyboard automation are distinct evidence. HostedCI and Docker runtime remain unperformed until actually run. All ordinary functional/persistence checks above are automatable locally. No new manual confirmation is required for approved demo operations.

## Sign-off

Pending executable plans and plan-check coverage. nyquist_compliant remainsfalse until every task/test owner and sampling dependency is complete; runtime pass evidence is recorded separately during execution.
