---
status: partial
phase: 13-complete-demo-and-acceptance
source: [13-01-SUMMARY.md, 13-02-SUMMARY.md, 13-03-SUMMARY.md]
started: "2026-09-26T11:51:51.462Z"
updated: "2026-09-26T11:51:51.462Z"
mode: auto-evidence
---

## Current Test

number: 9
name: Business walkthrough
expected: |
  A business reviewer follows the short demo through the insurance and servicing journeys.
awaiting: human confirmation when available; automatic portion finished

## Scope

Requested with --auto. Passes below identify fresh automated checks or explicitly reused evidence; none is recorded as a human response. No broad SQL suite, shared service restart or retained identity provisioning was repeated.

## Tests

### 1. Control traceability
expected: A reviewer can locate an original control, its current operation and linked phase evidence.
result: pass
evidence: Fresh read-only check: .local/phase13-uat/doc-trace.json; 949 controls, 537 operations. Mapping is not a new live assertion for every control.

### 2. Demo and developer handover
expected: Demo, setup, recovery and module guidance open through working local document links.
result: pass
evidence: Fresh check: 17 local document links resolve. Content reviewed against the Phase 13 summaries and explicit environment boundaries.

### 3. Saved reporting on a narrow screen
expected: Opening the saved report restores its filters and running it shows source records without page overflow.
result: pass
evidence: Fresh browser run: .local/phase13-uat/loaded-visual.json; report passed both attempts.

### 4. Search on a narrow screen
expected: Search displays saved records and keeps the controls/results inside the narrow page.
result: pass
evidence: Fresh repeat passed with 45 results; direct diagnostic also returned both search requests successfully. First sequence timed out; see observations below.

### 5. Task queue on a narrow screen
expected: The saved task queue loads within the narrow page bounds.
result: pass
evidence: Fresh repeated browser journey passed.

### 6. Desktop dashboard
expected: Dashboard displays the saved open task and three quotes, and labels unavailable role-restricted queues.
result: pass
evidence: Fresh repeated browser journey passed; dashboard and search screenshots visually inspected.

### 7. Saved work after application restart
expected: The same session, favourite/filter identity and saved quote IDs are recovered after an owned application restart.
result: pass
evidence: Reused same-day evidence: .local/phase13-tests/restart-browser.json and preservation.json. No new restart in this UAT run; does not cover SQL engine restart.

### 8. Keyboard navigation and role denial
expected: Skip navigation reaches main content, the narrow drawer restores focus, and unauthorised finance export is rejected.
result: pass
evidence: Reused same-day eight-check restart/browser evidence. Not human assistive-technology sign-off.

### 9. Business walkthrough
expected: A business reviewer follows the short demo through issue, referral/blocked adjustment, renewal, cancellation/refund and Commercial Combined servicing.
result: [pending]
reason: Human confirmation has not been supplied. Existing engineering journey evidence remains available; no user response is inferred from --auto.

### 10. Human usability and assistive review
expected: A reviewer confirms the key desktop/narrow screens and assistive interactions are suitable for their work.
result: [pending]
reason: Automated screenshots and keyboard assertions do not establish this human judgement.

### 11. Database engine recovery
expected: After a dedicated SQL Server instance restart, saved work, adapter outcomes, files, history and balances are recovered.
result: blocked
blocked_by: other
reason: The active SQL instance is shared with retained data. The Phase 13 context excludes stopping it; a dedicated-instance recovery environment is needed. ACC-02 remains partial.

## Summary

total: 11
passed: 8
issues: 0
pending: 2
skipped: 0
blocked: 1

## Observations

The first automated journey passed reporting, then timed out after 60 seconds waiting for the Search results table. Its report is preserved in `.local/phase13-uat/loaded-visual-first-attempt.json`. The direct diagnostic loaded all 45 results with successful responses. The identical journey with response logging subsequently passed all four screens and reported no page errors. No runtime code changed; the intermittent cause is unconfirmed, so this is not claimed as a fixed defect. Capture network/compiler timing if it recurs; avoid another broad regression run solely for this observation.

Fresh captures: `output/playwright/phase13-uat/`. Browser cookies and fixture credentials remain ignored. The artifact scan found only a historical Phase 10 initial verification gap file before this partial UAT record; current Phase 10 verification already supersedes it.

## Gaps

None diagnosed. Human and dedicated-environment prerequisites remain open above; they are not invented code defects or new implementation plans. Milestone acceptance is not marked complete.
