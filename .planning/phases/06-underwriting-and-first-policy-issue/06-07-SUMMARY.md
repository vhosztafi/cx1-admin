---
phase: 06-underwriting-and-first-policy-issue
plan: '07'
status: complete
completed: 2026-09-17
implementation_commit: aea6645
requirements_completed: []
---

# 06-07 — Capacity escalation and correspondence

Implemented in `aea6645`. Current quote referrals can create an
owned carrier escalation, submit immutable correspondence, inspect deterministic
demo outcomes and record actual supplied response evidence. Both Motor Trade
products have working escalation/authority screens with the prototype layout.
Carrier extensions remain limited to the exact submitted exposure and validity;
no binder/grant rewrite or unrelated referral approval occurs.

## Storage and boundaries

- Migration `20260916231026_CapacityEscalationStorage` adds escalation, submission,
  selected-evidence and message aggregates plus nullable exact submission proof.
  Composite owner keys, current-pointer fences, provider/retained-binder checks,
  immutable provenance and append-only SQL triggers protect history.
- Generated forward SQL was inspected in `.local/phase6-07-migration-forward.sql`.
  Fresh and retained-upgrade SQL tests passed. The documented initializer applied
  it to preserved CoverMGA_Demo before browser verification. Counts and SHA256
  hashes for all18retained data sets matched before/after/repeated seed, including
  credentials, historical quotes/rating, decisions, conditions and evidence.
  Evidence: `.local/phase6-07-preservation-{before,after,repeat}.txt` and
  `.local/phase6-07-demo-{migration,repeat}.log`.
- Six versioned fictional provider scenarios use one immutable request/outbox
  operation per submission, durable execution/application, bounded retries and
  duplicate/conflict quarantine. Source deadline is two working days using a
  Monday–Friday demo calendar, preserving London time across DST. The original
  elapsed-hour seed receives an appended setting version; operator overrides and
  existing submitted deadlines remain unchanged. Before/after/repeated-seed
  hashes for23data sets match in `.local/phase6-07-deadline-{before,after}.txt`.
  Pure weekend/DST tests and the realSQLseed preservation test pass.
- Supplied responses require exact submission ID/hash, actual file association
  and accepted current review, provider identity/reference/body/received time.
  Conditional responses create carrier-attributed conditions independent of later
  staff decisions. Withdrawal invalidates authority/resolution without rewriting
  correspondence or pricing. The shared100activecondition bound is enforced.
- Safe read/job endpoints expose scoped attempts and current recovery eligibility.
  Recovery requires integration-retry plus underwriting-escalate; replay rechecks
  current identity and grants. Generic admin job discovery remains diagnostic.

## UI and source coverage

Twelve capacity controls (including the parent quote referral action) and14source
display fields now map to actual persistence and browser evidence. Authority
context shows retained requested/binder limits and each current actor grant.
Correspondence distinguishes staff submission, demo-provider and supplied response,
with exact limits, timestamps, conditions and historical applicability. The314px
rail shows request routing, response deadline, days open, quote blockers and jobs.
MTA Reduce/Remove/Back adapts to explicit return-to-draft and actual parent quote;
literal MTA remains Phase7. Terms/acceptance/issue remain08–12owners.

The final source reread also identified conditional branches/table rows missed by
the original default-state inventory. `conditionalEscalationSupplement` explicitly
assigns final06-14coverage for follow-up replies, further responses, reopen/chase/
internal routing equivalents, condition-to-issue flow and scoped past-referral
comparison. These are not claimed as fully covered by the twelve default controls.
The final phase source gate must close or reconcile each entry before signoff.

The response dialog retains exact pending command identity after lost replies and
retains form data on stale412. Account switches prevent dispatch. Explicit Refresh
reloads all dependent evidence even when parent ETags have not changed; it does
not silently resubmit a mutation or poll away an unfinished form.

## Verification

- Fresh full backend: 767 tests (614 unit, 153 integration), including 126 real SQL scenarios; zero skips. Results: `.local/phase6-07-backend-deadline`, log `.local/phase6-07-backend-deadline.log`; integration duration 16m14s. The result gate passed MinimumTests767/MinimumSqlTests126.
- Targeted final17realSQLcapacitycases passed:
  `.local/phase6-07-capacity-final2`,2m28s. Scenarios include approve/query/decline/
  conditions, crash/lost lease, duplicate/conflict, superseded cycle, proof
  withdrawal, actual grant revocation, exact replay, supplied extent, ownership,
  scoped job recovery and competing read/update locks.
  Additional seed-upgrade/operator-preservation case passed in
  `.local/phase6-07-deadline-seed`; working-day behavior is also browser verified.
- Frontend94tests, lint, typecheck and productionbuild passed. Logs:
  `.local/phase6-07-web-{deadline,lint-deadline,typecheck-deadline,build-deadline}.log`.
  Contract/source317cases pass `.local/phase6-07-contracts-source-final.log`;
  OpenAPI valid with10existingwarnings `.local/phase6-07-openapi-deadline.log`.
- Actual Chrome both-product journey passed
  `.local/phase6-07-browser-deadline2.log`; persisted readback and deterministic409
  evidence-page recovery passed `.local/phase6-07-browser-deadline-readback.log`.
  Script `scripts/verify-underwriting-capacity-browser.mjs`, report/screenshots
  `.local/browser-evidence/underwriting-capacity`. Verified actual supplied
  reviewed letter/W07, exact lost-response retry, uncertain Escape/focus containment,
  stale412retained draft/readback, servicing deniedwrites, account-switch denial,
  explicitrevision/history/parentnavigation,314rail and390pxcontainment.
  Desktop/mobile screenshots inspected; no horizontal overflow.
- Browser exposed a SQL deadlock from upgrading read-scope locks. Corrected
  capability projections to stay read-only and added a real competing-lock test.
  Browser also exposed dependent-read Refresh recovery; fixed and verified with
  explicit409injection/readback. Midnight/timezone failures were harness inputs,
  corrected to normalized datetime-local values and explicitUTC test context.
- Local examples: CombinedQT-MT-0000000284, quote07d4354e-7c87-475f-aabf-7f7d5f2ce30e,
  escalation6953b12c-89f7-418e-983d-846cc73c7f4c with supplied W07condition.
  RRQT-MT-0000000285, quote6c935c88-2759-4947-99fb-6fc029efd2a5,
  escalationf2a3a414-2b03-472f-96e9-12efdd5065df retained afterreturntodraft.
- Owned preview processes stopped after verification. No sales-funnel changes,
  reset, deployment, real message/provider/payment or new portal. No human UAT,
  hostedCI or Docker acceptance claim. Whole UWRrequirements remain incomplete
  until the final06-14acceptance gate.

## Next slice

Execute06-08: immutable prepared terms/document payload, actual exact signed proof,
scoped delivery and acceptance. Replace the prepared-terms storage fence with
same-owner terms keys. Keep pricing, contractual terms and assurance hashes
separate; a reviewed signature must not recursively regenerate its terms version.

