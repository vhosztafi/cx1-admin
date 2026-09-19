# 07-15 — Policy history, reconstruction and cloning

Started after07-09/14 completion commit4fe20e5. Continue inline; no new command or routine approval needed. Plans15/16 remain incomplete.

## Prerequisite implementation

PolicyHistoryRules validates all supported issued snapshot formats before returning public declarations/comparison. Stable-ID QuoteRevisionDiff is reused. Policy-to-quote capture removes calculated premiums, provenance, issued sections/endorsements/warranties, driverBasis and original term dates, then reuses QuoteLifecycleRules for all identity/reference remapping. New capture is deliberately incomplete.

Four both-product unit cases first failed for missing PolicyHistoryRules, then passed in .local/phase7-15-history/unit.trx. These do not prove SQL/API/UI cloning.

PolicyHistoryService comparison holds current PolicyScope then resolves both version IDs within that policy before parsing/diffing. History uses shared PolicyTemporalSelector and explicit selected/not-yet-known/not-yet-effective/different-term/superseded classifications; no saved drafts are candidates. SQL tests first failed for absent service; real two-policy scope and current-identity checks running in .local/phase7-15-read. No public route or demo migration has been activated for15.

## Next storage/API choices

- Add immutable PolicyReconstructionRequest: policy/term, effective/known cutoffs, nullable selected version and hash (paired), selected coverage state, full canonical manifest/hash, actor/reason, outbox work. Compound version/term/policy FK and source/hash/cutoff guards. Keep renderer pending forPhase9; no generated PDF claim.
- Add immutable PolicyQuoteClone lineage: source policy/version/hash, new quote/revision, actor/reason and stable source-to-new risk identity map. Compound source and destination ownership FKs. Quote has fresh IDs and no current underwriting cycle, acceptance, referral, provider result or private flag.
- Clone defaults to current policy client/agency relationship. Target must be currently authorized and same client/agency; cross-agency transfer is outside this policy clone command. Explicit current destination terms ID is confirmed and rechecked before replay; published compatible capture configuration required. No old term is carried forward.
- History/comparison/risk-item readers use policy-read/no-store and authorize every referenced ID. Reconstruction uses the existing policy-draft-write capability and command receipt boundary; cloning additionally requires quote-capture and target relationship scope. Strong If-Match and idempotency for mutations, no editing lease for an immutable selected policy version.
- Existing planned /terms/{termId}/versions and /terms/{termId}/as-at/export contracts must be reconciled with actual handlers, not duplicated. Add scoped policy history/compare/stable risk-item and clone metadata/command routes with strict closed DTOs.
- Complete transaction/obligation/document-request links, stable driver/vehicle details, cutoff explanation, same-policy comparison, reconstruction receipt and incomplete-quote readback in actual browser. Phase9 generic task/message/claim/document processors andPhase10 cash remain their owners.

## Execution update — 2026-09-19

The additive PolicyHistoryStorage migration and independent SQL guards exist and pass isolated tests; the demo has not been migrated. Reconstruction commands retain exact selected E/K/version/hash manifests and pending outbox work. Policy clones retain source lineage, require confirmed current destination terms, remap risk identities and create incomplete quotations. Same-policy comparison and current identity checks run before reading or replaying.

HTTP routes and DI are implemented for policy history, term versions, comparison, driver/vehicle history, clone terms/command, reconstruction request/list. Existing planned global-version clone/compare paths were replaced with policy-scoped routes while retaining operation IDs. Reconstruction's planned PDF response was corrected to the actual retained request contract; Phase9 owns rendering.

Measured prerequisite checks:
- .local/phase7-15-history/unit.trx: 4 passed unit cases.
- .local/phase7-15-http-readback-v2/sql.trx: 2 passed both-product SQL/HTTP scenarios including CSRF, malformed requests, replay and saved request readback.
- .local/phase7-15-http-responses-v2: 12 real HTTP responses passed closed-schema validation.
- .local/phase7-15-risk-sql/sql.trx: 4 passed SQL/API cases, including immutable storage and actual driver/vehicle history reads.
- .local/phase7-15-api-contract-test-v2.log: 44 passed API contract tests. Registered the previously omitted issued-cancellation external schema in the shared test loader.
- .local/phase7-15-openapi-risk.log: OpenAPI valid,45 warnings.
- .local/phase7-15-web-typecheck-risk.log and web-lint.log: TypeScript and lint passed before later browser fixes.
- .local/phase7-15-web-build-v2.log: isolated Next production build succeeded. COVER_NEXT_DIST_DIR enables .local/next-policy-history without replacing the live demo build. Next-generated next-env.d.ts/tsconfig changes must be restored before commit.

History/comparison, clone/reconstruction controls and stable driver/vehicle panels are connected to policy-record.tsx. The actual both-product browser run now passes (.local/phase7-15-browser-driver-v6.log): changed issued-version comparison, stable driver/vehicle histories, cancelled-cover and no-cover reconstruction, URL-preserved cutoff reload, fresh incomplete quote and exact lost-response recovery. SQL readback confirms one clone and two retained requests per case. Earlier failing runs found test import, accessible label, datetime input formatting, tab selection and browser route-cleanup problems; fixes are included. Local validation no longer freezes a form before any command is sent.

Latest reviewed evidence:
- .local/phase7-15-history-gate-20260919 contains unchanged original unit/SQL/browser TRX reports: assert-test-results verified10 passing unique cases, including6 real-SQL scenarios, no skips, after2026-09-19T08:30Z.
- .local/phase7-15-unit-final/unit.trx:4 passed.
- .local/phase7-15-sql-final/sql.trx:4 passed, including lineage update/delete rejection and metadata-rich history.
- .local/phase7-15-final-responses:12 fresh responses passed closed-schema checks.
- .local/phase7-15-api-contract-final.log:44 passed; .local/phase7-15-web-lint-final-v2.log and web-typecheck-final.log passed. Lint excludes isolated generated .local output, like the existing .next exclusion.
- .local/phase7-15-web-build-v5.log passed. Only an opt-in build directory was added to next.config.ts; generated next-env.d.ts/tsconfig changes were restored.
- Final browser evidence: .local/browser-evidence/policy-history/CoverMGA_Test_38e2af92360d4ba5bbf7443df34c1f2f and CoverMGA_Test_83c3caa7b5594bef9c525248d7e97663. The cropped requests-mobile.png was visually checked and is readable.
- Review added explicit rejection of a null quote creator in the new, not-yet-demo-applied migration guard. The confirming SQL rerun .local/phase7-15-sql-reviewed passed all4 cases with no skips.

History queries project metadata without loading every snapshot; actor names, transaction invoice amounts and document-request counts come from owned stored records. Existing adjustment/renewal actions now select the corresponding persisted draft flow; agency navigation uses the real record. No source control/field has yet been marked verified for15. This is an implementation checkpoint, not plan completion.

Current verified demo remains07-14 API19460/web46256,5087/3100; .local/phase7-14-preview-pids.json. Preserve existing data and sales-funnel files. Remaining work: complete source fidelity (215 fields and23 controls assigned15, including explicit dependent-module dispositions), contextual document/obligation links and any source-display gaps, migrate/demo-preserve15 after review, close15, then execute16. Plans15/16 remain incomplete; no SUMMARY or phase completion claim yet.

## Fidelity checkpoint — 2026-09-19T09:40Z

Supersedes earlier demo/process details: history migration applied preserving130 prior fingerprints; refreshed fidelity builds preserve132 including new tables. Current API25936/web82308, ports5087/3100. Clean gate15 unique cases (9unit/4SQL/2browser), no skips.148 frontend and48 API/source checks pass;12 fresh HTTP responses validate. All22 controls assigned15 are verified;24/191 field occurrences reviewed,167 still require disposition. See07-15-SOURCE-REVIEW.md for exact evidence and outstanding IDs. No15SUMMARY yet;16 has not started.
