# 07-11 renewal preparation — partial foundation

This is not a completion summary. Phase7 remains9/16 plans complete,58/65
overall. No requirement or source control is marked complete by this foundation.

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

Still required: term-end/cancellation/overlap eligibility, published renewal
configuration and pins, fair-value demo evidence seed, current broker checks,
rating-cycle/worker/referral integration, strict public DTOs/DI/routes and HTTP
negative tests, four-stage UI/browser verification, shared-database migration
with preservation evidence, and final plan source/validation updates. No new
public endpoint or browser journey is claimed. The shared demo database and
running07-10 preview have not been migrated or reseeded by this foundation.
