# 08-11 implementation review

Reviewed the projection, shared service boundaries, generated contracts, product dispatch, editor state and actual SQL/browser evidence against T08-11. No unresolved HIGH/CRITICAL finding.

- The immutable base product selects the projector. Commercial input cannot become a Motor Trade change, inject issued premium or move client/relationship ownership. Closed prefixed payloads share the canonicalizer, not the Motor Trade risk shape.
- Stable subject IDs are checked against retained/current rows; duplicate targets, deleted/reused IDs and identity relocation are rejected. Explicit replacement distinguishes clearing from zero/false and preserves envelope identity. Property-only replacement retains address and protections. Cumulative slices are deterministic and obey London time, term and latest-source bounds.
- Current actor/product/client scope is held before receipt lookup. Strong ETags and current lease tokens fence fresh commands. Current issued-base checks protect commercial create/save. SQL proves stale ETags, expiry, rotated fences, revoked replay access and unchanged issued bytes/hash, exposure and financial records.
- The actual policy action creates/resumes a persisted commercial adjustment. All nine commercial editor groups reuse the capture fields and dialogs. The backend editor comparison describes the matching saved revision, not unsaved local state. The shared page dispatches both Motor Trade products explicitly and never guesses an unsupported product.
- Review found and corrected two concurrency concerns: local edits now retain their starting revision so polling cannot silently permit overwrite; in-flight reads carry a generation fence so a response started before a command cannot replace its confirmed result. An unconfirmed command retains its exact key/body/ETag through a subsequent denial until replay can be resolved with current access.
- Shared helper dialogs use “save the draft”, suitable for quote and adjustment capture. Navigation warns before discarding local edits. Missing lease/current read or invalid input disables writing; reacquisition retains local values.
- Commercial rating is explicitly gated until08-12. Renewal/cancellation remain with08-13/14. No commercial draft exposes the existing Motor Trade rating pipeline or changes issued exposure before issue. No migration or live demonstration data change was needed.

## Evidence

-78 focused units:15 commercial servicing projection,29 commercial capture and34 retained servicing cases; `.local/phase8-11-projection-unit-final/unit.trx`.
-4 current commercial/retained Motor Trade SQL cases: `.local/phase8-11-sql-current/sql.trx`,2m9s, zero skips. This rerun includes the corrected editor response serializer.
-389 root checks,170 web checks,71 contract checks; `.local/phase8-11-root.log`, `.local/phase8-11-web-tests.log`, `.local/phase8-11-contract-validation.log`.
-Backend build `.local/phase8-11-build-final.log`: zero warnings/errors. Web production build (including typecheck) and lint: `.local/phase8-11-web-build-final.log`, `.local/phase8-11-lint-final.log`.
-First full browser run passed all nine groups, location/wage/loss add/edit/remove, explicit BI/contract-works clearing, exact saved slices, negative HTTP cases, concurrent-edit/lease recovery, desktop/390px layout and retained Motor Trade creation. Strict final accounting excludes this preliminary run.

- An extended browser run exposed a real response-contract mismatch: the shared issue record omits null question IDs, while the editor schema requires the nullable field. The editor now explicitly projects that field in its response; the existing schema is retained. Another earlier run hit an unawaited form load; the browser now waits for the draft selector. Earlier diagnostic reports are excluded from final strict accounting.

-Final browser `.local/phase8-11-browser-c54ee05a-c398-4b7f-aaf5-846372586b8a/sql.trx`:1 case,6m34s. Artifacts `.local/browser-evidence/commercial-capture/CoverMGA_Test_bc19a7bc68ac437db1ec591c70399963/`. Actual draft/editor responses pass generated OpenAPI validation. All nine groups save/reload; location/wage/loss add/edit/remove, BI/contract clearing, foreign/motor/duplicate/stale HTTP rejection, local revision/lease recovery and committed lost response → denied retry → exact successful retry pass. Snapshot and complete district observation stay equal. SQL compares exact stored proposal and issued hash, and retains one exposure version/decision and zero servicing cycles. Desktop and390px screenshots inspected, with no page overflow.
-Strict `.local/phase8-11-verified`:83 unique passing backend cases,5 real SQL, zero skips. All four owned source controls now point to the real persisted adjustment flow; original60-control denominator and downstream ownership remain unchanged.

## Remaining ownership

08-12 owns commercial adjustment rating/referral/evidence/carrier/terms/acceptance, actual draft exposure projection and atomic issue.08-13/14 own renewal/cancellation. Business and assistive-technology UAT, hosted CI and Docker execution are not claimed. Sales-funnel files and the live demo database/processes/keys remain preserved.
