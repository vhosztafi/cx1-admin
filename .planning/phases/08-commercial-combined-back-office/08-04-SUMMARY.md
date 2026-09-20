---
phase: 08-commercial-combined-back-office
plan: '04'
status: complete
subsystem: commercial-capture-ui
tags: [commercial-combined, nextjs, sql-server, browser]
requires: [08-03]
provides: [cc-full-capture, stable-locations-wages, conditional-cover, exact-capture-totals]
affects: [08-05, 08-06, 08-09]
requirements-completed: []
completed: 2026-09-20
---

# 08-04 — Remaining Commercial Combined capture

Implementation commit: `b6ab5f1`.

All ten capture stages now save through the authenticated API into immutable SQL revisions. CommercialLocationStage, CommercialLiabilityStage, CommercialCoverStage and CommercialItemDialog implement stable locations/wages, construction/protection summaries, property values, BI dependencies, liability limits and remaining cover declarations. Every one of the 109 original questions is exercised and independently found in stored revisions.

Unknown, No and zero remain distinct. Deselecting EL/BI/contract works retains inspectable details until explicit clearing. Exact penny totals use integer arithmetic; invalid postcode, money and height remain editable and block saving. Stable row ownership prevents removing a location referenced by a loss. Validation returns to the owning stage/field. No calculated premium, approval or proof receipt is fabricated.

Source corrections add the fourth BI basis estimated-gross-profit to the closed draft/issued/OpenAPI schemas, make flood-history Yes require details and limit subsidence questions to selected cover. RED regressions preceded fixes. No migration, table, sales-funnel change or live demonstration reseed.

## Acceptance evidence

| Check | Result |
|---|---|
| CC backend unit cases | 29 passed — .local/phase8-04-unit/unit.trx |
| Native SQL/Chrome full capture | 1 passed — .local/phase8-04-browser-d2d02ca9-9b49-4ce6-a4d4-48dec2947753/sql.trx |
| Strict current TRX accounting | 30 unique successes, 1 real-SQL scenario, zero skips — .local/phase8-04-final |
| Frontend tests | 159 passed — .local/phase8-04-web-tests.log |
| Source/contracts tests | 376 passed — .local/phase8-04-root-tests.log |
| Final source evidence tests | 4 passed — .local/phase8-04-source-evidence.log |
| Web lint/typecheck/production build | Passed — .local/phase8-04-lint.log, phase8-04-typecheck.log, phase8-04-build.log |
| OpenAPI | 422 operations valid; 57 existing warnings — .local/phase8-04-openapi.log |
| Review | 08-04-REVIEW.md passed; no unresolved HIGH/CRITICAL finding |

Final browser report: .local/browser-evidence/commercial-capture/CoverMGA_Test_514ef68179124ee1a56c876c43bb095e/report.json. The saved proposal has ready=true and no readiness issues. The journey verifies all 109 question IDs, exact source choices, two locations/two wage categories after edits/removals, cover clearing, scoped loss ownership, exact amounts, invalid inputs, focused validation, desktop/390px reload and retained Motor Trade capture. Stale revisions preserve input; committed lost responses retry with the identical key/body/ETag across intervening access denial. SQL assertions inspect immutable revisions separately from browser state.

## Remaining ownership

The source denominator remains 166 controls/109 questions; rating, evidence, referral, terms and issuance keep their downstream owners. Capture readiness is not underwriting approval. CC requirements remain open until their full dependent behavior is verified. Human business/assistive UAT, hosted CI and Docker are not claimed. Live demo data, keys and history remain intact.

Continue 08-05 automatically: trusted CC rating facts, published configuration and durable rating jobs. Broad acceptance need not repeat for documentation-only changes.

## Self-Check: PASSED

Implementation and evidence files exist. Strict accounting was checked again before closeout. Summary and source ledger report only verified behavior and retain downstream ownership.
