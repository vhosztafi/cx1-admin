---
phase: 08-commercial-combined-back-office
plan: '03'
status: complete
subsystem: commercial-capture-ui
tags: [commercial-combined, nextjs, sql-server, browser, immutable-revisions]
requires: [08-02]
provides: [cc-wizard-dispatch, cc-proposer-history-ui, cc-loss-dialog, cc-browser-harness]
affects: [08-04, 08-05, 08-06]
requirements-completed: []
completed: 2026-09-20
---

#08-03 — Commercial business and loss capture

Implementation commit: `b373e62`.

Commercial Combined now has its own persisted quote editor and saved-record view. Users capture proposer/business history, activities and losses through the existing authenticated API and immutable SQL revisions. Ten named stages retain the prototype structure; the first three are functional and08-04 owns the remaining seven. CC-01/02 are not marked complete at this intermediate slice.

## Delivered

- `CommercialWizard`, `CommercialReceipt`, `CommercialBusinessStage`, `CommercialLossStage` and `CommercialQuestionFields` connect creation/view/edit, scoped source questions, activity/loss dialogs, readiness, saved revisions and history/comparison. The CC route exposes no Motor Trade driver/vehicle/NCD screen.
- `CommercialCatalogue` serializes the exact published CC choices. `commercial-capture.ts` provides immutable closed-format edits, stable collection IDs, owned loss-location links and explicit unknown/No/zero. Invalid numeric text remains visible; removing its row clears obsolete errors. Dialog discard and saved row removal are distinct actions.
- Shared `QuoteView<TProposal>` retains the MT default; creation/save commands accept the explicit CC proposal. Existing identity/ETag/receipt conventions are unchanged. Actor and quote access are rechecked during recovery; stale writes retain inputs and uncertainty retains the original request across denial.
- Source corrections add the distinct CC `charity-or-trust` choice and check loss readiness against persisted `occurredOn`. Both had RED regressions before correction. No client-account/MT schema change, migration, new table or sales-funnel edit.
- `RealSqlCommercialCaptureBusinessLossBrowser` and `verify-commercial-capture-browser.mjs --stage business-loss` use complete distinct fictional clients, owned temporary SQL databases, dynamic API/web ports and real Chrome. The host rejects5000 and verifies its configured database; cleanup preserves the live demo.

## Acceptance evidence

| Check | Result |
|---|---|
| CC backend unit cases |25 passed — `.local/phase8-03-source-discovery-green/green.trx` |
| Final native SQL/browser |1 passed,0 skipped — `.local/phase8-03-browser-cf8e3605-85f6-43cf-af6b-2c53e31264a5/sql.trx` |
| Strict TRX accounting |26 unique passing cases,1 real-SQL scenario — `.local/phase8-03-final` |
| Frontend tests |154 passed including5 new CC cases; no skipped/failing case |
| Full source/contracts |376 passed — `.local/phase8-03-root-tests.log` |
| Final source evidence overlay |4 passed — `.local/phase8-03-source-evidence.log` |
| Web lint/typecheck/build |Passed — `.local/phase8-03-lint.log`, `phase8-03-typecheck.log`, `phase8-03-build.log` |
| OpenAPI |422 operations valid;57 existing warnings — `.local/phase8-03-openapi.log` |
| Security/source review |08-03-REVIEW.md passed; no unresolved HIGH/CRITICAL finding |

Final browser evidence: `.local/browser-evidence/commercial-capture/CoverMGA_Test_49d72ea4a6cc4037a4b0a13bf7681c6b/report.json`. It exercises17 source questions; native SQL checks their presence in immutable saved revisions. The journey asserts CC creation, exact turnover and proposer names, stable activity/loss IDs, loss add/edit/discard/remove/reload, unknown versus explicit No/zero, foreign location422 without version advance, stale412 preserving edits, comparison/discard and exact retry after a committed lost response plus intervening403. Current access recovery keeps the draft frozen until receipt confirmation. The same run creates/opens the retained MT editor. Desktop and390px screen/dialog inspection and overflow assertions pass with no uncaught browser error.

RED evidence includes the missing pure UI helper, both backend source corrections, type-check feedback and incomplete/differently dated/duplicate-name storage fixtures uncovered by actual browser execution. Final accepted evidence supersedes those earlier intermediate runs. Test fixtures were corrected without weakening production validation or matching.

## Limits and next plan

Positive loss-location selection follows location creation in08-04;03 proves the empty choice, helper ownership and malicious foreign-link rejection. Source evidence distinguishes navigation rendering/negative-link verification from saved-question proof. Full166-control/109-question interaction coverage, stages4–10, conditional rendering and totals are08-04. Rating/proof, issue/exposure and servicing remain later plans. Human UAT, hosted CI and Docker are not claimed. The live demonstration and persistent keys/history remain unchanged.

Continue08-04 automatically using these helpers and the isolated browser host. Do not rerun earlier broad acceptance merely for documentation changes.

## Self-Check: PASSED

Implementation commit and named files exist; fresh results contain the stated successful cases. The source ledger retains original IDs and explicitly records downstream ownership. No completed CC business requirement is claimed prematurely.
