---
phase: 07-policy-lifecycle-and-history
plan: '12'
status: complete
completed: 2026-09-18
requirements: [POL-07, POL-08]
requirements_completed: []
---

# 07-12 — Renewal invitation, new-term issue and lapse

Both Motor Trade products now complete pinned invitation preparation/delivery,
separate evidenced acceptance and atomic new-term issue. Manual and date-driven
lapse retain the original expiry and produce one durable fictional notification.
Current cover and old policy bytes are preserved. Closed drafts retain invitation
documents and exact delivered recipients independently of current rating eligibility.

## Implementation and review

- Invitation templates and terms bind the exact preparation, rating, experience,
  reviewed proof and recipients. Applied delivery establishes invited state;
  acceptance and issue are separate commands. Unsupported late issue is rejected.
- Issue creates a linked nonoverlapping term, sequence-one renewal transaction,
  full-term posting, three document requests and one MID intent. Reads select
  old/new cover by inception; the original term is not overwritten. Current
  authority is checked before replay. See the earlier invitation/issue evidence
  in07-12-PROGRESS.md and commits9d48c8d /4641980.
- Immutable RenewalLapseEvent has unique term/work identity, configured London
  deadline, reason, actor/system mode and recipient snapshot. Manual/automatic
  paths share policy/term locks with acceptance/issue. Accepted/issued/cancelled
  outcomes cannot lapse; existing events deduplicate beyond command-receipt life.
- Lapse releases editing leases, changes draft ETags and projects closed lapsed
  drafts. New renewal drafts, preparation and writes cannot reopen a lapsed term.
  No cancellation transaction or new cover is invented by lapse.
- A Development opt-in dispatcher scans bounded pages of expired terms and
  processes the durable notification queue. Separate provider receipt/application
  survives lost responses and expired worker leases. Missing recipients create a
  retained failed job/exception, not successful delivery. Up to18 attempts remain
  visible, matching administrator retry budgets. No real email is sent.
- Strict no-store APIs, current scope, CSRF, term ETag and exact command retry
  protect the lifecycle. SQL guards reject mutable history, malformed/missing
  notification payload fields, foreign provenance and later acceptance/issue.
  Outbox EF writes account for the new input-immutability trigger.
- UI shows configured due/expiry/inception/lapse dates, real lifecycle state,
  reason and notification attempts. Four source progress styles navigate the
  actual stages. Unchanged renewals expose their prepared slice for referral
  condition targeting. Historical renewal prices retain renewal labels.
- Scoped, signed-cursor document history shows version, preparation, actual
  delivery and recipients after issue/lapse, with retained contract viewing.
  The mobile table scrolls horizontally without crushing text. The obsolete
  separate invitation endpoint was replaced by the implemented shared
  prepare/send operations in contracts and source mappings (410 operations).
- New feature-local records/model, endpoints, worker and history files refine
  the plan's suggested consolidated filenames. Only new additive migrations
  were written; previous migration history was not edited.

## Measured verification

Runs overlap and must not be summed as distinct coverage.

- Final lifecycle gate: **21 unit +21 native SQL =42 passed, no skips**,
  `.local/phase7-12-lapse-complete`, validated by assert-test-results.ps1 with
  cutoff2026-09-18T19:00:00Z. Covers both-product invitation/issue/lapse,
  acceptance/lapse and issue/auto-lapse races, exact replay/current authority,
  unchanged cover, malformed JSON, immutable event/work, rollback after outbox
  creation, missing-recipient failure, expired worker fencing and receipt recovery.
- Broader shared-storage/job run:52 passed and5 failed of57 in
  `.local/phase7-12-lapse-reviewed`. The failures were historical fixtures:
  accounting seed on pre-accounting schemas and rollback of immutable servicing
  templates into an older enum. Corrected seed opt-outs and latest-upgrade
  preservation were rerun in the21-SQL final gate; all five now pass. No failed
  report is represented as a wholly passing run or silently discarded.
- Document-history extension:6 SQL passed, no skips, `.local/phase7-12-documents`;
  both-product issued history, recipients, exact terms and invalid cursor/page
  checks. Existing invitation/acceptance/race scenarios remain green.
- Four actual Chrome/Kestrel/Next/isolated-SQL lapse journeys passed in
  `.local/phase7-12-lifecycle-browser/sql.trx`, completed18:55:45UTC. Manual cases
  traverse draft/lease/lapse, lose a response after commit, retry exact bytes and
  reload closed controls. Automatic cases advance the actual hosted worker's
  clock from one minute before to the configured deadline. Each retains one
  notification and original policy version. Desktop/mobile screenshots inspected.
- Browser report directories under `.local/browser-evidence/renewal-lifecycle/`:
  Combined manual `CoverMGA_Test_b4c84985292a4e01a4d570f3dcce7a6a`, automatic
  `CoverMGA_Test_de04022ef13e40d191d29280c6457413`; Road Risks manual
  `CoverMGA_Test_6e5110cf571a4b42bf2e6d9d0571a9ff`, automatic
  `CoverMGA_Test_3b9a576a78f74fcc8041ee18f50bec0a`.
- Eight actual browser responses and a separate eight SQL/API responses validate
  against closed schemas and reject unknown properties:
  `.local/phase7-12-lifecycle-browser-responses` and
  `.local/phase7-12-lapse-complete-responses`.
- Both saved renewal issue graphs survived restart, with unchanged hashes,
  issued timeline state, immutable documents/recipients, retained contract open,
  no-store/anonymous/query rejection and390px containment. Final report:
  `.local/browser-evidence/renewal-issue/restart-readback.json`,
  completed2026-09-18T19:23:48.787Z. Final mobile document screenshot inspected.
- 61 API/source checks and5 relevant frontend helper tests passed. TypeScript,
  ESLint, production Next build and Release API build pass; Release has zero
  warnings/errors. OpenAPI lint is valid with41 existing warnings. Earlier issue
  gate23unit+8SQL and both-product issue browser evidence are retained in PROGRESS.
- Failing tests/browser observations drove fixes: empty unchanged-renewal slices,
  SQL NULL comparisons permitting missing fields, authentication session timestamps
  mixing clocks, unavailable document history after issue, and cramped mobile
  columns. SQL session timestamps now consistently use the injected clock;
  browser clock-driven tests align browser time with their server fixture.

## Demo and boundaries

The additive lapse migration20260918175723 preserves **121 existing table
count/hash rows** (`.local/phase7-12-lapse-before.txt` / `after.txt`). Earlier
invitation/issue migrations preserved115. No full reseed/reset was performed.
Fresh initialization includes invitation templates; the targeted existing-demo
seed adds missing templates only. Owned API/web previews and worker configuration
are recorded in `.local/phase7-12-preview-pids.json`; web is on3100, API on5087.

Document generation/MID dispatch and actual Finance refund/payment remain later
owners. The implemented invitation and lapse notifications are persistent demo
outcomes. Human business/assistive-technology UAT, hostedCI and Docker were not
performed. Sales funnel files are unchanged. Compound requirement completion
remains with Phase7 verification. Continue07-13 cancellation review/approval,
then07-14 atomic cancellation and finish07-09 cancellation lineage.
