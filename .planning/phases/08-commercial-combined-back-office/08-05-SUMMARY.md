---
phase: 08-commercial-combined-back-office
plan: '05'
status: complete
subsystem: commercial-rating
tags: [commercial-combined, exact-money, durable-jobs, sql-server, browser]
requires: [08-04]
provides: [cc-rating-rules, cc-published-underwriting, cc-durable-rating, cc-rating-overview]
affects: [08-06, 08-07, 08-09, 08-12, 08-13, 08-15]
requirements-completed: []
completed: 2026-09-20
---

# 08-05 — Reproducible Commercial Combined rating

Implementation commit: `687c1b4`.

Commercial Combined now rates a saved complete proposal through the existing authenticated command, durable job and immutable result tables. The overview displays the saved premium, component factors, multipliers, expiry and configuration provenance. Current scope precedes command replay; completion rechecks revision, configuration/runtime/scenario, terms and requesting authority. A price does not grant underwriting approval.

## Delivered

- CommercialRatingRules and typed location/wage/extension facts implement the independent£4,195 annual example, disabled EL/BI, exact per-row rounding, all eight wage categories, selected optional covers/BI extensions, non-compounding loadings, minimum, London civil duration, tax, commission and one fee. CommercialUnderwritingInput validates the source proposal/catalogues/stable identities/readiness before projection; no caller premium or synthetic driver facts.
- CommercialUnderwritingConfiguration and generated commercial-underwriting.schema.json/commercial-underwriting-demo.json define closed rating/binder/authority versions and every source cover selection's disposition. Selection limits match the source catalogue; unsupported/missing maps cannot publish.
- CommercialUnderwritingSeed publishes independent CC v3 and rating/binder/authority versions without changing capture-only v2, granting agency access or adding staff decision grants. Existing approved agency terms must explicitly adopt v3. The initializer wires missing-only publication after capture/shared underwriting dependencies and never regrants a withdrawn/retired runtime.
- Migration CommercialUnderwritingDefinitions adds CC-specific limit checks to the existing tables and retains MT constraints. Compatible capture-only and underwriting CC editions may coexist under a narrow null-safe exception; duplicate/unknown kinds cannot. Both CC product kinds gain immutable-definition/final-retirement protection. No new business table.
- StoredRatingInput has the distinct commercial-underwriting-input-1 format and its own Commercial projection. Shared worker/jobs/readback dispatch explicitly; Motor Trade-specific downstream consumers reject the new format until06/07/09. RatingFactor's optional multiplier is omitted from legacy serialization, protecting durable replay. The existing API permits700 factor rows; no new route was added.
- CommercialRatingSummary appears first on the overview with expandable saved details. It uses actual saved values, the existing exact-retry command dialog and automatic coherent status refresh. Actual browser/API/SQL checks cover desktop/390px, reload and retained MT creation/editor. No sales-funnel or live-demo modification.

## Acceptance evidence

| Check | Result |
|---|---|
| Targeted backend unit tests |32 passed:20 CC,6 retained MT rating,6 retained MT projection — .local/phase8-05-final-unit/unit.trx |
| Final CC native SQL |5 passed: publication/initializer preservation, version/config eligibility, durable replay/API readback, stale config and revoked requester — .local/phase8-05-final-sql/sql.trx |
| Retained MT native SQL |10 passed: rating scenarios and eligibility — .local/phase8-05-retained/sql.trx |
| Final actual SQL/Chrome |1 passed — .local/phase8-05-browser-2890c96e-ea4f-410d-bd3b-ab01cbd63026/sql.trx |
| Strict accounting |48 unique successes,16 real-SQL scenarios,zero skips — .local/phase8-05-final |
| Frontend tests |159 passed — .local/phase8-05-web-tests.log |
| Root/source/contracts tests |380 passed — .local/phase8-05-root-tests.log |
| Final source ledger |4 passed — .local/phase8-05-source-evidence.log |
| Lint/typecheck/build |Passed — .local/phase8-05-final-lint.log, phase8-05-typecheck.log, phase8-05-final-build.log |
| OpenAPI |422 operations valid;57 existing warnings — .local/phase8-05-openapi.log |
| Review |08-05-REVIEW.md passed; no unresolved HIGH/CRITICAL finding |

Final browser evidence: .local/browser-evidence/commercial-capture/CoverMGA_Test_3998d0b1594b45438b6a1598d0c6293d/report.json. The complete109-question capture journey requests a rating through the UI, validates actual assessment/rating JSON against the closed API schemas, displays£1,949.40 premium/£2,258.33 gross for its distinct browser fixture, verifies the1.7 BI multiplier and automatic re-rate availability, then reloads at desktop/390px. Native SQL independently checks saved capture revisions. Screenshots were inspected. The separate independent golden fixture remains£4,195; browser and golden risks intentionally differ.

RED evidence: missing typed rules first prevented compilation; actual SQL exposed MT-specific binder constraints; actual API schema validation exposed binary-float noise for valid2.05 height. The latter is corrected in the JS response validator with a regression still rejecting2.051; exact server validation is unchanged. Review also corrected status-refresh timing and overview information order. Final accepted runs supersede those intermediate results.

## Downstream ownership

Continue08-06 automatically. CommercialReferralRules/CommercialEvidenceRules and the held decision context must implement actual referral/evidence/authority behavior and replace the explicit unavailable gates.05 publishes the configuration but adds no CC decision grant.06 must provide appropriate current grants and product-specific authority evaluation.07 owns carrier/terms/acceptance;08/09 own authoritative temporal exposure and issue;12/13 own servicing fee/movement application. CC-03 and compound CC-05 remain open until complete downstream behavior is verified.

Source denominators remain166 controls,109 questions,60 policy controls,298 displays and7 supplementary fields.05's rating is shared/implied behavior, not a reassignment of06 source referral rows. No fake within-appetite/received-proof/issued state is claimed. The existing shared quote identifier is retained; the explicit product label is Commercial Combined. Human UAT, hosted CI and Docker are not claimed. Live SQL demo, persistent keys and immutable history remain unchanged.

## Self-Check: PASSED

Implementation commit, generated artifacts, migration, reviewed screenshots and fresh reports exist. Strict accounting and source ledger regeneration passed. No repeated full backend run was needed; Phase8 full acceptance remains08-16.
