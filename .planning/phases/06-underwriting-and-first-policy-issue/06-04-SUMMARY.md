---
phase: 06-underwriting-and-first-policy-issue
plan: '04'
status: complete
completed: 2026-09-16
implementation_commit: da15815
requirements_completed: []
---

# 06-04 — Quote overview, rating and revision UI

Implemented in `da15815`. Both Motor Trade products now expose persisted rating,
premium components, factors, provenance, requirements and paginated result history.
Source-aligned requested stock/premises/tools selections save actual amounts and
targets through immutable capture revisions. No historical defaults are invented.

## Delivered behavior and path refinements

- Rate/re-rate, submission, return and explicit published-version refresh use
  frozen key/body/ETag requests, current-account checks, uncertain retry protection,
  retained stale reasons and keyboard focus containment. Clone/withdraw/revision
  comparison remain integrated with the existing capture components.
- Overview uses actual created/product/provider/team metadata; exhaustive states
  and derived expiry remain distinct from pricing readiness and proof satisfaction.
  Failed or rejected history never presents a zero premium as an offer.
- Required scoped job/refresh metadata and GET quotes/{quoteId}/ratings were added
  to support the UI. History has bounded protected user/quote/version cursors and
  current authorization. No migration was required. Only the verified history
  route advances to phase-6-04-implemented; future commands remain pending.
- RequestedCover and its pure buffer helpers extend quote-cover rather than
  placing the entire form in quote-wizard. Action dialog and pure API helpers keep
  recovery separate from rendering. TypeScript permits explicit .ts imports with
  noEmit so the same pure functions run in Node semantic tests.

## Verification

- Final backend **727 passed:601 unit/126 integration,99 real SQL,zero skips**.
  Fresh `.local/phase6-04-backend-20260916-metadata`; log
  `.local/phase6-04-backend-metadata.log`; integration duration13m6s.
  assert-test-results passed exact727/99 minima. This run includes all final
  production backend metadata changes, superseding the earlier727-case run.
- **86 frontend tests**, lint, TypeScript and production build passed:
  `.local/phase6-04-metadata-{web-tests,lint,typecheck,build}.log`.
  **316 contract/source tests**, zero skips:
  `.local/phase6-04-contracts-status.log`; final OpenAPI lint valid with10 retained
  unused-component warnings in `.local/phase6-04-openapi-status.log`.
- Actual Chrome/API/SQL journeys for both products passed:
  `.local/phase6-04-browser-metadata.log` and
  `.local/browser-evidence/underwriting-rating/report.json`. Covers stored cover,
  independently approved agency terms, explicit refresh/unchanged old proposal,
  fresh vehicle provenance, durable fictional rating, committed-response loss and
  exact retry, re-rate, paginated retained results, submission, concurrent412 with
  retained reason, return/history, keyboard wrapping and314px/390px containment.
- Pending/failed/expired display fixtures intercept reads only and are explicitly
  labelled in the report. They do not claim persisted business outcomes. Real
  provider failure/recovery remains covered by the backend suite.
- Final Road Risks desktop, Combined mobile, failed desktop and expired mobile
  screenshots inspected; source hierarchy, rail and mobile containment confirmed.
  Browser fixture leaves current priced examples for Combined
  ed960316-c337-4caa-a083-e9b856f5d2fb and Road Risks
  068a3ce8-12b9-469e-899c-9cc848bb556a. Report records exact current rating IDs.
- Earlier browser runs caught missing accessible select names and dialog Tab
  escape; both fixed and verified. Refresh correctly invalidated old vehicle
  provenance, so the journey now makes a fresh explicit manual decision.
- git diff --check passed; frontend-code unchanged. Verified owned API65288 and
  Next1732 previews stopped after browser checks. No reset or real transmission.

## Source coverage and remaining owners

All12 control placements and24 display occurrences assigned06-04 were reconciled
in06-SOURCE-AUDIT. Existing clone/withdraw/revision comparisons retain earlier
coverage; the new browser does not claim to exercise every retained action.
Driver proof currently shows actual independent requirements; review is06-05/06.
Applied endorsement and licence-review values explicitly await their decision and
terms owners06-05/06/09. Later quotation, capacity and issue actions remain closed.
Compound requirements and full cross-surface acceptance stay pending06-14.
Human business/assistive-technology UAT, hostedCI and Docker remain unperformed.

Next:06-05 active-cycle evidence, independent reviews, multidimensional referral
decisions and typed conditions, including UW-22/UW-09/W-07 and the explicit
any-driver-minimum-licence warranty. Preserve current demo data and credentials.
