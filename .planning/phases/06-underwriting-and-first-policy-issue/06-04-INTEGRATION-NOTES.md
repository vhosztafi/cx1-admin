# 06-04 integration preparation

Read-only preparation while06-03 regression runs. No06-04 implementation or
acceptance is complete yet. Approved06-UI-SPEC and prototype styling govern.

- Existing API client/types are lib/quotes.ts, not quote-api.ts. QuoteReceipt uses
  QuoteActions, QuoteHistory/QuoteProposalDetails, useQuoteResource and primitives.
  Current receipt status is draft-versus-withdrawn and must become exhaustive.
- QuoteService now supplies actual CanClone/CanWithdraw independently of CanSave
  for progressed states; consume those capabilities without client role guesses.
- Six03routes provide rate/submit/return-to-draft/explicit refresh/assessment/rating;
  rating jobs use GET/jobs/{id} with quote subject scope. Recovery additionally
  requires integration-retry plus quote-rate (combined internal roles).
- Complete the read projection needed by UI during this slice, refining the closed
  contracts and covering the backend extension with its required full gate:
  (1) assessment should expose persisted current jobId so pending/error details
  survive reload; (2) discover historical rating IDs through a scoped paginated
  quote rating list, not local remembered IDs; (3) provide eligible published
  refresh options including actual current approved agencyTermsVersionId/version
  for the required confirmation. QuoteProducts currently exposes product IDs and
  labels but not agency terms IDs. Do not present raw GUID entry to business users.
  (4) expose actual routed team label/ID for the persisted submission if displayed.
  Existing internal UW can inspect agency terms, but Servicing must receive only
  the owned quote's safe commercial/version choices through the quote boundary.
- Refresh only selects same-product published versions, current independently
  approved terms and whole-term eligibility. The old saved proposal remains intact;
  changed pins can invalidate vehicle lookup/evidence fingerprints. UI must show
  current readiness instead of claiming proof/provenance survives every refresh.
- The preserved demo now has additive v2 products/configuration but existing
  approved agency terms are untouched. Browser/demo fixtures need an explicitly
  approved additional terms version (or a separate fictional agency), never
  silently grant a new product version by editing old terms. Agency terms currently
  permit3entries, so do not assume four retained/new product-version grants can
  fit one proposal. A new current terms version can intentionally require old
  drafts to refresh; retain their history and make the refresh path usable.
- Stock/premises/tools requested sections require explicit selections. Preserve
  saved input rather than fabricate missing limits. Existing capture proposal
  parser retains these fields; the wizard still needs source-aligned controls.
- frontend-design skill has been read in preparation; announce its actual use when
  starting UI implementation. User-approved IBM Plex/prototype layout overrides
  generic typography/style suggestions. React/performance and browser skills are
  still to be read/applied as appropriate. Do not start the next wave until03gate.
