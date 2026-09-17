# Phase 6 implementation, security and UI review

Inline review under the approved no-subagent workflow. This is not independent
peer review or human acceptance. Final overall signoff is recorded separately in
06-VERIFICATION after the full fresh suite and restart complete.

## Findings and resolutions

- Result-gate reports could be old or duplicated and still satisfy the minimum.
  New rejection tests first failed for both defects. The gate now verifies run
  timestamps, unique run/test IDs, counters and individual successful outcomes.
  Seven Node-driven gate scenarios pass; the retained PowerShell gate scenarios
  also pass. A caller can bind reports to an exact run start with `NotBeforeUtc`.
- Retained browser fixtures assumed every returned product version was eligible.
  They now select `captureEligible` versions, with UI selection restricted to an
  enabled radio. No old product version was changed or removed.
- Withdrawal confirmation text changed in Phase 6. The retained journey now
  checks the current closure reason and actual persisted `withdrawn` state;
  both-product exact-retry and reload checks pass independently.
- Policy discovery advertised a legacy status filter and future Commercial
  Combined product. Contract and source binding now use the implemented `state`
  filter and the two Motor Trade products. A contract test prevents recurrence.
- The retained integration browser found that quote pagination used database-wide
  `@@DBTS`; normal authentication session maintenance could invalidate the next
  page. A real SQL regression now ages the session between pages. Discovery must
  bind to relevant record versions/counts while reauthorizing every request;
  unrelated session/job writes must not break a stable list. Quote/client edits
  must still invalidate the cursor. This fix belongs to the discovery slice.

## Conditional capacity controls to close

Existing `SendAsync` is a deliberate follow-up submission: it retains prior
correspondence, creates an immutable new submission and outbox job, and invalidates
prior acceptance. Answer/query and chase controls must explain those effects.
Generic tasks and external messaging remain their approved Phase 9 responsibility.

Withdrawal/reopening require an explicit audited command scoped to the current
quote/cycle/escalation and both quote and child ETags. Preserve the monotonic submission
and response pointers, retain every submission/message, and return the request to
draft. The SQL pointer guard correctly rejected the initial clearing approach.
Draft state now fences both supplied and worker responses and carrier authority;
an old queued result becomes retained history. A new submission advances the
pointer normally. An outstanding
capacity requirement continues to block issue; withdrawal cannot grant authority.

Internal escalation should assign a current active internal senior underwriter,
without conferring authority or fabricating carrier correspondence. Persist the
reason and previous/current assignment in a subject-scoped immutable audit event.
Current command authorization must precede receipt replay. Internal action history
is displayed separately from carrier correspondence.

Similar referrals must query persisted same-agency, product, provider and rule
records, with real quote/referral/escalation links and recorded outcomes. There
must be no sample rows, cross-agency disclosure or implied grant of authority from
a prior decision. Any limit on displayed rows must be explicit.

## Final acceptance gates (results below)

- Actual follow-up and successive supplied responses, withdrawal/reopen and late
  worker result, internal assignment/current-scope denials, similar-record scope.
- Carrier conditions and reviewed evidence through a real Combined policy issue;
  retained direct-authority Road Risks issue remains separately verified.
- Rating-expiry UI rejection and authoritative SQL expiry coverage, clearly
  distinguishing a UI response fixture from actual server expiry evidence.
- Fresh complete backend/real-SQL gate, all 37 retained browser journeys, final
  frontend/contracts/build, source reconciliation and policy graph restart.
- Native Windows/SQL/Chrome evidence only; human UAT, hosted CI and Docker are
  unperformed until actually exercised.


## Implemented resolutions and evidence

- Capacity actions now enforce current role/grant, exact quote and child ETags,
  strict body/CSRF/idempotency and current active senior identity before replay.
  Assignment grants no additional authority. Two new real SQL/API scenarios pass
  in `.local/phase6-14-actions-3`; the complete targeted capacity regression passes
  six unit and twenty integration cases in `phase6-14-capacity-regression`.
- The old pointer-clearing draft failed the existing SQL monotonicity guard.
  The final code preserves those guards and fences draft state in both supplied
  responses and worker application, plus capacity authority/assurance selection.
  The late-response scenario proves no reactivation and preserved correspondence.
- Actual Chrome creates successive submissions, two reviewed supplied responses,
  active senior assignment and real similar-record navigation. It resolves the
  W-07 condition with reviewed evidence and issues an accepted Combined policy.
  PL-MT-0000000005 is the final-suite example; IDs/hashes are in the local report.
- Expiry presentation is explicitly a 409 UI fixture: reason retention, current
  state read and disabled stale confirmation. Real SQL tests independently advance
  the service clock and assert issue creates neither policy nor journal.
- Source tables retain scoped links, real outcomes and explicit latest-10/latest-50
  limits. Correspondence and internal actions are distinct. Desktop and 390px
  layouts contain forms/tables; the right rail remains 314px. Shared confirmation
  retains keyboard/focus and exact uncertain-retry behavior verified in 06-12.
- Final review rejected the old preservation files: they contained SQL error text
  because PolicyRegistration uses a composite key, not Id. Corrected SQL and a
  fail-closed script require 44 unique named count/SHA256 rows and SQL exit success.
  Six gate scenarios cover valid input, SQL failure, equal error text, duplicate,
  missing and changed results. These stubs are not real SQL preservation evidence.
  Actual stopped-worker reseeding must still pass before phase completion.
- Browser harness fixes distinguish implementation from fixtures: eligible product
  versions, current withdrawal wording, form-scoped policy/quote search buttons,
  live client policy availability, canonical datetime-local seconds, and bounded
  retry only for the documented concurrent terms-history GET conflict. No business
  command failure is turned into success or hidden by a generic retry.

No outstanding high/critical implementation finding was identified in the reviewed
capacity, issue, discovery, permission, schema and atomic-write paths. The remaining
acceptance gates above are mandatory; unperformed human UAT, hosted CI and Docker
remain explicit limitations rather than agent review claims.


Final browser suite passed in `.local/phase6-14-suite-5.log`: all four stages,
including all17 quote and all20 agency/client journeys. Three retained agency
terms stages are labelled UI fixtures; actual agency lifecycle publication has
separate SQL evidence. New carrier journey is entirely persisted except its
explicit expiry-error UI fixture. Restart capture/verify compares three complete
policy graphs and actual capacity correspondence/actions/attempts with fresh login.
Owned previews66980/70364 were stopped;53128/30204 were started with the same local
SQL/key/storage configuration. Two additive initializations preserved all44 valid
count/hash rows in `.local/phase6-14-preservation-final-1`. The source audit retains
211controls/98displays and all seven conditional controls without dropping later
phase owners. Full final backend regression passed:838cases,169realSQL,zero skips. Overall phase signoff is recorded in06-VERIFICATION.
