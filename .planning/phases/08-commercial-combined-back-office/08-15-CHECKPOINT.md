# 08-15 checkpoint — delivered commercial demo and final acceptance handoff

08-15 is ready for implementation/summary/state completion commits. Do not mark08-16,Phase8 orCC-05 complete. The latest handoff below supersedes all archived intermediate notes in this file.

## Latest handoff —2026-09-20T20:40Z

- Final demo `.local/phase8-15-demo-acceptance-sql/sql.trx`:7passed (complete normal API lifecycle/scenarios twice, authority4, proposal preservation/readiness/access1, all-table initialization1). Strict demo gate `.local/phase8-15-demo-verified`1098/7SQL/0skip; separate payload gate1102/11SQL/0skip overlaps, do not add totals. Full test artifact `.local/browser-evidence/commercial-capture/CoverMGA_Test_b5474052796045e4b1c3b8194cee68a6`.
- Actual local upgrade succeeded: `.local/phase8-15-demo-upgrade` has checksum-verified COPY_ONLY backup;56,867 original rows and2keys unchanged;38config rows appended. MT exact graph compare after actual restart passed. No reset/restore occurred.
- Auto-review initially rejected commercial staff authority creation. User explicitly replied **Approve this local demo grant**. Same command then succeeded: grantbea623a0-f49e-47b3-b4ef-b2dc6fc6397a for senior-underwriter@cover.example. No pending approval, no workaround.
- Normal independent agency terms adoption/context preparation passed twice: agencyAG-0000154/f3b98024-18e2-463e-96fb-e64b25161f16; clientCN-0000197/60527f0c-db57-441c-9f53-d91fab4ae14e; relationship666e823a-bb48-4fe0-8b21-4ba1e88c598a; contactcea1b85c-395b-4bf2-bd7b-1b46c019d58e; productCCv3/5c7d31e9-0e66-4829-9831-5854e5e432b6. prepare-commercial-demo-context.mjs preserves original terms and uses normal distinct author/reviewer APIs.
- Full retained lifecycle passed twice: `.local/phase8-15-live-lifecycle.log` and `...-repeat.log`. Actual policyPL-CC-0000000025/91964a43-2afe-45ee-b01e-06cfcd717795; five commercial quotesQT-MT-0000000691..695. Actual references, exact versions, conditions and exposure: `.local/commercial-lifecycle-demo-v1/references.json`/references.md. Cancellation2027-12-01T09:00Z posts1695.88credit/cash0. Proposal CLI repeat retained all5identities.
- Final preservation also passed: `.local/phase8-16-live-initialization/report.json`139tables unchanged across2initializations. `.local/phase8-16-commercial-restart` captures/compares3policy graphs/12versions plus pinned CC exposure across actual restart and unchanged original keys.
- **Current previews** `.local/phase8-15-preview-pids.json`: API72276/web61816, ports5087/3100; API `.local/phase8-15-live-final-bin/BackOffice.Api.exe`; Next `.local/next-phase8-15-live`; keys `.local/data-protection`. Prior64900/66512 and38760/79728 were actually stopped after identity/start-time checks. Verify current metadata before future stops.
- **Running full backend** exec session58412, `.local/phase8-16-backend-all.log`/directory. Command dotnet test backend/BackOffice.slnx --no-restore --logger trx. NativeSQL owned GUID DBs, current Next dist. Fresh full unit1091passed in `vilmo_DESKTOP-SCF19PJ_2026-09-20_21_13_17_net10.0.trx`; integration still running. No backend/frontend source used by it changed since compilation. New observer/aggregate scripts are not dependencies of running tests.
- Frontend172/lint/typecheck pass `.local/phase8-16-web-*.log`; root390/source4/builds pass. Generated next-env/tsconfig restored. New16 `verify-commercial-restart.mjs` actually passed; `verify-commercial-suite.mjs` syntax checked but NOT RUN. Aggregate requires COVER_SQL_TEST_CONNECTION, COVER_NEXT_DIST_DIR, COVER_COMMERCIAL_UNIT_TRX, COVER_ACCEPTANCE_NOT_BEFORE_UTC; use current unitTRX/cutoff2026-09-20T20:12Z after reviewing full backend baseline. It runs5maximal isolated UI stages with strict TRX accounting and source hashes.
- Complete15 commits then16. No agents. Review08-15-REVIEW.md. Two16 observations: legacy importedAG-DEMO-QUOTES terms-history GET422 from incomplete old snapshot (untouched; CCuses separate fully approved agency); agency catalog hardcodes ratingReady=false/unavailable caption despite actual published rating support. Review/fix without rewriting history/definitions.
- The earlier mixed `.local/phase8-15-demo-final-sql` has4pass/2test-fixture failures and is excluded. Final7 supersedes it. Final authority denial fixtures use typed EF suspension/role removal/retirement and check current authority before existing grant discovery. Final contender39,900,000.01 buildings produces actual shared-book contention.

## Archived intermediate notes (not current instructions)

## Implemented so far

- CommercialPayloadSource verifies bounded exact UTF8 source hash, closed commercial issued shape, duplicate members and effective interval.
- CommercialIncidentPayload validates exact-source property/liability subjects, selected cover, declared EL occupation and occurrence/knowledge intervals. Cancellation cannot authorize new incident cover.
- CommercialDocumentPayload builds complete per-kind commercial content and selected conditions. CommercialDocumentRequestPayload integrates it into actual first issue and servicing document/outbox writes; Motor Trade remains unchanged.
- Generated standalone incident/document schemas and OpenAPI components. No fabricated Phase9 route, logged incident or rendered document.
- Migration20260920182749_CommercialOperationalPayloads pins closed document envelope, source, template, metadata and per-kind content. Insert-only guards preserve old requests; downgrade refuses new-format history.
- CommercialDemoSeed prepares five named two-location commercial proposals through QuoteService in an already approved relationship. Current capture scope precedes retained discovery, a session lock serializes initialization, initial immutable revision markers preserve user edits. Development-only CLI `--seed-commercial-proposals-demo` accepts explicit relationship/product-version IDs. It creates no grants or issued history.
- Existing commercial contract documentation retained in full, with an appended08-15 section. Phase9 handoff and proposal command documentation added.

## Verified evidence

- RED: `.local/phase8-15-payload-red/unit.trx`,16 failing runnable tests against unimplemented builders. No compile-only RED claim.
- Latest full units: `.local/phase8-15-unit-final/unit.trx`,1091 passed/0skipped, including20 operational payload cases.
- Root390: `.local/phase8-15-root-tests.log`; contract71: `.local/phase8-15-contract-validation.log`; focused commercial contract9: `.local/phase8-15-contract-tests.log`.
- Current envelope guard + demo SQL: `.local/phase8-15-envelope-sql/sql.trx`,9passed/0skipped. Includes6 substituted document variants (same poisoned outbox/request bytes and recomputed hashes), normal exact-version issue/replay, repeat preservation/access denial, and retained Motor Trade migration reversal/reapply.
- Earlier guard SQL: `.local/phase8-15-guards-sql/sql.trx`,3passed, including both adjustment variants. Later metadata guard additions require current servicing rerun, started below.
- API final build `.local/phase8-15-api-final-build.log`,0warnings/errors; `.local/phase8-15-api-final-bin` includes new CLI flag filtering.
- Generation succeeded;422 existing operations unchanged. `git diff --check` passed.

## Excluded attempts

`.local/phase8-15-demo-negative-sql/sql.trx` had4pass/1fail: the new initializer incorrectly queried QuoteRevision.Sequence. Corrected to typed `revision.Number == 1`; current9-case run passes. Do not promote this failed report.

The contract file already existed despite the approved plan calling it new. An initial replacement was corrected before commit; `git diff --numstat` confirms43 additions/0 deletions of prior content.

## Running / next

Final servicing SQL passed2 cases in `.local/phase8-15-servicing-final-sql/sql.trx`: early renewal plus final scheduled expiring risk (includes dated adjustment). Strict `.local/phase8-15-payload-verified` gate passes1102 distinct cases,11 real SQL,0 skips, combining current9 SQL and1091 units. Native test databases are disposable GUID-owned instances.

In-progress uncommitted follow-on: `scripts/seed-commercial-lifecycle-demo.mjs` now performs journaled normal API issue for the two-location base; it pins fixture IDs, scopes every read and preserves exact pending requests. The new `RealSqlCommercialDemoNormalApiIssueAndRepeat` harness branch is being built/tested against an isolated API/web/SQL host. This follow-on is not yet verified or part of the payload commit. First build found a local variable-name collision and was corrected; current build folder `.local/phase8-15-api-demo2-bin`.

Remaining required plan15 work: complete resumable normal-service lifecycle demo orchestration and actual published references (issue/MTA/renewal/cancellation and capacity behavior), repeat initialization/history fingerprints, final review/summary/source verification/commits. The proposal initializer alone does not satisfy full lifecycle demonstration. Current docs explicitly say so. Reuse normal services and existing journal patterns; do not use test helpers as a runtime API or mark queued Phase9 work completed.

Continue sequentially inline without agents or another permission question. User already approved autonomous continuation.

## Continued normal-API verification

- Proposal/source payload work committed as c2f85b2. Follow-on scripts and tests remain uncommitted while acceptance runs.
- Normal-API issue repeat passed `.local/phase8-15-api-demo-sql/sql.trx`; adjustment repeat passed `.local/phase8-15-api-adjustment2-sql/sql.trx`; renewal repeat passed `.local/phase8-15-api-renewal-sql/sql.trx`; cancellation repeat passed `.local/phase8-15-api-cancellation-sql/sql.trx`. One case each; cancellation includes all preceding issue/MTA/renewal steps, posted credit, cash0, noMID and exact before/after exposure. Current cancellation artifact: `.local/browser-evidence/commercial-capture/CoverMGA_Test_e0fba24ddf954707b0c0ad9d4a882be5`. These owned test databases were removed; references are not live business fixtures.
- All-table repeat initialization after actual issue passed `.local/phase8-15-preservation-sql/sql.trx` (one). Proposal readiness/access/edit preservation passed `.local/phase8-15-demo-recipe-sql/sql.trx` (one) before final contender amount correction; current rerun includes that correction.
- New scripts: seed-commercial-lifecycle-demo.mjs plus commercial-demo-servicing/renewal/cancellation/referrals/report.mjs. Main supports issue/adjustment/renewal/cancellation/scenarios/full, normal authenticated commands, persistent exact-request journal and actual reference reports. Full report composition still awaits complete-stage test.
- Excluded scenario attempts: `.local/phase8-15-demo-scenarios-sql` used a configuration code instead of the published `Demo: cc conditional proof` label. `.local/phase8-15-demo-scenarios2-sql` reached real conditional responses but the proposed39m still fit actual stored headroom. Final contender is39,900,000.01 buildings atS9: own39,930,000.16 plus existing130,000.16 exceeds40m; own remains below40m. Older retained scenarios are never rewritten.
- Explicit `--seed-commercial-authority-demo` setup is being added because commercial publication intentionally grants no staff authority and the later configuration UI is not yet implemented. Separate from startup, initialization and proposal/lifecycle commands. It requires active internal admin/senior roles and current published commercial v3 authority; preserves existing revoked/expired grants. Runnable RED `.local/phase8-15-authority-red-sql/sql.trx` failed with NotImplementedException. First mixed GREEN attempt has invalid test SQL table names for two denial fixtures; corrected tests now use typed EF update/delete and test denial before discovery of an existing grant. Production helper is not implicated by those fixture errors.
- Current mixed run `.local/phase8-15-demo-final-sql` uses `.local/phase8-15-demo-final-bin`:6 cases (four authority, proposal-repeat, referral/capacity-repeat); two fixture failures known. Do not accept this whole report. Corrected test binary built `.local/phase8-15-demo-final2-bin` with0warnings/errors; rerun the authority cases after inspecting mixed-run completion. Then run `RealSqlCommercialDemoNormalApiFullLifecycleAndRepeat` and current final gate.
- Live demo unchanged. Old previews API38760/web79728 remain. Read-only Motor Trade API graph capture succeeded `.local/phase8-15-preupgrade-api` with2policy graphs. Prepared `.local/phase8-15-live-api-bin` is already stale after explicit-authority/contender edits; rebuild before any use. No live SQL migration, authority grant, agency adoption, commercial fixture or process restart performed. Native SQLCMD path `C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\SQLCMD.EXE`.
