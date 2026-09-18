# 07-11 renewal preparation — partial foundation

Foundation commit: `dc34b13`. This is not a completion summary. Phase7 remains9/16 plans complete,58/65
overall. No requirement or source control is marked complete by this foundation.
Eligibility/configuration follow-up commit: `953c623`.
Persisted preparation follow-up commit: `11c4de9`.

## Implemented

- Pure London-calendar renewal term calculation, configured shorter months,
  leap/month-end clamping and explicit rejection of invalid DST anniversaries.
- Typed observed experience, exact unrounded loss-ratio threshold, unknown
  denominator/evidence states, and configured loading on the full next term's
  risk price. Annual maximum, pennies, tax, commission and one configured fee
  remain enforced. This pricing primitive is not yet wired to the rating worker.
- Additive migration `20260918142908_RenewalPreparationEvidence`: immutable
  draft-owned experience/file associations and reviews of exact experience
  versions. Compound ownership, ordered history, current authority and latest
  facts are enforced by SQL, including rejection of history rewrites.
- Immutable product-owned evidence bytes and fair-value assessments, with
  product/version/binder ownership, screening/hash/size checks and published
  coverage of the assessment validity. No fabricated quote/draft anchors.
- `RenewalPreparationService` provides actual upload, append-experience, review
  and read services. Current identity/capability and review grants precede receipt
  replay; rowversion and editing lease fence new writes. Changed figures and
  changed reviews invalidate existing cycles. A new experience version never
  inherits a previous version's evidence review.
- Strict versioned renewal settings and missing-only fictional seed now supply
  configured term lengths, ratio threshold/loading/fee and invitation/lapse days.
  Actual retained fair-value bytes identify the bundled product and binder.
  An immutable initialization marker preserves published operator changes.
- Scoped renewal preview selects the known snapshot strictly before expiry,
  rejects cancellation/overlap and pins published product, binder and commercial
  terms covering the next term. Future approved commercial terms can apply to
  renewal while current quote capture keeps today's terms. Current distribution
  authority is still independently required. Fair-value checks read actual owned
  assessment/evidence; broker arrears remain explicitly unavailable.
- `RenewalPreparationVersion` now retains exact draft/base ownership, calendar
  term choice and product/binder/commercial/rule/fair-value pins. The preparation
  command holds current permissions, ETag and lease, invalidates old cycles,
  appends a new proposal revision with server-derived inception and records an
  immutable preparation version. SQL independently rejects stale/cancelled
  bases, overlap, foreign pins, invalid calendars and history rewrites.
- Renewal editor projection reads the retained preparation term. Configured
  six-month cover projects a short-period intent while retaining risk item IDs;
  the original issued snapshot and previous preparations remain unchanged.
- Authenticated HTTP routes now expose preview, preparation, experience upload,
  save, read and exact-version review, backed by the actual services and DI.
  Closed input DTOs reject arbitrary pricing, amounts are canonical decimal
  strings, and responses are no-store with strong current ETags. Existing scoped
  file download serves the retained experience bytes.

## Path/design refinements

Feature records/model are in `RenewalPreparationRecords.cs` and
`RenewalPreparationModel.cs`, following existing partial DbContext conventions.
The existing servicing evidence association requires a rating; reusing that
association for pre-rating experience would create a circular workflow.
`RenewalExperienceEvidence` therefore binds the existing immutable draft-owned
file bytes independently of a rating. Reviews bind an immutable experience
version; they do not approve future-term underwriting or waive UW-31.

Product-wide fair-value evidence cannot honestly belong to an invented quote or
servicing draft. `ProductEvidenceFileVersion` uses the same actual-byte/hash/
screening storage pattern with explicit product/version/binder ownership.
The planning-only `/drafts/{draftId}/experience` spelling is replaced by
`/drafts/{draftId}/renewal/experience`; operation ID `recordRenewalExperience`
is retained. Generated definitions/routes for existing implemented operations
were structurally compared against HEAD and remain identical.

## Evidence and remaining work

Red runs: missing rule types, missing registered model, and missing command/
review methods were observed before the respective implementations. Initial
sandboxed SQL access failed at connection; the authorized isolated-database
rerun reached the expected missing-model failure. That connection failure is
not counted as a domain test.

Focused intermediate runs passed9 rule cases, then12 pricing cases,2 storage
cases for both products, and1 command/review case. They overlap final evidence
and must not be added together. Final foundation evidence: **13 unit +3 real
SQL =16 passing cases**, no skips, `.local/phase7-11-foundation-final`; repository
TRX gate passed with cutoff `2026-09-18T14:35:00Z`. `git diff --check` passed.

Configuration/eligibility follow-up: **26 unit +3 pure chronology +8 real SQL
=37 passing cases**, no skips, `.local/phase7-11-eligibility-final`, same cutoff.
This includes both-product preview and prospective commercial terms, original
quote eligibility/refresh regressions, exact experience services, seed preservation
and storage ownership. Focused red future-terms cases reproduced rejection before
the shared optional commercial-inception extension. The first seed test omitted
underwriting initialization; that fixture was corrected before the passing rerun.
Intermediate `.local/phase7-11-seed-final` contains30 passes and overlaps this run.

Persisted preparation follow-up: **60 unit +3 pure chronology +8 real SQL =71
passing cases**, no skips, `.local/phase7-11-preparation-final`, repository gate
cutoff `2026-09-18T15:00:00Z`. Includes the full existing servicing-proposal unit
suite plus annual-to-short preparation changes, editor readback, immutable
preparation and exact retries. Additive migration:
`20260918151032_RenewalPreparationVersions`. Both new migrations have been used
only in isolated SQL test databases so far.

HTTP follow-up: **60 unit +2 real SQL/API =62 passes**, no skips,
`.local/phase7-11-api-final`, gate cutoff `2026-09-18T15:00:00Z`. Both Motor Trade
products exercise authenticated preview/preparation/upload/save/review/download,
CSRF and missing-version failures, unknown pricing fields, canonical money,
exact retry and current grant revocation before review replay. Twelve actual
responses in `.local/phase7-11-api-responses` pass generated schemas and reject
extra properties (`.local/phase7-11-api-response-contracts.log`).
**61 JavaScript contract/source checks** pass (`.local/phase7-11-contract-green.log`).
OpenAPI lint passes with41 warnings (`.local/phase7-11-openapi-lint.log`). These
focused runs overlap earlier evidence and must not be summed as distinct tests.

Rating envelope follow-up: **37 unit +2 real SQL/hosted API =39 passes**, no
skips, `.local/phase7-11-rating-input-final`, gate cutoff `2026-09-18T15:00:00Z`.
Version2 inputs carry exact renewal preparation, experience, review and fair-value
identities plus the actual experience and configured loading rules. Unchanged
renewals need no invented change IDs. Full-term premium and annual comparison
are distinct, including six-month London civil-day pricing for both products.
Missing/unreviewed/rejected experience remains unresolved and cannot introduce
the approved loading. Version1 adjustment serialization omits the new optional
context and round trips unchanged. The shared provider dispatch now uses the
validated envelope calculator; both existing hosted adjustment journeys pass.
The request/SQL layer still refuses renewal cycles until their ownership guards
and current-scope checks are implemented. This is not renewal end-to-end proof.

Renewal cycle follow-up: **37 unit +6 real SQL/API =43 passes**, no skips,
`.local/phase7-11-renewal-cycle-final`, gate cutoff `2026-09-18T15:30:00Z`.
New additive migration `20260918155123_RenewalRatingSourcePins` retains compound
draft/preparation/experience/review ownership. SQL also compares the hashed
envelope with stored facts, settings, full-term dates and current term-end risk.
The unchanged-risk renewal request, provider worker, read model, evidence
projection and referral flow now run for both products. Supplied/reviewed
experience supersedes previous cycles; reviewed losses above the configured
threshold apply the full-term loading and generate UW-31. Missing experience
generates non-approvable UW-31-information. Both service and SQL reject ordinary
underwriter approval of UW-31; actual current senior approval passes. Revoking
the experience reviewer's grant makes the rating inapplicable and blocks replay
of its previous approval. Direct SQL rejects rehashed forged preparation,
accepted-evidence flags, term dates and fees. Existing hosted adjustment APIs
and latest additive migration downgrade/upgrade preserve the original issued
snapshot. The older storage test now downgrades only the latest migration:
today's published servicing templates cannot fit the pre-servicing template enum.
An initial new-trigger physical table-name error was fixed before the final run.
**61 JavaScript checks**, infrastructure build (zero warnings/errors), and
`git diff --check` pass. This migration is still isolated-test-only; the shared
demo has not been changed. These runs overlap preceding evidence.

Still required: renewal UI preparation/readback contracts and HTTP journey proof,
four-stage UI/browser verification, shared-database migration
with preservation evidence, and final plan source/validation updates. No new
browser journey is claimed. The shared demo database and
running07-10 preview have not been migrated or reseeded by this foundation.
