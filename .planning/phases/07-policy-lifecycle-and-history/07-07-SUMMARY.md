---
phase: 07-policy-lifecycle-and-history
plan: '07'
status: complete
completed: 2026-09-18
requirements: [POL-04]
requirements_completed: []
production_commit: 91ed93e
---

# 07-07 — Servicing capacity referral lifecycle

Motor Trade servicing referrals now have persistent carrier requests, immutable
submissions/correspondence/responses, dated conditions and explicit proof
resolutions. Current carrier extent participates in referral authority; previous
quote approvals and historical carrier responses confer no servicing authority.

## Implementation

- Additive SQL migrations enforce exact draft/revision/cycle/rating/referral/
  provider/submission ownership, immutable history and monotonic pointers.
  Selected evidence retains exact reviewed associations. Supplied responses need
  reviewed proof of the exact submission; demo responses retain durable operation
  and inbox provenance. Dated conditions and resolutions have compound ownership.
- Current identity, grants, provider, lease and proof are checked before replay.
  Extent covers the exact target and all relevant exposure intervals. Conditions
  require explicit resolution and current proof. Withdrawal/reopen preserve history
  while removing applicability; a new submission invalidates the earlier response.
  Revocation/withdrawn proof invalidates readiness and subsequent authority.
- A registered servicing-specific demo worker uses durable operation keys,
  leased outbox work, attempts, inbox deduplication and conflict quarantine. Late
  results cannot reactivate withdrawn/superseded work. Senior assignment routes
  internal work without extending authority.
- Scoped no-store APIs enforce CSRF, closed bounded input, parent/child versions,
  exact retry and signed bounded history cursors. Exact referral filtering prevents
  historical pagination blocking discovery or creation for a current referral.
- The actual referral workspace supports create/submit/query reply/chase,
  supplied responses, dated conditions, proof resolution, senior routing,
  withdrawal/reopen, correspondence/history, extent and same-policy similar cases.
  The prototype carrier shortcut and parent draft links navigate real records.
  Existing upload/review and immutable retry machinery are reused.
- Principal commits:234d363 submission;1d33855 actions;c18d362 response-proof
  ownership;f8b1cae responses;9979aa8 conditions;2b5de00 response/resolution
  commands;b806946 worker/routing;1265735 authority;250b517 reads;fd3a23f
  HTTP/contracts;91ed93e UI/scoped discovery. Earlier red/green evidence is
  retained in07-07-PROGRESS.md, not summed as a unique test total.

## Measured verification

- Final27checks=25unit+2real SQL HTTP workflows, zero skips, in
  .local/phase7-07-capacity-filter-green, cutoff2026-09-18T09:18:00Z.
  Filter red expected200/actual400 before implementation. Both product workflows
  create, submit/retry, chase/assign, process query/reply, record supplied conditional
  permission after a second query, resolve reviewed proof, page history and reopen.
  CSRF, unknown fields, stale versions, unknown proof/owner and cursor invalidation
  are negative-tested. All38 captured responses pass closed runtime contracts.
- Shared authority regression49unit+8SQL passed in
  .local/phase7-07-capacity-authority-reviewed. Worker routing7SQL passed in
  .local/phase7-07-worker-routing-green; final validity fix25unit+1SQL passed
  .local/phase7-07-worker-final. These overlap other runs and are not added together.
-52 API/frontend tests pass in .local/phase7-07-final-contracts-web-tests.log
  (42 API+10 frontend). TypeScript, complete frontend lint, API Release build and
  Next production build pass. OpenAPI400 operations lint passed with34 unused
  component warnings.
- Both final browser journeys pass in .local/phase7-07-capacity-browser-final.log
  and .local/browser-evidence/servicing-capacity/report.json, completed09:25:52UTC.
  Actual UI commands create/rate an adjustment, query/reply, upload/attach/review
  response proof, record dated conditional permission, review trading proof and
  explicitly resolve. Review alone is insufficient; carrier readiness alone does
  not approve the referral. Reopening removes applicability and retains history.
  Issued snapshots remain identical; only harness-owned drafts are abandoned.
- Desktop/390px screenshots inspected and viewport containment passes. Initial
  element screenshots intersected the fixed header; final viewport capture fixes
  the artifact. Earlier runs exposed wrong build-time API port and a correctly
  rejected stale version after background processing. Final harness explicitly
  waits for the page's saved version. No human UAT is claimed.

## Source coverage and boundaries

CTL-4c6212277cb7 and CTL-e39aeff1b11d map to the real carrier workspace. BR07-08
has implemented state rules plus storage/command/browser evidence. Terms,
delivery/acceptance, exact financial components and atomic issue remain07-08/09/10;
POL-04 stays open. No servicing issue endpoint is claimed here;07-10 must compose
the current authority and proof predicates already verified by referral readiness.

Demo migrations applied through20260918073955 without reset/reseed. frontend-code
is unchanged; no real providers/messages/payments. Hosted CI, Docker and
human/assistive-technology UAT remain unperformed.

## Self-Check: PASSED

Commits, SQL/runtime-contract/browser outputs, source controls, current authority
fences and screenshots reviewed. Continue07-08 inline; this checkpoint does not
end the active execute-phase7 --auto task.
