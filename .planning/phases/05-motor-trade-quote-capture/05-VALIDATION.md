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


## 05-02 transactional quote service — 2026-09-16

Fresh final .local/phase5-quote-service-final:360unit+70integration=430passing cases,63realSQL,0skips; assert-test-results.ps1 passed. Two SQL service scenarios verify persisted create/save/read, no-op/replay/ETag/history/projection behavior, suspension/read restrictions/closure, same-key races, competing-key stale writes and complete rollback at business-activity and receipt failures. Null saves are explicitly rejected and tested. Initial full run is superseded by the final run after that review fix. Contracts290/949controls/336operations and frontend30pass. ClientActivity migration and generated schema now allow quote record kind; HTTP labels/scoped links are still part of upcoming integration. No quote HTTP route or demo quote data is exposed;05-02 remains incomplete.


## 05-02 strict HTTP input/activity projection — 2026-09-16

Fresh final .local/phase5-quote-http-input-final:360unit+88integration=448passing cases,63realSQL,0skips; assert-test-results.ps1 passed. Eighteen HTTP-helper tests in the API-referencing integration assembly cover streaming size/Unicode/depth/shape/ID/header guards. Existing real-SQL service test now uses authenticated HTTP reads to prove accurate quote activity labels, denied agency-admin counts and suppressed risk/links. Initial run superseded after padded-UUID review fix. Contracts290/949controls/336operations and frontend30pass. The new request helper is not routed; capture response/readiness/capabilities, permission metadata and actual quote endpoints remain next.


## 05-02 held quote read projection — 2026-09-16

Verified current save availability and client/agency/product labels materialize under held quote read authority. Real SQL regression covers active incomplete capture, suspended agency, inactive client/relationship, withdrawn/closed quote, malformed/revoked/restored capture settings, renamed identities, unchanged immutable history/no read side effects and denial after stored-user suspension. Capture capability is advisory and separate from readiness; expected configuration failures do not hide historical reads.

Fresh full suite:449passing=360unit+89integration, including64realSQL,0skips. Evidence:.local/phase5-quote-read-final (TRX); .local/phase5-quote-read-final.log. assert-test-results.ps1 -ResultsDirectory .local/phase5-quote-read-final -MinimumTests 449 -MinimumSqlTests 64 passed. Targeted regression passed in .local/phase5-quote-read-targeted-verified; earlier test fixture/assertion attempts are superseded. Contracts290pass/949controls/336operations (.local/phase5-quote-read-contracts.log), frontend30pass (.local/phase5-quote-read-web.log). No new migration, endpoint, frontend/funnel edit or native demo reset.05-02 and QUO acceptance remain incomplete; next are term/readiness projection and authenticated HTTP route integration.


## 05-02 London term assessment — 2026-09-16

20 new unit cases verify exact local input parsing, London spring gaps/repeated hours and explicit offsets, missing/unsupported term fields, derived annual ends, leap-day clamping, annual-end gap/ambiguity, chronology across repeated hours and annual date-range overflow. Persisted quote reads now assess their retained local term intent; the real SQL service test verifies correct UTC projection while caller proposal JSON retains original intent. Incomplete dates produce field issues without preventing partial saves. This is a term readiness component, not full quote readiness.

Fresh full469passing=380unit+89integration,64realSQL,0skips. Evidence:.local/phase5-quote-term-final (TRX), .local/phase5-quote-term-final.log; assert-test-results.ps1 -ResultsDirectory .local/phase5-quote-term-final -MinimumTests 469 -MinimumSqlTests 64 passed. Targeted20unit passed in .local/phase5-quote-term-targeted. Contracts290pass/949controls/336operations (.local/phase5-quote-term-contracts.log), frontend30pass (.local/phase5-quote-term-web.log). No migration, new quote endpoint, UI/funnel change or native demo reset.05-02 and QUO requirement acceptance remain incomplete; readiness composition and authenticated routes are next.


## 05-02 authenticated quote API / partial readiness — 2026-09-16

Implemented authenticated create/detail/proposal-save/readiness routes with quote-read/quote-capture policies, held stored authority, strict bounded requests, CSRF, no-store, ID-only receipts, ETags and bounded safe field errors. Tested actual cookie login and SQL persistence through create/read/save/no-op/replay, stale/altered/missing/weak versions, unknown query/missing record, malformed/oversized/wrong-media/invalid-shape input, unsupported match association, denied reads/writes, suspended authority before replay and closed capture. Readiness returns real term/structural/eligibility issues and an explicit server-owned progression blocker pending dependent semantic/evidence/matching plans; no successful full assessment is claimed. All six complete source examples remain blocked in unit tests.

Fresh full473passing=383unit+90integration,65realSQL,0skips. Evidence:.local/phase5-quote-api-final (TRX), .local/phase5-quote-api-final.log. assert-test-results.ps1 -ResultsDirectory .local/phase5-quote-api-final -MinimumTests 473 -MinimumSqlTests 65 passed. Final targeted3unit+1SQL/API passed in .local/phase5-quote-api-targeted-final; initial test-clock/error-expectation attempts are superseded. Contracts291pass/949controls/336operations (.local/phase5-quote-api-contracts.log), frontend30pass (.local/phase5-quote-api-web.log). No migration, UI/funnel edit, native demo initialization/reset or browser/human UAT claim.05-02 is incomplete; next product selection and supported-service quote demo examples, followed by remaining plan acceptance.


## 05-02 capture product selection — 2026-09-16

Verified GET /api/v1/quote-products with strict single relationshipId query, quote-read authorization, no-store and held stored relationship/configuration/eligibility scope. A real SQL/API scenario covers approved/unapproved offers and exact explicit capture pins, safe public projection, anonymous/missing-query/missing-relationship errors, suspended agency/inactive relationship/provider, future/current setting selection, distribution/capture revocation, malformed/dangling configuration, revoked identity before configuration reads and no quote/receipt writes. A helper test covers nine malformed/duplicate/unknown query forms. Offers come only from the current explicit capture catalogue; an intentionally empty catalogue is revocation, not a missing-configuration fallback.

Fresh full475passing=383unit+92integration,66realSQL,0skips. Evidence:.local/phase5-quote-products-final (TRX), .local/phase5-quote-products-final.log; assert-test-results.ps1 -ResultsDirectory .local/phase5-quote-products-final -MinimumTests 475 -MinimumSqlTests 66 passed. Targeted2pass in .local/phase5-quote-products-targeted. Contracts291pass/949controls/336operations (.local/phase5-quote-products-contracts.log), frontend30pass (.local/phase5-quote-products-web.log). No migration, frontend/funnel change, native demo initialization/reset or full-readiness claim.05-02 remains incomplete; supported-service quote demo examples and remaining acceptance are next.


## 05-02 quote demonstration / final plan gate — 2026-09-16

Added the guarded local quote fixture command and eight persistent examples through QuoteService. The dedicated fictional pre-approved agency context is explicitly marked as imported demo history, not an agency approval execution. The new SQL regression proves two product grants, eight initial quotes/revisions/receipts, complete/incomplete term data, unchanged IDs on repeat, preservation of later user edits, suspension blocking and original draft-agency preservation.

Fresh full476passing=383unit+93integration,67realSQL,0skips. Evidence:.local/phase5-quote-demo-final (TRX), .local/phase5-quote-demo-final.log; assert-test-results.ps1 -ResultsDirectory .local/phase5-quote-demo-final -MinimumTests 476 -MinimumSqlTests 67 passed. Targeted1SQL passed in .local/phase5-quote-demo-targeted; final suite includes the culture-independent snapshot date refinement. Contracts291pass/949controls/336operations (.local/phase5-quote-demo-contracts.log), frontend30pass (.local/phase5-quote-demo-web.log).

Native CoverMGA_Demo: normal additive initialization and --seed-quote-demo succeeded; repeat initialization/seed created0additional quotes and retained the same8IDs. SQL confirmed4quotes/current revisions per Motor Trade product,0missing current pointers, and AG-DEMO-01/02 still draft. Existing agency grants were not expanded. Evidence:.local/phase5-quote-demo-native-initialize.log, .local/phase5-quote-demo-native-reinitialize.log, .local/phase5-quote-demo-native-first.json and .local/phase5-quote-demo-native-repeat.json (log streams ending in result JSON).

Plan05-02 backend acceptance is satisfied with its recorded scope clarification: readiness is partial/fail-closed until dependent semantic/evidence/matching plans. No quote UI/browser/human UAT completion is claimed, no funnel change, no reset and no rating/issue authority. See05-02-REVIEW.md and05-02-SUMMARY.md; whole QUO requirement acceptance remains05-11.


## 05-03 first business-readiness slice — 2026-09-16

QuoteBusinessRules composes required business/proposer/contact/premises answers, source conditional declarations, prototype business context/history and activity declarations/splits into persisted readiness. Valid-shaped incomplete drafts remain savable and quote-assessment-unavailable remains unconditional.25new unit cases plus the amended real SQL/API regression cover trusted references, false/zero/absent distinctions, all six source fixtures, retained contradictory details and saved issue paths. Additional business semantics and all wizard UI acceptance remain pending; see05-03-PROGRESS.

Fresh full501passing=408unit+93integration,67realSQL,0skips. Results:.local/phase5-quote-business-final; log:.local/phase5-quote-business-final.log; assert-test-results.ps1 -ResultsDirectory .local/phase5-quote-business-final -MinimumTests 501 -MinimumSqlTests 67 passed. Integration run15m10s; no cause for slower duration asserted. Targeted28pass:.local/phase5-quote-business-targeted-final. Contracts291pass/949controls/336operations:.local/phase5-quote-business-contracts-final.log; frontend30pass:.local/phase5-quote-business-web.log. Initial restricted Node teardown crash superseded by successful fresh escalated contract run. No native demo reset, migration, UI/funnel change or browser acceptance claim. Implementation commit af750bc.


## 05-03 business reconciliation and browser transport — 2026-09-16

Committed6077ea1: full-name proposer/entity reconciliation, occupation-row completeness/minimum/duplicate/total and metadata-based car-jockey checks, business-policy chronology and business-owned text limits.15new unit cases plus the expanded real SQL/API readiness assertion. Fresh full516passing=423unit+93integration,67realSQL,0skips. Results:.local/phase5-quote-business-reconciliation-final; log:.local/phase5-quote-business-reconciliation-final.log; assert-test-results.ps1 -ResultsDirectory .local/phase5-quote-business-reconciliation-final -MinimumTests 516 -MinimumSqlTests 67 passed. Integration24.8955minutes. Targeted43pass:.local/phase5-quote-business-reconciliation-targeted. Contract291pass/949controls/336operations:.local/phase5-quote-business-reconciliation-contracts.log.

Committede241f28: unexposed quote browser DTO/transport and immutable exact-command recovery helpers. Final frontend37tests, lint, typecheck and production build pass; logs:.local/phase5-quote-transport-web-final.log, .local/phase5-quote-transport-lint-final.log, .local/phase5-quote-transport-types-final.log, .local/phase5-quote-transport-build-final.log. Build originhttp://127.0.0.1:5087. Tests cover lost-response retry retaining exact method/URL/key/body/ETag, stale/denied safe feedback, malformed success uncertainty, canonical versions and payload preservation. No new route/component/CSS or browser acceptance is claimed. No3100listener existed before builds; no preview was stopped. All owned test/build processes completed; no native demo reset or funnel change.

05-03 remains incomplete. Before exact readiness field links, add question identity to the public issue contract: absent answers currently share container paths and may be collapsed. Wizard forms, actual browser recovery/navigation, desktop/mobile verification and full plan summary remain outstanding. Read05-03-PROGRESS for review findings and integration decisions; full quote readiness remains blocked.


## 05-03 question-aware readiness — 2026-09-16

Committedfc4fdd2 adds optional trusted questionId to application/API readiness issues and the OpenAPI/frontend DTOs. Distinct missing questions sharing an answers container are preserved; ordinary fields omit the metadata. Four new unit cases and the actual SQL/API regression verify missing/reordered answers, both products, optional JSON serialization and distinct public question IDs. One new strict contract case covers optional/invalid metadata.

Fresh full520passing=427unit+93integration,67realSQL,0skips. Results:.local/phase5-quote-question-issues-final; log:.local/phase5-quote-question-issues-final.log; assert-test-results.ps1 -ResultsDirectory .local/phase5-quote-question-issues-final -MinimumTests 520 -MinimumSqlTests 67 passed. Integration14.9750minutes. Targeted47pass:.local/phase5-quote-question-issues-targeted. Contracts292pass/949controls/336operations:.local/phase5-quote-question-issues-contracts.log. Frontend37tests, typecheck, lint and production build pass:.local/phase5-quote-question-issues-web.log, .local/phase5-quote-question-issues-types.log, .local/phase5-quote-question-issues-lint.log and .local/phase5-quote-question-issues-build.log. Build used BACKOFFICE_API_ORIGIN=http://127.0.0.1:5087. No3100listener existed before build and no preview was stopped. All checks completed; git diff --check passed.

This closes the issue-identity metadata prerequisite, not the actual field-link/browser acceptance.05-03 remains incomplete: wizard routes/forms, saved exit destination, dirty/uncertain/stale navigation and desktop/mobile browser evidence remain next. No UI route/component/CSS, migration, native demo reset or sales funnel edit; no human UAT claim. The global partial-readiness blocker is unchanged.

## 05-03 creation UI browser slice — 2026-09-16

Implementation `ee0fffd`; protected creation/landing/saved receipt with actual API storage and immutable recovery. Fresh37frontendtests plus lint/typecheck/production build pass in `.local/phase5-quote-create-{web,lint,types,build}.log`. Real Chrome script `scripts/verify-quote-create-browser.mjs` passed: both Motor Trade products persisted and reloaded; successful write with lost response; later403retains uncertainty; exact key/body retry returns same ID; frozen controls and link/history guard; system-admin page/API denials;314pxrail and390pxmobile no overflow. Evidence `.local/phase5-quote-create-browser.log`, `.local/browser-evidence/quote-create/report.json` and screenshots there. Selection desktop/mobile and receipt mobile visually inspected. No human UAT claim. Preserved native demo referencesQT-MT-0000000009/10, revision1/readinessfalse. Owned previews65648/65616 stopped after command-line identity verification. No backend changes/rerun; preceding520backend/67SQL and292contract evidence remains latest. No funnel change or database reset.

Full05-03acceptance remains open: source-complete business and term wizard, saves/resume, stale edit handling and edit navigation still required. Minimal receipt is a real saved destination, not general quote discovery. Chrome Navigation API verified; fallback browsers and explicit reload warning override are not durable recovery guarantees. See05-03-PROGRESS for exact limitations and next work.

05-03 typed-form helper checkpoint: eight data-preservation cases raise frontend total to45passing/0skips. Unit assertions cover exact decimal/basis-point conversions, invalid-input retention contract, immutable sibling/child-ID preservation, versioned answer identity and exact catalogue options; strict draft schema checked for both products. Typecheck/lint pass. Logs `.local/phase5-quote-form-{web,types,lint}.log`. No rendered UI change or new browser/build claim; preceding creation evidence remains applicable. Complete business/term edit UI and its browser acceptance remain pending.

## 05-03 initial editor slice — 2026-09-16

`b5f63eb` adds protected persisted proposer/initial business editor and nine-stage shell, with unavailable controls explicitly stated. Fresh45frontendtests/0skips, lint/typecheck/build pass (`.local/phase5-quote-edit-{web,lint,types,build}.log`). Actual Chrome both-product save/reload, false consent preservation, exact GBP/percentage capture, invalid text retention, lost-response exact key/body/ETag retry, stale comparison/explicit discard, dirty cancel/accept, cross-document Back warning cancellation, Save/exit/resume, edit-role denial and390px layout pass (`.local/phase5-quote-edit-browser.log`, `.local/browser-evidence/quote-business/report.json`). Desktop/mobile screenshots visually inspected. Native final edit fixturesQT-MT-0000000018/19 revision4; previous attempts preserved. Final creation regression including314px rail also passes (`.local/phase5-quote-edit-create-regression.log`, newQT-MT-0000000020/21). Backend/contracts unchanged; previous520/67SQL and292results remain latest. Owned API64440/web16008 stopped and checks complete. No funnel modification/reset/human-UAT claim.

Limits: native browser warning can be overridden; no durable recovery after explicit leave. Non-Chrome fallback not verified. Title/company-category/marketing, conditional source answers/occupations, term/DST controls and readiness links are still absent. Full05-03acceptance remains incomplete; no summary or requirement signoff. See05-03-PROGRESS for failure fixes and exact next scope.

## 05-03 requested term and readiness traversal — 2026-09-16

Committed1bbb7f1: actual London annual/short-period term inputs, repeated-hour offset choices, gap/ordering feedback, leap anniversary and explicit retained-end removal. Five frontend cases;50totalpass/0skips. Lint/typecheck/build pass in .local/phase5-quote-term-{web,lint,types,build}.log. Actual Chrome term journey for both products passes in .local/phase5-quote-term-browser.log with fixturesQT-MT-0000000028/29 revision6; screenshots/report under .local/browser-evidence/quote-term.314pxdesktop/390pxmobile verified and visually inspected; valid offset guidance corrected to neutral colour. Initial business recovery/stale/dirty navigation regression passes (.local/phase5-quote-term-business-regression.log;QT-MT-0000000026/27).

Browser testing exposed unrelated-section false positives from flattened schema diagnostics. Hierarchical traversal skips successful subtrees while retaining selected-branch failures. Three new backend regression cases; focused46pass. Fresh current run523backend=430unit+93integration,67realSQL,0skips; integration7.1789minutes. Verified evidence .local/phase5-quote-term-ui-final-20260916 and corresponding.log; threshold523/67passed. The initially reused .local/phase5-quote-term-final included older TRX files (aggregate992); that aggregate is not accepted evidence. Only this run's exact two TRX files were copied unchanged into the new verified directory. Contracts292pass/949controls/336operations (.local/phase5-quote-term-contracts.log).

Owned final previews31484/32664 stopped after identity verification; full backend test process completed. No schema/API shape change, native reset, external send or funnel edit. Remaining source questions, pinned reference selections, occupations and readiness links keep05-03open. No human-UAT claim or QUO signoff.

## 05-03 pinned proposer selections — 2026-09-16

Owned quote revision catalogue metadata now drives title/company-category and quotation/marketing answer capture. Exact schema/question/reference matches are mandatory before rendering reference controls; no fallback to current product offers. Typed IDs, labels, versions, false answers, explicit empty selections and omitted answers remain distinct. Client contact records are unchanged. Version contracts are strict and historical reads retain owned metadata when capture is unavailable.

Fresh523backend=430unit+93integration,67realSQL,0skips; integration7.3323minutes. Evidence .local/phase5-proposer-pins-20260916 and corresponding.log; directory checked absent before run,523/67threshold assertion passed. Contracts293pass/949controls/336operations (.local/phase5-proposer-pins-contracts.log). Frontend51pass/0skips, lint/typecheck/build pass (.local/phase5-proposer-pins-{web,lint,types,build}.log). Actual Chrome both-product exact reference/consent save/reload, empty-versus-omitted, retained methods, unsupported-version controls unavailable without writes, and390pxlayout pass (.local/phase5-proposer-pins-browser.log; screenshots/report .local/browser-evidence/quote-proposer). Desktop/mobile visually inspected; no human UAT claim. Final fixturesQT-MT-0000000030/31 revision4; business replay/conflict/navigation regression also passes (.local/phase5-proposer-pins-business-regression.log;QT-MT-0000000032/33).

Owned previews31484/39420 stopped; all tests complete. No database reset/migration, real messages or funnel edits.05-03 remains open for conditional/prototype source answers, occupation rows, readiness links and full source reconciliation. Full quote readiness remains blocked.

## Versioned occupation rows — 2026-09-16

Implemented in 5deb125. Trade activities now supports add/edit/remove/reorder of occupations with stable UUID row identities, exact percentage-to-basis-point conversion and references from the saved quote's pinned catalogue. Blank rows and incomplete allocations can be saved; invalid numeric text blocks save and follows its row through reordering. Removing a row clears its pending numeric error. Duplicate occupations and the exclusive car-jockey rule are shown locally and verified against persisted API readiness. Occupation allocation remains separate from the seven prototype activity buckets. Unsupported capture versions retain saved data and hide occupation editing. Stale comparisons include occupation labels and shares.

Frontend54tests pass,0skips; lint/typecheck/production build pass in .local/phase5-occupations-{web,lint,types,build}.log. Chrome on native SQL2022 passed both products: blank-row persistence, exact33.33/66.67 shares, reorder/reload identities, invalid-buffer retention/removal, duplicate/exclusive readiness and version mismatch with no write. FixturesQT-MT-0000000034/35 revision7; report/screenshots .local/browser-evidence/quote-occupations; log .local/phase5-occupations-browser.log. Desktop/mobile component captures inspected; fields stack at390px without document overflow. The mobile element capture includes the sticky header over its introductory text, so it is not evidence of a full-page heading review. Existing business recovery/conflict/navigation regression passed (QT-MT-0000000036/37; .local/phase5-occupations-business.log), as did pinned proposer regression (QT-MT-0000000038/39; .local/phase5-occupations-proposer.log).

No backend or contract changes in this slice. Latest preceding backend523/67realSQL and contracts293 remain valid baseline evidence, not newly rerun results. Owned API31428/web20528 identified by command line and stopped. No native reset, external send or funnel edits. Automated browser and agent visual review do not constitute human UAT.

05-03 remains incomplete. NEXT: conditional/prototype proposer and business questions at their source-owned stages, precise readiness links and complete source reconciliation. Later sections remain unavailable and overall quote readiness stays blocked. No QUO signoff or05-03SUMMARY.

## Source business declarations — 2026-09-16

Trade activities now captures MTS-03-Q04..09: trade association membership/name, VAT registration/number, vehicles handled annually and MIPD vehicle limit. Exact integer input rejects fractions, exponents and overflow, retains invalid text across stages and blocks saving until corrected. Unanswered, false and zero stay distinct. Changing a controlling answer retains existing details visibly for explicit clearing; API readiness identifies missing or inactive association details. VAT number remains optional and limited to20characters, matching the source. Saved catalogue and response-envelope versions must match before controls render. Concurrent-edit comparison includes the business response section and readable labels for these six answers.

Frontend55tests pass/0skips, lint/typecheck/production build pass: .local/phase5-business-answers-{web,lint,types,build}.log. One new unit scenario verifies immutable conditional-answer retention, explicit clearing, zero and draft-schema validity. Real Chrome both-product persistence/reload, conditional/inactive readiness, fractional input retained across stages, explicit clearing, zero limit and version-mismatch no-write checks pass: .local/phase5-business-answers-browser.log and .local/browser-evidence/quote-business-answers. Final fixturesQT-MT-0000000045/46 revision5. An initial script used an incorrect stage-button label; fixed to the actual '2 Proposer', then passed. Earlier fictional attempts remain preserved. Business recovery/concurrency/navigation regression also passed: .local/phase5-business-answers-recovery.log, fixtures43/44.

Mobile390px no-overflow passed. Agent inspection found neutral help text inherited error colouring; corrected selector and rebuilt/reran the browser journey. Final mobile screenshot inspected with neutral guidance and full field labels. No human UAT claim. Backend/contracts unchanged; prior523backend/67realSQL and293contracts remain the latest baseline, not new rerun results. Owned API41016/web47764 stopped after command-line verification (earlier web7328 stopped for rebuild). No migration, database reset, external send or sales-funnel modification.

05-03 is still incomplete. NEXT: source-stage prototype trader/experience/employment and trade-declaration questions, trading-from selection, exact readiness field links and full source reconciliation. Business start date/description currently appear under Trade activities; reconcile prototype Proposer-stage ownership before completing the plan. Later quote sections and complete readiness remain unavailable. No05-03SUMMARY or QUO signoff.

## Source-stage trader and trade questions — 2026-09-16

Added15 source business controls:14prototype questions owned by Proposer/Trade activities plus the funnel trading-from reference. Proposer captures full/part-time status, experience, employment, other business/directorship and main occupation; Trade activities captures location, seven independent activity declarations, other activity description and Yes-answer details. The server projects only these definitions and bound reference choices from the owned catalogue; later claims/vehicle questions sharing risk.business.responses stay excluded. Form reference labels/IDs/version come from the fixed pinned bindings. False is distinct from unanswered, changing controlling answers retains details until explicitly cleared, and unsupported versions prevent edits. Stale comparison now names projected source answers.

57frontendtests pass/0skips, lint/typecheck/build pass: .local/phase5-source-business-{web,lint,types,build}.log. New unit tests reconcile all14prototype question controls against independent control-stage ownership, verify exact reference projections, exclude later claims/vehicle questions and reject invalid bindings/kinds. Real Chrome both-product exact15-answer save/reload, full/part-time retained-detail warning/explicit clearing, trade Yes/details readiness and version-mismatch no-write checks pass. Final fixturesQT-MT-0000000051/52 revision8; evidence .local/phase5-source-business-browser.log and .local/browser-evidence/quote-source-business. Business recovery/concurrency/navigation regression passes on fixtures49/50 (.local/phase5-source-business-recovery.log).

Visual review found unstyled new textareas; applied existing prototype textarea class, rebuilt and reran Chrome. Final desktop component and full-page390px mobile screenshots inspected, no document overflow. This is agent review, not human UAT. Earlier fictional browser fixtures47/48 preserved. Backend/contracts unchanged; prior523backend/67realSQL and293contracts remain baseline, not newly rerun results. Owned API37756/finalweb40896 stopped after command-line verification; earlier web29628 stopped before rebuild. No native reset, migration, external send or funnel changes.

05-03 remains incomplete. NEXT: exact readiness field links and required-input guidance; reconcile business start/description placement against the prototype Proposer stage, remaining source identity inputs and full source acceptance before writing05-03SUMMARY. Full quote readiness stays blocked while later sections are unavailable. No QUO signoff before05-11.

## Final05-03 acceptance — 2026-09-16

Readiness navigation and source-stage alignment committede43af11.59frontendtests, lint/typecheck/build and293contract tests pass. Actual Chrome43readiness targets per product focus correctly, dirty/frozen links are disabled and completed specific fields lose their guidance. Generic section errors remain unlinked instead of pointing to a completed field. Business description/start date moved to Proposer; existing recovery script updated to actual stage placement. All eight final both-product browser journeys pass; exact fixture/log table and source reconciliation are in05-03-SUMMARY.md. Final owned previews47192/49652 stopped. Backend unchanged in final slice; existing523/67realSQL result carried forward. No human UAT, full-readiness or QUO signoff claimed. Plan05-03complete; next05-04driver and history capture.

## Initial05-04driver assessment — 2026-09-16

538backend=445unit+93integration,67realSQL,0skips passed in fresh .local/phase5-driver-core-20260916 and corresponding.log. Directory checked absent before execution;538/67result assertion passed. Integration6.9846minutes.15new backend cases cover source baseline products, age17/85/leap-day boundaries, pinned plans/licence conditions, missing/inactive histories and preserved question identity/global readiness blocker. Frontend62pass plus lint/typecheck: .local/phase5-driver-core-{web,lint,types}.log. Three new stable-row cases cover immutable reorder/removal, global ID/row bounds and retained vehicle/trip/loss references. Existing05-03build against new API passes43readiness targets per product; fixtures73/74 (.local/phase5-driver-core-browser.log). Owned previews48596/44592 stopped. Remaining eligibility/reconciliation and actual driver/history forms are not implemented;05-04incomplete.


## Driver/history capture checkpoint — 2026-09-16

ff5db28:560backend=467unit+93integration,67realSQL,0skips in fresh .local/phase5-driver-history-final-20260916;560/67assertion passed. Earlier failed history run and corrected clock test are documented in05-04-PROGRESS.md.67frontendtests plus lint/typecheck/build and294contracttests pass. Final Chrome both-product driver/history capture and recovery fixtures80/85 revision23 pass; business regression81/83 and43readiness targets per product82/84 also pass. Exact logs, screenshots, prior attempts and process cleanup are recorded in05-04-PROGRESS. Driver forms are functional;05-04is incomplete pending chronology/text and dynamic cover-dependent rules/options. Full assessment remains blocked. No skippedSQL, funnel change or humanUAT claim.


## Final05-04 acceptance — 2026-09-16

573a9eb completes chronology/text limits, ban bands, reconciled cover-dependent options and personal vehicle owner prerequisites. Driver option choices are per-instance, exact and pinned; stale values are retained for explicit clearing with precise saved-readiness focus. Final fresh587backend=494unit+93integration,67realSQL,0skips passed in .local/phase5-driver-options-20260916;587/67result assertion passed, integration9.9447minutes.70frontend, lint/typecheck/build and294contracts pass. Both-product Chrome dynamic options86/87revision14, full driver/history88/89revision23 and readiness90/91(43targets each) pass. Exact logs, screenshots and scope are in05-04-SUMMARY.md. Owned previews32848/33900verified and stopped. Plan05-04complete; next05-05vehicles. Full readiness and QUO signoff remain gated.


## Final05-05 acceptance — 2026-09-16

bdd8205 implements vehicles, specified/ordinary roles, modifications, portfolio and separate held/covered plates with source rules and exact readiness links. Fresh .local/phase5-vehicle-final-20260916 passed602=508unit+94integration,68realSQL,0skips;602/68assertion passed, integration9.1906minutes. Initial core run exposed bounded-readiness ordering regression; corrected and focused API then fresh full run passed.74frontend, lint/typecheck/build and294contracts pass. Final Chrome vehicles100/101revision17, drivers97/99revision23 and readiness96/98(43targets each) pass. Exact logs, failed attempts, SQL projection/history evidence, dependency boundaries and cleanup are in05-05-SUMMARY.md. API56412/web2944stopped; source snapshot unchanged. Next05-06. No full-readiness or QUO signoff.


## Final05-06 acceptance — 2026-09-16

94f2b52 completes premises, insurance, cover/extras, European trips, declarations and actual missing evidence requirements. Fresh .local/phase5-sections-verified-20260916 passed633=539unit+94integration,68realSQL,0skips;633/68assertion passed, integration10.4116minutes. Earlier compilation/fixture/readiness-ordering failures are documented in05-06-SUMMARY and are not accepted evidence.78frontend, final lint/typecheck/build and294contracts pass. Final Chrome114/115revision12/11 passes persistence, preserved original premises, actual trip drivers, linked trip errors, explicit proof requirements, exact retry and stale comparison/discard;314pxrail/390pxcontainment and screenshots inspected. Vehicle regression109/110and readiness105/106pass. Final API50232/web55440stopped. No funnel change or final QUO signoff. Next05-07durable lookups.


## 05-07 storage prerequisite — 2026-09-16

035be54 input rules and f480e6e lookup storage: fresh .local/phase5-lookup-storage-final-20260916 passed644=549unit+95integration,69realSQL,0skips;644/69assertion passed, integration6.6742minutes. New real-SQL test covers lookup/selection ownership, source fingerprint, target membership, immutable input/outcome/decisions, candidate prerequisites and duplicate selection. EF migration/model synchronization passed. No runtime routes/workers/UI enabled, no demo DB migration, no05-07completion or QUO signoff. Details and next integration work are in05-07-PROGRESS.md.


## Final05-07 acceptance — 2026-09-16

`a509e6e` completes durable private lookups, six demo scenarios, leased/recoverable workers, immutable candidate/manual selection, trusted vehicle provenance and actual quote controls. Fresh .local/phase5-lookups-complete-20260916 passed649=549unit+100integration,73realSQL,0skips;649/73assertion passed,8.8346minutes.78frontend,294contracts/337operations, lint/types/final build passed. Both-product Chrome121/122revision8/7passes every target, schema validation, pending-input edits, exact replay and stale/failed recovery; final visual reload confirms styled controls and314px/390pxlayout. Final API67508/web49308stopped. Detailed evidence and corrections in05-07-SUMMARY. No funnel edits or final QUO signoff. Next05-08.


## 05-08 evidence storage prerequisite — 2026-09-16

`b543b81` rules and `ecab402` storage verified: fresh .local/phase5-evidence-storage-final-20260916 passed660=559unit+101integration,74realSQL,0skips;660/74assertion passed,7.5947minutes. New SQL storage coverage binds hashes/lengths, actual drivers, same-quote files/revisions and immutable withdrawal history; model snapshot check passed. Subsequent b632dc7scoped services/read model passed focused SQL but its full regression is still running; see05-08-PROGRESS. No runtime endpoints/UI/readiness acceptance or05-08completion claimed.


## Final05-08 acceptance — 2026-09-16

be78212 completes scoped HTTP evidence, persisted readiness and actual upload/attach/download/withdrawal controls. Full service661/75 and final HTTP662/76 regressions completed successfully with0skips.79frontend,294contracts/338operations, lint/types/final build and Chrome125/126revision2 passed. Demo migration preserves history; no running tests/previews remain. See05-08-SUMMARY for exact artifacts and corrections. Next05-09; no final QUO signoff.
