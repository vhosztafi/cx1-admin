# 08-16 review — ongoing final acceptance

This is an interim review, not phase completion. Updated2026-09-21T06:23Z. The latest section below supersedes historical open/run notes.

## Latest reviewed evidence

Commit0a18a17 guards sign-in until hydration and uses explicitPOST, preventing a native GET from placing credentials in the URL. Actual browserRED asserted disabled=false before the fix; no-JavaScript and hydrated validationGREEN both pass, including on the updated live preview. The first connection-refused attempt is excluded. Demo fixture logs redact the plain and URL-encoded generated password.

The first-page commercial history reader retries only409/underwriting-history-changed, at most3 retries, and never retries a cursor, unrelated conflict or denied read. Four focused tests pass. Mutation replay behavior is unchanged. The rollback tests now expect the later document-history migration's51971 guard and verify its retention as well as the existing data-preservation assertions; nativeSQL4 passes. Root395,frontend172,lint,typecheck and builds pass.

Final current live initialization preserved139 table fingerprints across two passes. Verified owned processes were actually restarted; all3 policy graphs/12 versions,5 pinned commercial exposure readings and persistent key hashes are unchanged. Evidence: `.local/phase8-16-final-initialization/report.json` and `.local/phase8-16-final-restart/compare-report.json`. The preview needed a separate build with its API5087 rewrite; the initial5080 test-build attempt is excluded. Desktop/390px current capture evidence was visually inspected with no new layout defect.

Current full acceptance is still open. A fresh non-issue commercial editor run timed out while reacquiring after reload. Its helper now waits for the loaded editor before starting the response deadline and handles click/response promises together; actualGREEN is queued, not yet claimed. The interrupted full-backend/commercial runs are excluded. Retained suites and a source-pinned sequential follow-on runner are recorded in08-16-CHECKPOINT.md. Finish them, perform strict result accounting and reconcile final source/security evidence before writing08-16-SUMMARY or markingPhase8 complete.

## Reviewed corrections

- d608c7e fixes catalog ratingReady using current published runtime/scenario/team/product/provider/rating/binder/authority configuration and definitions. It remains product-level metadata; actor/agency/term/risk checks remain in the real rating service. Existing draftproducts stillreportunavailable; retiringtheactualcommercialauthority makescatalogunavailable. SQL2passes in `.local/phase8-16-catalog-green/sql.trx`; strictcataloggate1093/2SQL/no skips overlaps the fixgatebelow. The newassertionfails against preservedprefixedAPI/Infrastructurebinaries in `.local/phase8-16-catalog-red2/sql.trx`, with currenttestcode; initialanalyzerfailureisnotcounted asREDbehavior.

- e0a682a fixes actual commercial policy discovery in the shared API filter, frontend product selector/label and policy/agency projection schemas. The API still applies existing current internal discovery authorization, serializable paging scope and product filtering. No new permission, response field or agency risk disclosure is introduced. SQL verifies exact owned policy discovery, Motor Trade exclusion and invalid-product refusal. Contract tests preserve agency hidden-field rejection for both Motor Trade and commercial products. Root391/frontend172/build/lint/typecheck pass.
-1919a98 replaces a stale seed-row count with exact ID/scope/version/effective-date/JSON preservation and clarifies the unsupported-configuration boundary in the old adjustment fixture. The final servicing negative asserts503 plus exactcode and no created draft. Separate configured-renewal positive tests remain. Strict `.local/phase8-16-fixes-verified` accepts1094cases/3realSQL/zero skips. Failed earlier tests remain retained and excluded.
-1919a98 also fixes retained Motor Trade discovery pagination after sorting. It follows actual next-page controls with response checks and repeated-page detection. Complete retained underwritingaggregate passed in `.local/underwriting-suite/2026-09-20T21-04-37-800Z/report.json`, including both products and nested quote/agency/client journeys. The aggregate retains its explicitly labelled terms UI fixtures; they are not represented as SQL publication proof.
- Commercial evidence browser assertions wait for the required legends rather than reading count before asynchronous content loads. The latest aggregate has now passed both capture and referral/query stages; complete issuance/servicing stages remain ongoing. New discovery assertions use actual API responses and UI navigation, not mocked policy results.
- The commercial wrapper's optional assembly override is restricted to a compiled `.local/<directory>/BackOffice.IntegrationTests.dll`. The aggregate records/checks the assembly hash and all browser source hashes and retains strict no-skip TRX accounting. This avoids rebuilding binaries locked by the original full test process. No environment check disables SQL.

## Preservation and source review

08-15 and the actual08-16 restart/readback artifacts prove retained139tables unchanged across two initializations,3policygraphs/12versions and pinned exposure unchanged after restart with canonical keys. New discovery code changes no stored policy data. Live15preview remains running; final16build is not installed there yet.

Original166/109/60 identities and future owners remain intact. All seven supplemental capture fields have current actual saved-proposal evidence; no future incident/document action has been marked implemented. Desktop and390px capture screenshots were inspected: desktop controls remain readable, and the mobile cover schedule uses its contained horizontal table. This is not a human or assistive-technology UAT claim.

## Still open

Original fullbackend baseline has two identified, now-targeted-green regression assertions; the baseline itself remains running and cannot be accepted as passing. Complete commercial and retained servicing aggregates, final current-source accounting, remaining threat/source audit and final livebuild preservation are outstanding. Imported legacy terms display still needs disposition. No final verification or CC05 completion is claimed.
