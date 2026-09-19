# 07-14 — Atomic cancellation, execution in progress

Updated 2026-09-19. Continue inline without another user command. Plan14 and09 remain incomplete until their remaining evidence is closed.

## Implemented and verified runtime

- Exact approved cancellation issues under existing command/policy/draft locks; current grant is rechecked before replay.
- Separate immutable cancellation decision, transaction and retained-risk snapshot; own preview/approval provenance on API and policy UI.
- Independent SQL calendar-day/penny calculation, original component lineage, single-return index, balanced sealed journal and original-party guards.
- All-or-none issue creates four durable consequences, closes competing drafts and leases, and returns signed financials with cashPaid 0.00.
- Registered deterministic notice dispatcher supports durable delivery receipt/restart and stale worker fencing. Other three intents stay pending for Phase9.
- GET cancellation-issue reads immutable decision and current jobs, independent of expiring idempotency receipts.
- Actual issue UI confirms reviewed effective time/credit, freezes uncertain command retry, reloads persisted status and links to cancellation transaction.
- Three additive migrations now applied to demo: 20260919060215, 20260919061452, 20260919061958. No reset or reseed. All127 existing table/setting SHA256/count records preserved. New nullable transaction provenance excluded from old-column comparison.
- Live hidden API19460 on5087 and web25676 on3100. .local/phase7-14-preview-pids.json. Cancellation notice worker enabled.

## Measured evidence

- .local/phase7-14-runtime-fixed/sql.trx:8 SQL cases pass (including HTTP and adjustment regression), no skips.
- .local/phase7-14-browser-v2/sql.trx:5 cases pass:3 storage/issue cases (both products, exact concurrent replay vs competing draft save, rollback, authority revocation and notice restart) and2 actual Next/browser/SQL issue journeys.
-12 captured browser HTTP responses pass strict schemas and unknown-property rejection in cancellation-issue mode.
-17 servicing contract/source checks, lint, typecheck, Next production build and API Release build pass. OpenAPI valid with43 warnings (previously41; nested oneOf composition warnings included).
- .local/phase7-14-final/unit.trx:4 CancellationIssueTests pass. Earlier foundation13 combined unit cases also included snapshot regressions; do not claim13 current CancellationIssueTests.
- Earlier failed runs retained: unsupported notice job kind; public consequence kind mismatch; browser script top-level return; incorrect expectation that retaining a superseded late rating returns false. Each diagnosed; final combined rerun pending latest test corrections.

## Remaining before completion

- Final rerun of actual late-rating fencing assertions (worker retains historical result, attempt superseded, current pointers remain null), direct SQL mutation/rollback tests, current issue/read HTTP negatives and browser screenshots focused on receipt.
- Finish09 real negative-adjustment cancellation return lineage and any uncovered cross-policy/settlement/interval cases; ensure no duplicate return can post.
- Explicit competing issue vs cancellation coverage and late capacity/terms evidence still need review; do not infer them from rating test.
- Review source coverage, clean TRX gate, restart/hash verification and final summary/commit before marking14 complete. Then15 policy history/reconstruction/cloning and16 full acceptance.

Implementation follows existing partial CancellationReviewService and CancellationReviewEndpoints, separate CancellationIssueWriter/PostingService/NoticeWorker/Read, existing cancellationreview component plus cancellationissued. Browser fixture extends existing review harness; focused command is scripts/verify-cancellationissue-browser.mjs. Sales funnel unchanged.

## Closed 2026-09-19

07-14-SUMMARY.md supersedes the in-progress sections above. All named late worker, adjustment/cancellation race and actual adjusted-ledger tests passed. Clean gate18 cases (4unit/14SQL),12 fresh HTTP captures, both final browser journeys. Current web46256/API19460. Continue07-15 inline.
