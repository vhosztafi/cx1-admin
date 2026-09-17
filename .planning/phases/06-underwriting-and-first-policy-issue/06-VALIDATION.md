---
phase: 06
slug: underwriting-and-first-policy-issue
status: planned
nyquist_compliant: true
wave_0_complete: true
created: 2026-09-16
---

# Phase6 validation strategy

06-01 contract gate passed:314 tests, zero failures/skips,354 operations; implementation4800d71. See06-01-SUMMARY for deterministic generation and source evidence. Runtime results remain pending. Every executable task below has a bounded semantic verification owner. Existing infrastructure is sufficient; no new test framework is required.

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

Plan-check coverage approved2026-09-16. nyquist_compliant=true describes complete task sampling/ownership only; wave_0_complete=false until06-01 fixtures actually pass. No Phase6 runtime pass is claimed. Semantic families may be consolidated within named files, never removed from coverage.

## Executable task sampling map

| Tasks | Wave | Semantic verification and dependencies | Runtime status |
|---|---:|---|---|
| 06-01-01, 06-01-02 | 1 | Strict underwriting contracts and source fixtures; node --test tests/underwriting-contracts.test.mjs tests/underwriting-source.test.mjs; node scripts/validate-contracts.mjs | Pending |
| 06-02-01, 06-02-02, 06-02-03 | 2 | Underwriting storage, authority and deterministic domain rules; dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter "FullyQualifiedName~QuoteRatingRulesTests\|FullyQualifiedName~UnderwritingEligibilityTests"; dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter FullyQualifiedName~UnderwritingStorageTests | Pending |
| 06-03-01, 06-03-02 | 3 | Persistent rating, submission and revision lifecycle; dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter "FullyQualifiedName~QuoteRating\|FullyQualifiedName~QuoteUnderwritingLifecycle\|FullyQualifiedName~QuoteMatch" | Pending |
| 06-04-01, 06-04-02 | 4 | Quote overview, rating and revision UI; pnpm web:test; pnpm web:lint; pnpm web:typecheck; pnpm web:build; node scripts/verify-underwriting-rating-browser.mjs | Pending |
| 06-05-01, 06-05-02, 06-05-03 | 5 | Proof review, referrals and typed conditions; dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter FullyQualifiedName~ReferralConditionTests; dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter "FullyQualifiedName~QuoteEvidenceReview\|FullyQualifiedName~QuoteReferral" | Pending |
| 06-06-01, 06-06-02 | 6 | Underwriting decisions and evidence UI; pnpm web:test; pnpm web:lint; pnpm web:typecheck; pnpm web:build; node scripts/verify-underwriting-decisions-browser.mjs | Pending |
| 06-07-01, 06-07-02, 06-07-03 | 7 | Capacity escalation and correspondence vertical slice; dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter FullyQualifiedName~CapacityRulesTests; dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter "FullyQualifiedName~CapacityEscalation\|FullyQualifiedName~CapacityApi"; pnpm web:test; node scripts/verify-underwriting-capacity-browser.mjs | Pending |
| 06-08-01, 06-08-02, 06-08-03 | 8 | Immutable terms, delivery and exact acceptance backend; dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter FullyQualifiedName~QuoteTermsRulesTests; dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter "FullyQualifiedName~QuoteTermsTests\|FullyQualifiedName~QuoteDeliveryTests\|FullyQualifiedName~QuoteAcceptanceTests" | Pending |
| 06-09-01, 06-09-02 | 9 | Quotation preparation, send and acceptance UI; pnpm web:test; pnpm web:lint; pnpm web:typecheck; pnpm web:build; node scripts/verify-underwriting-terms-browser.mjs | Pending |
| 06-10-01, 06-10-02, 06-10-03 | 10 | Policy storage and balanced first-issue posting; dotnet test backend/tests/BackOffice.UnitTests --no-restore --filter FullyQualifiedName~IssuePostingRulesTests; dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter "FullyQualifiedName~PolicyStorageTests\|FullyQualifiedName~IssuePostingStorageTests" | Pending |
| 06-11-01, 06-11-02 | 11 | Atomic policy issue and protected policy reads; dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter "FullyQualifiedName~QuoteIssue\|FullyQualifiedName~PolicyReadTests" | Pending |
| 06-12-01, 06-12-02 | 12 | Issue confirmation, receipt and policy record UI; pnpm web:test; pnpm web:lint; pnpm web:typecheck; pnpm web:build; node scripts/verify-underwriting-issue-browser.mjs | Pending |
| 06-13-01, 06-13-02 | 13 | Policy discovery, client links and safe agency sharing; dotnet test backend/tests/BackOffice.IntegrationTests --no-restore --filter "FullyQualifiedName~PolicyDiscoveryTests\|FullyQualifiedName~PolicySharingTests"; pnpm web:test; node scripts/verify-policy-discovery-browser.mjs | Pending |
| 06-14-01, 06-14-02 | 14 | Both-product acceptance, restart and phase verification; dotnet test backend/BackOffice.slnx --no-restore --logger trx --results-directory .local/phase6-final-fresh; ./scripts/assert-test-results.ps1 -ResultsDirectory .local/phase6-final-fresh -MinimumTests 680 -MinimumSqlTests 85; node scripts/validate-contracts.mjs; pnpm web:test; pnpm web:lint; pnpm web:typecheck; pnpm web:build; node scripts/verify-underwriting-suite.mjs | Pending |

All tasks test in the same plan, so no more than one task elapses without automated feedback. Schema tasks additionally execute initialize-demo and inspect applied migration/retained hashes. Integration tasks repeat affected checks and run full backend only when runtime backend changed. Browser commands assume a production build and owned local hosts; frontend-only tasks still run retained targeted API journeys. No watch modes or placeholders. Additional promised families QuoteRatingTests, UnderwritingEligibilityTests, QuoteRatingWorkerTests, QuoteUnderwritingLifecycleTests, QuoteEvidenceReviewTests, QuoteReferralTests/API, ReferralConditionTests, CapacityEscalationTests/API, QuoteTerms/Delivery/AcceptanceTests, IssuePostingRules/StorageTests, QuoteIssue/API, PolicyStorage/Discovery/Sharing all have owning files in06-02..13. The final suite/restart in06-14 covers every earlier slice.

## 06-02 core evidence

Implementation625bf90: full696cases/87realSQL,0skips; final unit583cases after pure lifecycle helper addition. Additive migration20260916184737 applied to preserved demo with unchanged retained counts/hashes. See06-02-SUMMARY for exact directories and limitations. Public underwriting commands/browser/issue acceptance remain pending.

## 06-03 runtime evidence
Implementation b3c642d:727 full backend cases (601unit/126integration),99realSQL, zero skips;315contract/source tests. Six rating/lifecycle routes implemented. See06-03-SUMMARY.md for exact paths, preservation and limitations. UI and later underwriting/policy acceptance remain pending.


## 06-04 runtime evidence
Implementation da15815:727 full backend/99 realSQL,86 frontend,316 contracts; zero skips. Both-product actual browser and labelled presentation fixtures passed. Final desktop/mobile screenshots inspected. See06-04-SUMMARY for paths and remaining owners.

## 06-05 runtime evidence

Implementation4ace720:741full backend (606unit/135integration),108realSQL,
zero skips;317contract/source tests. Additional foreign-file/superseded-cycle
proof assertions pass in the follow-up test. Fresh and retained SQL upgrade paths
pass; both additive migrations applied with unchanged historical hashes and
credentials. See06-05-SUMMARY for exact evidence. Decision UI remains06-06.

## 06-06 runtime evidence

Implementationf359532:743backend/108realSQL,90frontend,317contracts;zero skips.
Both-product actual decision/proof/recovery browser and final target-labelled
readback/screenshots passed. Real bulk denial creates zero decisions; account
switch prevents dispatch; stale412 retains inputs. See06-06-SUMMARY. Capacity,
terms, acceptance and issue retain later owners.

## 06-07 runtime evidence

Implementation aea6645: 767 backend (614 unit, 153 integration), 126 real SQL,
zero skips; 94 frontend, 317 contracts/source; lint/typecheck/build passed.
Both-product actual Chrome and persisted readback passed, including supplied
reviewed response, exact retry, stale draft, account switch, working-day deadline
and explicit dependent evidence Refresh recovery. Additive migration and repeated
seed preserved historical counts/hashes and credentials. See 06-07-SUMMARY for
exact artifacts. Conditional source supplement requires the explicit 06-14 gate;
terms, acceptance and issue retain their later owners.

## 06-08 runtime evidence

Implementation0ba6132:799backend (628unit/171integration),144realSQL,zero skips.
Full integration30m20s; exact result gate passed.317contract/source cases and
OpenAPI355operations pass (10existingwarnings). Applied additive migration and
repeated initialization preserve all23retained count/hash sets and credentials.
See06-08-SUMMARY for paths, strict HTTP, delivery recovery and immutable acceptance
evidence. UI remains06-09 and policy storage/issue06-10..12.

## 06-09 runtime evidence

Implementation9d68ee6:100frontend tests,317contracts/source, lint/typecheck/build
passed. Both-product actual Chrome and final readback verify prepared/sign/send/
accept, retained exact retry, proof withdrawal/fresh acceptance and314px/390px
layout. Separate negative journey verifies actual queued/failed jobs, ended
recipient, account switch and superseded terms without extra delivery. No backend
change;799backend/144realSQL baseline from06-08 retained. See06-09-SUMMARY.

## 06-10 runtime evidence

Implementation af10de7:811backend (635unit/176integration),149realSQL,zero skips.
Integration22m41s; exact result gate passed. Five new SQL cases cover premature
binding, owner/uniqueness/immutability, retained upgrade, exact posting/sealing and
document requests. Additive migration20260917030154 and repeat initialization
preserved31count/hash sets and credentials. See06-10-SUMMARY. Issue endpoints and
UI remain06-11..12; compound requirement completion remains06-14.

## 06-11 runtime evidence

Implementation4d55d06:834backend (642unit/192integration),165realSQL,zero skips.
Full integration25m38s; exact834/165 gate passed. Sixteen new SQL/API cases cover
atomic issue/races/rollback/current authority before replay/exact accepted proof/
protected reads.319contracts;OpenAPI359operations valid with12warnings. Repeated
seed preserved31hash sets and credentials. See06-11-SUMMARY.
