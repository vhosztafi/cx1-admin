---
phase: 07-policy-lifecycle-and-history
plan: '10'
status: complete
completed: 2026-09-18
requirements: [POL-04, POL-05, POL-06]
requirements_completed: []
production_commit: d02a2b7
---

# 07-10 — Atomic accepted adjustment issue

Both Motor Trade products issue accepted adjustments through the actual back-office
confirmation and strict API. One SQL transaction records the immutable issuing
decision, ordered cumulative policy versions and registrations, signed obligation,
sealed journal, per-version document requests and MID intents, audit/activity,
closed draft/lease and durable command receipt. No cash collection or real delivery
is inferred from issuing.

## Implementation and review

- `20260918123403_AtomicServicingIssue` adds exact compound decision ownership,
  append-only decision/MID records, terminal draft guards and complete graph checks.
  Adjustment transactions require their actual servicing decision. Existing first
  issue remains valid and its bound-quote guard checks the original first graph,
  independently of the term's latest version pointer.
- `issued-servicing-1` retains full declarations and exact base/revision/decision,
  transaction, one-based slice ordinal, effective/processed instants and input hash.
  Original `issued-quote-1` validation is unchanged. Signed servicing policy views
  have a separate schema; positive first-issue schemas were not weakened.
- Current identity, policy scope and actual authority are held before receipt
  replay. Fresh issue reuses the existing current-base/rating/lease decision context
  and rechecks assessed proposal, reviewed proof, conditions, capacity, exact
  delivered terms and acceptance. New keys cannot issue a closed draft again;
  expired receipts remain durable, and revoked issuing authority blocks replay.
- UI retains exact request bytes, key, lease and ETag through uncertain responses
  and readback. It shows actual due/credit, adjustment and revised term premiums,
  effective dates, version count and queued side effects. Links open the current
  policy, exact issued slices and the actual adjustment transaction. Source labels
  that suggested cash collected or documents already sent now describe the real
  posted/queued state instead. Existing saved risk review supplies the actual changes.
- Future issue updates the latest stored base without moving today's temporal
  policy/discovery selection. SQL verifies future registration visibility separately.

## Measured verification

- Final gate: **9 unit + 2 real-SQL scenarios = 11**, no skips, `.local/phase7-10-final`;
  `assert-test-results.ps1` cutoff `2026-09-18T14:00:00Z` passes. SQL scenarios contain
  all 14 injected write boundaries and compare all graph-table counts after each
  rollback; different-key simultaneous issue, concurrent exact replays, wrong role,
  stale ETag/lease/assurance, expiry and revocation; exact two-slice chronology,
  document/MID graph, original bytes, closed-draft and immutable-decision protection.
  Road Risks changes registration; Combined temporarily adds then removes cover.
- Full unit run: **799 passed**, `.local/phase7-10-regression/unit`.
  Affected shared SQL run: **27 passed, 1 fixture failure** in
  `.local/phase7-10-regression/sql`. The failing Road Risks fixture changed its
  registration after rating; moving that setup before save/rating produced both
  passing cases in `.local/phase7-10-registration-green/sql`, then the stronger final
  run above. There are 28 distinct affected SQL cases across these runs, not 29.
  First issue, storage, posting, chronology and discovery checks remain green.
- **83 contract/frontend/source checks passed**, `.local/phase7-10-contracts-green.log`.
  OpenAPI lint, TypeScript, ESLint and API/Next release builds pass. External issued
  servicing schemas are now registered by contract validation. The source test uses
  explicit remaining operation identities instead of an obsolete pending-route count.
- Both actual browser journeys pass in
  `.local/browser-evidence/servicing-issue/report.json`, completed `2026-09-18T14:11:00.615Z`.
  They prepare/sign/send/accept/issue, deliberately lose the issue response, retry
  identical bytes/headers, confirm one saved result, verify future current-snapshot
  stability, 390px containment and reload. Repeat runs use the latest owned issued
  base and a genuinely changed fictional trading name; no-op rating remains rejected.
- Final API restart readback passes in the same directory's `restart-readback.json`:
  exact snapshot hash/provenance, journal/amounts, issued state, version and transaction
  UI links, current-policy navigation, and strict schemas for both browser policy
  views/receipts plus both actual SQL-hosted HTTP receipts in `.local/phase7-10-final-http`.
  Desktop/mobile screenshots were inspected.
- Additive demo migration preserves **113 existing table counts/content hashes**,
  `.local/phase7-10-{before,after}.txt` and preservation SQL. Only new nullable columns,
  new tables and EF history are excluded. No reset/reseed; original sales funnel untouched.
  Owned previews: `.local/phase7-10-preview-pids.json`.

## Boundaries and continuation

Implementation uses feature-local service/writer/snapshot/endpoint/model/guard files
instead of concentrating the whole graph in the suggested single filenames. Shared
integration fixtures expose scenarios through `DisplayName~ServicingIssueTests`;
the hypothetical integration class filter would execute zero cases.

Document rendering/delivery, cash settlement and broader operational history remain
their approved later owners. Stored document requests and MID work are genuine pending
intents. No real provider, payment, hosted CI, Docker or human business UAT is claimed.
The source issued-screen navigation actions are verified; field evidence distinguishes
actual issue facts from those downstream document/payment states. Compound requirements
remain open. 07-09 remains partial for cancellation lineage with 07-13/14. Continue
07-11 renewal preparation inline; 9/16 Phase7 plans and 58/65 implementation plans complete.
