---
phase: 07-policy-lifecycle-and-history
plan: '15'
status: complete
completed: 2026-09-19
requirements: [POL-01, POL-02, POL-05]
requirements_completed: []
---

# 07-15 — Policy record, history, reconstruction and cloning

Both Motor Trade products have scoped issued history, same-policy comparison, arbitrary effective/known-at selection, stable driver/vehicle histories and cloning to a fresh incomplete quote. Reconstruction retains an immutable request/manifest; Phase9 owns rendering.

## Implementation and boundaries

Current policy and destination relationship scope is checked before read or receipt replay. Cloning requires current capture eligibility and confirmed current agency terms for the same client/agency relationship; it remaps quote/revision/risk identities and excludes term intent, acceptance, rating, referral and authority state. Source lineage is persisted and independently protected in SQL. Reconstruction binds exact E/K/version/hash or explicit no-cover, records its candidate manifest and immutable outbox request, and rechecks selection under the policy command boundary. Strong policy If-Match, CSRF, no-store and frozen idempotent retry are wired through actual routes.

Policy history resolves agency/provider/actor and decision binder/authority from owned records. Actual version document requests and originating quote/draft decisions are navigable. Historical risk responses contain the exact stored item, cover and permitted-driver basis from each version. Driver ages/licence years use that version's effective London date. Overview shows recorded risk counts and cumulative snapshot charges separately from signed transaction movements; omitted data is not interpreted as zero. Cancellation retains pre-cancellation cover charges and its distinct credit.

Additive migration20260919074413_PolicyHistoryStorage introduces PolicyReconstructionRequest and PolicyQuoteClone, exact compound ownership and independent immutable/selection/lineage guards. Planned large file paths were refined into feature-local partial PolicyHistoryService files, PolicyHistoryEndpoints and PolicyHistoryRecords/Model. No old migration or sales-funnel files were rewritten.

All22 controls owned by15 have measured functional adaptations. All191 field occurrences owned by15 have recorded dispositions in07-15-FIELD-DISPOSITIONS.md and07-SOURCE-FIELDS.json. This is not191 separate browser cases: optional absent fixture values are covered by typed contracts/source review, while populated risk/cover/financial values and navigation have actual browser readback. Existing evidence/referral/renewal assessment workflows are reached through the originating quote/draft, without invented source fixture statuses. Assignment/product-review/complaint and unsupported wording/VIN values remain explicitly unavailable. The original137 controls/393 occurrences remain; the All-tasks control and24 field occurrences are reconciled to their already-approved Phase9/10 module owners. POL-01 remains compound and is not marked complete.

## Measured verification

- .local/phase7-15-close-gate:15 unique passing cases (9unit +6realSQL/API/browser), no skips, verified by assert-test-results.ps1 after2026-09-19T16:00Z. Reports are unchanged originals from close-unit and risk-context-reviewed.
- .local/phase7-15-browser/2026-09-19T16-43-43-097Z-40460/sql.trx:2 supplemental final browser passes, overlapping the gate's browser identities. They additionally check Overview counts including omitted premises, five cumulative amounts, declared business fields and every populated cover-section code/level/limit/excess. No extra unique test count is claimed.
- .local/phase7-15-close-contracts-final.log:48 API/source checks pass; .local/phase7-15-close-web-tests.log:148 frontend tests pass. Lint/typecheck, isolated Next build, Release API build and production demo build pass.
- Earlier12 actual HTTP captures in .local/phase7-15-fidelity-responses-v2 passed closed schemas; newly added historical-context properties are checked against the exact stored JSON through the actual API and in browser. All changed schema components compile in the contract suite.
- Negative coverage includes foreign-policy compare, a genuinely different agency clone, stale selection/terms/ETag, current identity changes before replay, active draft exclusion, SQL update/delete rejection, fresh risk identities, and one saved clone after a deliberately lost response.

Failures were retained: the initial risk-context test filter matched zero and was not counted; corrected2-case red run failed at the missing cover property before implementation. The first Overview browser run failed on omitted Road Risks premises; the UI and assertion now distinguish unavailable from an explicitly empty list. Final checks above pass.

## Preservation and handoff

Migration application preserved130 existing table/setting fingerprints. Final preview refresh preserves132 including both new tables (.local/phase7-15-close-demo-before.txt and after.txt). No reset/reseed or policy history rewrite occurred. Authenticated smoke confirms both previews return200 and four historical versions expose their own cover context. Current owned previews:API83488/web75048, ports5087/3100, .local/phase7-15-preview-pids.json.

Continue07-16: deterministic additive demo scenarios, full backend/retained browser acceptance, repeated initialization/restart preservation, final source/security/UI review and Phase8 handoff. Human business/assistive UAT, hostedCI and Docker runtime remain unperformed. No real providers, messages or payments are sent.
