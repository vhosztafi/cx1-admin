# 08-15 execution checkpoint — operational payloads and proposal preparation

Plan remains executing; do not mark08-15,08-16 orCC-05 complete. No live demo database migration, reset, key change or process restart has occurred.

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
