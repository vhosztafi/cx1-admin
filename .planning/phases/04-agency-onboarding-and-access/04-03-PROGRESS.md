# 04-03 implementation progress

Storage/service commit: **a825a83**. API/readiness commit: **0c9f605**. Latest full backend regression: **206 passed, 23 real-SQL scenarios, zero failures/skips**, verified by `assert-test-results.ps1` against `.local/phase4-evidence-api-full`. A final targeted API rerun after correcting evidence activity labels passed in `.local/phase4-evidence-api-activity-final`. Frontend18, contracts75, ESLint/typecheck/production build and the built six-stage browser regression passed. 324 operations and949 controls remain mapped.

Plan remains **in progress**. Do not create a completion summary or count the plan complete yet.

## Implemented storage/service foundation

- `AgencyEvidenceRules` validates allowed PDF/PNG/JPEG/text signatures and filenames, 10MiB actual byte limits, SHA-256, canonical relevant-input snapshots/fingerprints, explicit demo pass/refer/unavailable checks, manual attestation declaration requirements and evidence freshness. File screening is labelled demo-cleared; it is not a malware scan or regulatory certification.
- `AgencyEvidenceFile`, `AgencyEvidence`, `AgencyCheckAttempt` own immutable SQL rows. Composite agency/file and agency/evidence FKs prohibit cross-agency association. Identity ordinals order attempts even with repeated/frozen clocks. File bytes stay in SQL and are absent from command receipts.
- Migration `20260914101044_AgencyEvidence` adds bounds/state/hash/provenance constraints and update/delete rejection triggers. Attestation notes explicitly require NOT NULL as well as nonempty, avoiding SQL CHECK's unknown-value behavior.
- `AgencyEvidenceService` implements atomic upload, attestation and deterministic check commands with current internal permission before replay, parent ETag/lock, audit/activity, ID-only receipts and no external calls. Actor/file/rule/time/result authority is server-owned. Relevant old evidence stays immutable and becomes stale when the input or rule version changes.
- Additive `agency-compliance` setting version has explicit fictional PI minimum 1,300,000.00, TOBA2026.1, 90-day demo check validity and per-kind scenarios. Real dates use Europe/London for expiry boundaries. Existing setting versions are not overwritten.
- Unit evidence cases: 11 passed. SQL service scenario passes file bytes/hash, replay, retained failed then passed attempts, frozen-clock ordering, FK ownership rejection, immutability, reseed preservation, stale writes, permission denial and current-role revocation before receipt.

## Implemented API and readiness slice

- Added `AgencyActivationRules`, with48 unit cases covering required and conditional identity/address/contact fields, representative restrictions, exact commercial values, dates, all8 evidence kinds, current declarations and unavailable product/user prerequisites. Maximum checklist size is80 in OpenAPI to preserve every actionable field path.
- `AgencyEvidenceService.Validate` reads latest evidence by immutable ordinal and rechecks current input fingerprint, rule version, expiry, PI/TOBA requirements and manager eligibility. It requires an aggregate transaction. GET agency holds RepeatableRead while reading the parent/details/readiness, so concurrent evidence writes cannot mix versions. Product distribution eligibility and broker-administrator staging remain explicitly unavailable until04-05/06; readiness cannot bypass future approval.
- Implemented bounded multipart upload, attachment/nosniff/no-store download, scoped paged file/evidence/check lists and individual authorized reads. JSON bodies are bounded and strict. POST validate is fresh and uncached, with CSRF and current ETag. Other commands return ID-only receipts plus ETag/Location.
- Multipart section limit includes the file plus two metadata fields (3 sections). Header-only CSRF prevents the antiforgery layer from reading uploads before endpoint bounds. SQL/API tests cover missing CSRF, UW write denial/read access, forged fields, malformed/oversized files, byte-preserving download, no bytes in lists, cross-agency file access, replay, current/stale readiness, refer/pass/unavailable attempts, changed-input invalidation, scoped cursors and accurate evidence activity summaries.
- OpenAPI mutation/read contracts and canonical DATA-MODEL match actual persisted hex hashes, content field, identity ordering and synchronous immutable demo attempts. All generated controls still map.
- Local demo was additively migrated and seeded, preserving fictional history. Browser regression passed all six stages, retry, stale edits, abandonment, URL filters,314px rail and390px viewport. Agency detail displays failed/stale/expired checklist labels accurately. Previews/tests were stopped at this checkpoint.

## Required next work

1. Implement `agency-evidence.tsx` and wire stage1 register results, stage4 upload/attestation/check results and stage6 live checklist to these APIs. Existing wizard still has unavailable-action copy for evidence; update it only alongside functioning controls. Forms must preserve unsaved inputs and exact uncertain commands, including File/form metadata on retry. Refresh the parent ETag after each evidence mutation.
2. Checklist links should navigate to the actual owning saved stage. Preserve 314px rail, blue header and source controls. Current overview readiness is live, but evidence action UI is not implemented.
3. Add frontend evidence-state tests and built browser acceptance for format-only denial, upload/attestation, demo check refer/retry, stale evidence after relevant edits, protected downloads and retained error states. Perform source/desktop/mobile/keyboard/dialog review and fix gaps.
4. Complete full regression gates and review after UI changes, then 04-03-SUMMARY and advance to04-04. Do not mark04-03 complete yet. Later04-05/06 must replace unavailable broker-admin/distribution dependencies with actual current scoped rows and independent approval.

No real providers/messages, external broker identity or sales-funnel changes. Evidence API behavior is verified; evidence UI and complete plan acceptance remain pending. No human UAT or hosted CI is inferred.
