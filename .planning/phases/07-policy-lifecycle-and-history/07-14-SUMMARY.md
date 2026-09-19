---
phase: 07-policy-lifecycle-and-history
plan: '14'
status: complete
completed: 2026-09-19
requirements: [POL-09, POL-05, POL-06]
requirements_completed: []
---

# 07-14 — Atomic cancellation and durable consequences

Both Motor Trade products now issue an exactly approved cancellation, retain historical risk, post the independently calculated signed debtor movement and expose durable follow-up status. Future cancellation remains scheduled until effective. Cash paid is explicitly zero.

## Implementation

Current actor/scope/issuing grant is checked before receipt replay. Fresh issue rechecks the current revision, base, exact preview and approval under shared locks and the editing lease. One transaction commits immutable decision, cancellation transaction/version, original-party obligation, component returns, sealed balanced journal, four outbox intents, audit and conflicting-draft/lease closure. Single cancellation per term and single return per original component are enforced in SQL.

SQL independently calculates London calendar-day returns using integer-penny half-away-from-zero rounding, retaining fees. Exact original component, interval, amount, parties and complete journal are checked. The cancellation snapshot has its own closed format and preview/approval provenance, retaining insured, risk, cover, term and historical premium bytes. Policy reads/UI do not invent rating or customer-acceptance IDs for cancellation.

The registered deterministic notice worker persists a delivery receipt separately from lease-owned application. Expired workers cannot apply; restart reuses the same receipt. Delayed rating, capacity and terms results remain historical/superseded and cannot restore current draft pointers. Certificate withdrawal, MID removal and task-close intents remain pending for Phase9 processors; no external notice is sent and no Finance payment occurs.

GET cancellation-issue reconstructs saved issue status from durable domain records rather than expiring command receipts. The actual UI confirms effective time/credit, freezes uncertain retries, reloads saved status and opens the cancellation transaction. Two-column consequence rows remain readable at390px.

Paths refine the planned large Servicing files into partial CancellationReviewService, CancellationIssueWriter, CancellationPostingService, CancellationIssueRead, CancellationNoticeWorker and CancellationReviewEndpoints. Existing cancellationreview UI plus cancellationissued retain the prototype visual language. Focused browser command: scripts/verify-cancellationissue-browser.mjs.

## Verification

Clean gate .local/phase7-14-clean-gate:18 passing cases,4 unit and14 real SQL, no skips, checked with assert-test-results.ps1. Reports are unique, timestamp-checked copies of actual runs:

- guards-sql:6 cases, both-product issue/storage/HTTP plus exact SQL arithmetic. Wrong amount, interval, party, missing/duplicate original component writes are injected after application calculation and rejected by SQL with complete rollback. Also exact concurrent replay versus draft save, grant revocation, late rating, notice restart and scheduled/effective reads.
- adjusted-sql:2 actual adjustment-then-cancellation journeys, including positive return of a negative Combined premium movement, original interval/identity preservation and unchanged historical component bytes.
- workers-sql:2 actual cancellation-before-apply journeys for capacity and terms delivery; retained responses stay superseded.
- races-sql:2 accepted adjustment versus approved cancellation races; exactly one succeeds and only one additional journal posts.
- browser-sql:2 actual Chrome/Next/Kestrel/SQL journeys, separate requester/senior approval, lost issue response/exact retry, registered notice worker, reload, stable transaction navigation and mobile containment.
- unit:4 cancellation snapshot cases. Earlier foundation snapshot regression evidence remains in07-14-PROGRESS; do not count overlapping historical runs again.

Twelve fresh captured responses validate against closed API schemas and reject extra fields.17 servicing source/contract tests pass. Typecheck, lint, Next production build and API Release build pass. OpenAPI415 operations validates with43 warnings (existing unused components and nested oneOf composition analysis; strict captured-response validation passes). Final browser evidence directories: CoverMGA_Test_9713287ffa7647289d2baf20c8dc770c and CoverMGA_Test_292e26e0fba3460e9cc5b8ecd92bd8b7 under .local/browser-evidence/cancellation-issue. Mobile screenshot inspected.

Earlier failures remain recorded: notice kind missing from lease allowlist; internal/public consequence kind mismatch; browser test top-level return; test incorrectly treating historical superseded-rating retention as false; legacy capacity fixture assuming one policy version. All corrected and covered by passing runs. Failed aggregate .local/phase7-14-final/sql.trx is not part of the clean gate.

## Demo and remaining phase work

Three additive migrations through20260919061958 applied without reset/reseed. All127 pre-existing table/setting count/SHA256 records match (new nullable transaction provenance excluded from old-column comparison). Existing07-13 migration was never rewritten. Hidden demo API19460 and web46256 serve5087/3100; .local/phase7-14-preview-pids.json. The notice worker is enabled. Sales funnel untouched.

The cancellation portion of07-09 is now verified. Continue07-15 history/reconstruction/cloning and07-16 acceptance. Requirement-wide completion remains subject to those phase gates. Real document/MID/task processing and cash settlement remain with their named later phases.
