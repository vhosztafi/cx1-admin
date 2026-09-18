---
phase: 07-policy-lifecycle-and-history
plan: '06'
status: complete
completed: 2026-09-18
requirements: [POL-04]
requirements_completed: []
production_commit: f488cad
---

# 07-06 — Servicing evidence and referral decisions

Saved Motor Trade adjustments now support scoped immutable files, purpose/item
associations, review and withdrawal, current-authority referral decisions,
documentary conditions and resolution, and persistent underwriting submissions.
Missing proof remains independently blocking. Issued policy snapshots stay unchanged.

## Implementation

- Compound draft/revision/cycle ownership, additive SQL migrations, immutable
  decisions/history and atomic selected decisions preserve provenance. Current
  scope, role grants, authority dimensions, lease and rating applicability are
  checked before mutation/replay. Withdrawn or wrong-purpose proof cannot qualify.
- Strict API reads/commands provide no-store responses, CSRF protection, exact
  retry receipts and bounded signed history cursors. Upload screening is distinct
  from underwriting review. Live authority reads reflect revoked grants without
  relying on a changed draft ETag.
- The UI supports real upload/attach/review/withdrawal, decision history, conditions,
  current authority, specific referral work links and keyboard focus. Forms survive
  lease renewal; pending uncertain commands retain their exact request for retry.
- Immutable underwriting submission records preserve each rated cycle's handoff.
  Lost responses can be retried or recovered from saved history; rerating and
  abandonment retain previous submissions. Submission is not approval or issue.
- Principal final commits: f488cad (scoped referral work), 5be1f37 (submission UI),
  5301304 (submission HTTP), 77016f9 (submission service), f03514e (storage).
  Earlier implementation and failing-first evidence are in 07-06-PROGRESS.md.

## Measured verification

- Final referral work/shared authority regression: 12 unit and 6 real SQL tests,
  zero skips, in .local/phase7-06-referral-work-final. Result gate passed with
  minimum18/minimumSQL6 and cutoff2026-09-18T04:23:00Z. Two actual scoped HTTP
  response bodies passed runtime contract validation.
- 132 frontend tests passed (.local/phase7-06-referral-work-ui.log); 40 API
  contract tests passed (.local/phase7-06-referral-work-contracts.log); 17 source
  contract tests passed (.local/phase7-06-final-source-contracts.log).
- OpenAPI validates 391 operations with29 unused-component warnings. Typecheck,
  changed-file lint, API Release build and Next production build passed. A failed
  webpack cache build recovered after retaining/renaming the cache and rebuilding;
  no runtime configuration change was needed. Logs retain both attempts.
- Earlier proof HTTP run: 30 domain +2 paging unit +2 SQL tests in
  .local/phase7-06-proof-commands-api-reviewed;12 read and14 command bodies validated.
  Earlier submission HTTP run:12 unit +2 SQL in
  .local/phase7-06-submission-http-reviewed;12 actual bodies validated.
  These overlapping runs are not summed into an invented unique test total.
- Both product full evidence browser journeys passed on the final code:
  .local/phase7-06-final-evidence-browser.log and
  .local/browser-evidence/servicing-evidence/report.json. Coverage includes exact
  lost-upload retry, lease renewal, reviewed driver/trading/premises proof,
  withdrawal/replacement readiness, condition resolution, historical cycles,
  downloads, reload,390px containment and unchanged issued snapshots.
- Both focused referral-work journeys passed in
  .local/phase7-06-referral-work-browser.log and referral-work-report.json in the
  same evidence directory: exact scoped work, keyboard focus, reload, persisted
  decisions, missing-proof blocking and stale-cycle rejection.
- Both submission journeys passed in .local/phase7-06-submission-browser-reviewed.log
  and submission-report.json: missing proof can still be handed off, exact retry,
  saved-record recovery, one row per cycle, rerating and abandonment history.
  Browser-owned drafts were abandoned with audit retained. Injected transport
  failures are deliberate tests. Human/assistive-technology UAT is not claimed.

## Source coverage and boundaries

CTL-721a19d5dbfa, CTL-909dc8df1733 and CTL-d2bf3015d449 are verified. The prototype's
specific referral task link opens the real persisted referral's work and decisions;
generic task assignment, due dates, comments and inbox remain Phase9 under D-10.
BR07-05 supporting evidence is verified with actual purpose/item associations.
The source API inventory retains its original scoped29 entries; it is not the
global391-operation API count. Full393-field/137-control review remains07-16.

The demo database was upgraded additively without reset. frontend-code is unchanged.
Capacity, terms/acceptance and atomic issue remain07-07/08/10, so POL-04 remains
open. Hosted CI and Docker runtime remain unperformed. Next:07-07 inline.

## Self-Check: PASSED

Production commits, final real SQL and browser outputs, source mapping and runtime
contract evidence were inspected before close-out; no plan-owned gap remains.
