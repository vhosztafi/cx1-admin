---
phase: 05
slug: motor-trade-quote-capture
status: in_progress
nyquist_compliant: false
wave_0_complete: true
created: 2026-09-15
---

# Phase 5 validation strategy

Existing xUnit unit/real SQL/API, Node contract/frontend, ESLint/TypeScript, production Next build and Chrome infrastructure is ready. Starting gate329backend/57realSQL,81contracts,30frontend and20retained browser journeys. New named tests below are planned outputs, not existing passing evidence. Never count skipped SQL tests or mix old TRX directories with a fresh run.

| Plan / threat | Requirements | Required semantic evidence |
|---|---|---|
|05-01 / T-05-01|QUO01–06|All364candidate controls and255occurrences have explicit path/type/options/applicability/owner; strict draft/ready schema rejects unknown sensitive fields, duplicate IDs/keys and invalid typed references. Canonical nested-history gaps closed before endpoints.|
|05-02 / T-05-02|QUO02/06|QuotePersistence: same-key/changed-body/stale/rollback/current-pointer ownership, immutable history, no-op saves, normalized hash, suspended/revoked authority and capture closure.|
|05-03 / T-05-03|QUO02/03|Business/term unit tests: DST gap/repetition, leap anniversary, percentage totals and incomplete/complete shape. Real two-product create/resume and dirty/stale/uncertain browser.|
|05-04 / T-05-04|QUO04|DriverRules: age-at-term, licence/residency, every nested history and conditional contradiction. Browser stable identity/reorder/edit/remove; forbidden orphan owner references.|
|05-05 / T-05-05|QUO04|VehicleRules: typed ownership/units/registration, duplicates, specified links, modifications, plate counts and proportions. Current search projection versus immutable historical rows.|
|05-06 / T-05-06|QUO03/04|Readiness: both products' complete mapped paths, bounded amounts, previous insurance/NCD and reachable Combined subsection, question applicability, evidence still missing until actual attachments.|
|05-07 / T-05-07|QUO05|QuoteLookup: scoped durable outcomes, no-match/multiple/manual, leases/restart/provider-success-before-apply, stale input/fingerprint selection race, no privileged job disclosure.|
|05-08 / T-05-08|QUO03/04|QuoteEvidence: exact file bytes/hash, bounded types, foreign item/file/revision, stale/withdrawn evidence and response-loss replay; boolean alone never verifies.|
|05-09 / T-05-09|QUO06|QuoteLifecycle: keyed historical comparison, clone remap/source isolation/evidence removal, destination eligibility, save-withdraw race, capture-close and revision/hash invalidation seam.|
|05-10 / T-05-10|QUO01/02/06|QuoteIntegration: real client/intake links, current owned discovery/counts/cursors, match progression fence and reassociation race, accepted agency cookie safe projection/foreign denial.|
|05-11 / T-05-11|All|Fresh full no-skip suite, two-product no-reset Chrome and all retained regressions, process restart, source/security/rendered reviews and accurate requirements/backlog.|

For each implemented backend slice use a fresh `.local/phase5-<slice>-full` directory and scripts/assert-test-results.ps1 with actual reviewed counts. Windows has2more baseline scenarios than Linux; preserve that distinction when raising CI minima. Execute scripts/test-result-gate.ps1 and parse workflow YAML after changes. Do not rebuild while an owned preview holds DLLs; stop only verified task PIDs, never unrelated port5080.

UI slices use built API5087/Next3100, BACKOFFICE_API_ORIGIN set at build and start, real fictional SQL records, exact lost-response retry assertions, labelled intercepted recovery where used, keyboard/native dialog focus/Escape,314px desktop rail and390px containment. Capture source/application screenshots and inspect them. Final restart tests compare actual quote revisions, current pointers, evidence bytes and lookup state plus fresh authenticated application reads. Human/assistive-technology UAT, hostedCI and Docker runtime remain separate.

QUO-01 remains partial for policies untilPhase6. QUO02–06 can be accepted only after05-11 verifies whole-phase behavior. No source-mapping enumeration or plan review is implementation acceptance.


## 05-01 actual design evidence — 2026-09-15

Source/schema/API gate complete; see05-01-REVIEW.md and05-01-SUMMARY.md. Full node scripts/validate-contracts.mjs passes287tests with0failures/skips, OpenAPI lint and949controls/336operations. All six generated capture examples pass the composed section validators and capture-ready shape. Log: .local/phase5-contract-validation.log. No backend/UI/migration changes in this plan; runtime counts above remain prior accepted evidence. Nyquist/phase compliance remains false until actual runtime implementation and05-11 acceptance.

## 05-02 canonicalization slice — 2026-09-15

Fresh .local/phase5-quote-canonical-verified contains297unit and64integration passes (361total,57realSQL,0skips); assert-test-results.ps1 passed.32new tests cover bounded exact canonicalization/version-pinned hashes. Initial .local/phase5-quote-canonical-full SQL encryption failures are not passing evidence; required local-access rerun succeeded. Contracts287 and frontend30 also pass. No actual quote persistence/endpoints yet;05-02 and phase compliance remain incomplete. See05-02-PROGRESS.

## 05-02 item identity slice — 2026-09-15

Fresh .local/phase5-quote-identity-verified:307unit +64integration =371passing cases,57realSQL,0skips; assert-test-results.ps1 passed.10new .NET identity/link cases and1Node nil-ID case; contracts288/frontend30pass. .NET item identity is an internal prerequisite, not a completed capture boundary or quote endpoint. No migration/UI/funnel changes. See05-02-PROGRESS and05-DOTNET-SCHEMA-DECISION for next work.

## 05-02 closed shape slice — 2026-09-15

Locked dependency restore and fresh .local/phase5-quote-shape-verified pass:323unit +64integration =387cases,57realSQL,0skips; assert-test-results.ps1 passed.16new cases cover bundled draft shape/all answer types/formats/authority/concurrency/error bounds. Contracts288 and frontend30pass. No quote SQL migration/endpoints yet; catalogue identity/composition remains next. See05-02-PROGRESS for dependency changes and exact logs.

## 05-02 composed capture boundary — 2026-09-15

Fresh final .local/phase5-quote-boundary-final:337unit +64integration =401passing cases,57realSQL,0skips; assert-test-results.ps1 passed.14new .NET cases cover all six capture fixtures, every reference binding's edge options, scoped questions and all parser/shape/catalogue/item gates. A review correction for missing/present-stale question versions is covered in .NET and Node; contracts289/frontend30pass. Earlier400-case run is superseded. SQL quote storage/endpoints remain next; see05-02-PROGRESS.


## 05-02 quote SQL storage — 2026-09-15

Fresh .local/phase5-quote-storage-verified:337unit +66integration =403passing cases,59realSQL,0skips; assert-test-results.ps1 passed. Two new real-SQL scenarios verify migration/model parity, additive upgrade and seed preservation, composite ownership/current pointer, append-only history/activity, immutable references, UTF8 JSON bounds, closed capture and rollback after revision/projection/pointer writes. Contracts289/frontend30 pass. Native demonstration database and funnel unchanged. Scope/service/API commands remain unimplemented; this is an internal storage prerequisite, not completion of05-02. See05-02-PROGRESS for logs and remaining runtime obligations.


## 05-02 stored quote authority/scope — 2026-09-15

Fresh .local/phase5-quote-scope-verified:344unit+67integration=411passing cases,60realSQL,0skips; assert-test-results.ps1 passed. Seven quote role-matrix cases and one SQL authorization/replay scenario, plus extended quote storage scope assertions. Verified held agency/user/role/relationship fences, current authority before receipt replay, inactive-context historical reads, unfinished quote exclusion and capture scope. Contracts289/frontend30 pass. Quote service, product/capture eligibility and endpoints remain next; see05-02-PROGRESS.


## 05-02 capture configuration/eligibility — 2026-09-15

Full .local/phase5-quote-eligibility-verified:354unit+67integration=421passing cases,60realSQL,0skips; assert-test-results.ps1 passed. Ten configuration cases and expanded SQL eligibility assertions cover explicit version pins without changed product metadata, malformed/future/current settings, revocation, provider status, retained-term ownership and held provider/configuration-publication fences. A final test-only size-boundary refinement passed all354unit cases again in .local/phase5-quote-eligibility-final-unit; unchanged production/integration code retains the full-run evidence. Contracts289/949controls/336operations and frontend30pass. No live quote API, capture setting seed or demo reset. See05-02-PROGRESS and05-DATA-API-DESIGN for exact logs, explicit setting decision and remaining service obligations.


## 05-02 capture seed/write preparation — 2026-09-15

Fresh .local/phase5-quote-preparation-verified:360unit+68integration=428passing cases,61realSQL,0skips; assert-test-results.ps1 passed. Six preparation tests cover both default envelopes, six actual capture fixtures, product identity, registration projection, term-sensitive hash, closure and exact pin retention. New real-SQL opt-in seed test verifies revoked/malformed operator setting preservation, repeat IDs/product rowversions, unchanged distribution and absence of fabricated quote data. Contracts289/949controls/336operations and frontend30pass. The normal initializer is wired to seed capture configuration; no native demo initialization/reset was performed. Service/API, derived term/readiness and persistent quote examples remain incomplete.
