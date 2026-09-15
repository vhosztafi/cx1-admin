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
