---
phase: 05-motor-trade-quote-capture
plan: '06'
status: complete
completed: 2026-09-16
requirements_completed: []
requirements_supported: [QUO-03, QUO-04]
---

# 05-06 — premises, insurance, cover and declarations

Implementation `94f2b52` completes all nine editable capture stages for both Motor Trade products. Combined premises have stable IDs, source address/use/years/shared-worksite declarations and independent prototype use/security/buildings/contents/overnight/public-access fields. Road Risks premises remain reachable under Trade activities. Previous insurance, separate policy/NCB expiry, exact versus at-least years and discount declarations are reachable in Road Risks insurance and Combined Cover & excess. Source cover limits, dynamic own excess, loan/demonstration/windscreen/other extras, annual European vehicles and temporary trips use the exact pinned options. Trip drivers are actual same-proposal IDs. Declaration/explanation pairs and material facts persist. Vehicle portfolio was already implemented in05-05 and is reused.

New backend insurance, cover/extras and additional-trade assessments compose with existing business/driver/history/vehicle rules. They preserve false versus absent, inactive retained answers, source/prototype differences and actual term/driver/vehicle context. All six complete source fixtures pass these capture rules; remaining issues are the global progression gate, missing stored vehicle provenance and actual missing evidence. Evidence requirements enumerate photocard/DVLA proof per real driver, motor-trader proof and applicable NCB/introductory proof; caller received flags cannot satisfy them. API issues and the wizard display missing evidence explicitly, with driver numbers. File persistence and applicability fingerprints remain05-08; Commercial Combined requirements remainPhase8.

Shared typed controls retain exact money/percent/date/number behavior, stable raw buffers and explicit clearing. Premises/trip rows support add/edit/reorder/remove with reference guards. Saved readiness links reach the correct product/stage/field, including trip dates and retained excess clearing. New sections appear in stale comparisons and reuse exact lost-response replay. Textarea borders and control alignment were corrected after visual inspection. The source sales funnel is unchanged.

## Verification

- Fresh `.local/phase5-sections-verified-20260916` and matching log: **633 passing =539 unit +94 integration;68 real SQL;0 skips**. `assert-test-results.ps1`633/68passed; integration10.4116minutes. Full command used `--no-build --no-restore` after compiling the final application/integration assemblies and corrected unit suite. No old TRX aggregation.
- New insurance tests cover both products, capped/exact NCB meaning, independent origins/protection, conditional details and expiry. Cover/additional tests cover source fixtures, loan prerequisites, inactive/unsupported cover, reconciliation, actual trip boundaries, named/any-driver social use, comprehensive prerequisites, duplicate annual registrations, premises applicability and car-jockey radius. Evidence tests reject boolean substitution and require separate driver/proposal proof. The six-fixture readiness regression verifies all actual missing requirements.
- Initial `.local/phase5-sections-final-20260916` exposed history guidance displaced by evidence within the100-issue cap, and a new unit-test raw-string compile error. Restored established history priority and fixed the test literal. `.local/phase5-sections-accepted-20260916` integration94passed but its earlier unit build failed, so it is not final full-suite evidence. The first corrected unit run had538/539because the previous fixture assertion allowed only the old blockers; updated it to assert every new missing evidence requirement. All539thenpassed. Final fresh633run above is authoritative.
- **78 frontend tests** in `.local/phase5-sections-web-accepted-tests.log`; final lint/typecheck in `.local/phase5-sections-{lint,types}-release.log`; final production build with5087origin in `.local/phase5-sections-build-final-ui.log`. **294 contracts**,949controls/336operations in `.local/phase5-sections-contracts.log`.
- Final actual Chrome journey **QT-MT-0000000114/115**,Combinedrevision12/RoadRisksrevision11: `.local/phase5-sections-browser-final-ui.log` and `.local/browser-evidence/quote-cover/report.json`. Verifies original premises preservation, exact sums, stable European rows/driver IDs, product-specific insurance access, declaration reload, field-linked trip errors, explicit missing proof, lost-response exact replay, stale comparison/discard,314pxrail and390pxcontainment. Final desktop/mobile screenshots inspected. Pre-acceptance corrections included distinct accessible source labels, comparison coverage, styled textareas and hidden-row browser-test locators; original-data preservation is explicitly asserted. Fictional histories retained.
- Shared vehicle-control regression109/110revision17passed in `.local/phase5-sections-vehicles-regression.log`. Existing readiness105/106checks43targets per product in `.local/phase5-sections-readiness-regression.log`. No skips, funnel changes, database reset, real provider call or human UAT claim.
- All owned previews cleaned up: final API50232/web55440 verified and stopped; earlier owned web processes stopped before rebuilds. Full test process completed. Diff check clean.

## Boundaries and next

Plan05-06complete. No QUO requirement signoff before05-11. Rating/issue/full progression stay closed. Next05-07: durable address/vehicle/licence lookups, stored manual recovery, exact candidate selection, worker leases/retries and restart/race/scope tests. Then05-08evidence,05-09history/lifecycle,05-10discovery/matching and05-11final acceptance.
