---
phase: 07-policy-lifecycle-and-history
plan: '11'
status: complete
completed: 2026-09-18
requirements: [POL-07]
requirements_completed: []
---

# 07-11 — Renewal preparation and supplied experience

Both Motor Trade products now prepare real owned renewal drafts, store supplied
claims figures and actual file bytes, retain exact underwriting reviews, rate the
full future term, and require current senior authority for UW-31. Missing,
unreviewed and zero-denominator experience remains unresolved rather than zero.
The source quote and issued policy stay unchanged.

## Implementation and review

- Strict pre-expiry/current-known risk selection; configured six/twelve month
  London terms; anniversary/leap rules; prospective product/binder/commercial
  eligibility; cancellation/overlap rejection; immutable preparation versions.
- Append-only supplied experience, file associations and review grants. Current
  review authority is rechecked before rating/readiness/replay. Real fictional
  fair-value files/assessments and approved agency terms are exposed; arrears is
  explicitly unavailable until its finance owner implements it.
- Versioned format2 renewal rating input and compound SQL preparation/experience
  pins. Full-term price, one fee and configured experience loading, including
  unchanged risk. Adjustment format1 bytes and delta pricing are preserved.
- The unchanged-risk common effective date must still equal prepared inception.
  A failing both-product native SQL test reproduced the former bypass; the guard
  now rejects it. Renewal cannot prepare an adjustment document template.
- Four-stage navigation, saved preparation/evidence/review, actual risk comparison,
  rating and referral decisions are wired to APIs. Editing remains lease/ETag
  controlled; uncertain commands retain exact files, keys and fences. The
  invitation/acceptance stages expose their pending 07-12 implementation honestly.
- GET renewal preparation retains saved choices and current blockers. Strict
  no-store/ETag contracts cover 409 operations. New feature-local services, guards,
  read models and endpoints refine the plan's suggested consolidated filenames.

## Measured verification

Runs overlap; counts must not be added as distinct coverage.

- Foundation/eligibility/preparation evidence and commits are retained in
  07-11-PROGRESS.md: dc34b13,953c623,11c4de9,2f5a385.
- e639c5a rating input gate: 37 unit +2 hosted SQL =39 cases.
- ef95510 renewal-cycle gate:37 unit +6 SQL/API =43 cases. Both products prove
  full unchanged-risk rating, UW-31-information non-approval, evidence review,
  loading, senior-only decision, authority revocation, forged SQL input rejection,
  and issued snapshot preservation. Existing adjustment APIs/migration survive.
- Workspace API:37 unit +2 SQL/API =39, `.local/phase7-11-workspace-api-final`;
  14 captured HTTP responses validate against four strict schemas in
  `.local/phase7-11-workspace-responses` (16:00UTC cutoff).
- Final inception/template regression:25 unit +2 native SQL =27, no skips,
  `.local/phase7-11-inception-final`. Template guard also passes the earlier two
  product cases in `.local/phase7-11-renewal-template-final`.
-32 frontend helper tests;48 final API/source tests; OpenAPI lint passes with
  41 pre-existing schema warnings. Final typecheck/lint and Release API/Next
  builds pass. Backend build has zero warnings/errors; git diff check passes.
- Actual Chrome journeys for both products finished2026-09-18T16:56:30Z:
  `.local/browser-evidence/renewal-preparation/report.json`. Six/twelve months,
  actual uploaded/downloaded bytes, supplied figures and reload, review, full-term
  rating, senior UW-31 approval, saved Road Risks risk amendment, stage navigation,
  responsive width and unchanged issued graph all pass; no browser errors.
  Desktop/mobile screenshots inspected. Browser verification found and fixed
  grid overflow; earlier failed runs remain distinguishable from the final report.
- Three additive migrations preserve115 existing demo table counts/hashes:
  `.local/phase7-11-preservation-{before,after}.txt`. Targeted seed changes only
  renewal settings and new fair-value tables; no full reseed/reset. Owned previews
  are recorded in `.local/phase7-11-preview-pids.json`.

## Boundaries and continuation

Source-field ownership for issued invitation documents, lifecycle timeline and
renewal issue is refined to07-12, matching its approved task rather than marking
those pending actions complete. POL-07 stays open across07-12. No real delivery,
payment, hosted CI, Docker or human UAT is claimed. Sales funnel unchanged.
Continue07-12 inline;10/16 Phase7 plans and59/65 implementation plans complete.
