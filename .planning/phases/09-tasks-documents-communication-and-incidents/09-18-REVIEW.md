---
phase: 09
plan: '18'
status: pending-runtime-gates
---

# Final acceptance review — not yet a pass

Prepared while the complete v4 diagnostic is still running. Plan17 remains active; this review does not complete either plan or any requirement. The canonical continuation is `.local/phase9-v4-compatibility-repair-v2/report.json`.

## Evidence integrity

The discovered integration inventory is550 cases, including485 named RealSql or RealApiProcessRestart. Current unchanged production has1337 passed unit cases. Reused unit evidence must remain byte-identical and count once. The strict checker compares every discovered and executed integration name, rejects skips and requires unchanged source and assembly hashes. Interrupted earlier runs and the known-failing v4 diagnostic cannot count as final acceptance.

The diagnosed regression is in older migration compatibility assertions: current Motor Trade issue now retains initial MID work, so the newer52024 retention guard rejects downgrade before the old547 template constraint. The prepared test correction verifies the actual applicable guard and exact retained template/MID rows; it does not weaken production protection or delete history. All complete diagnostic failures must match the reviewed cause before automatic correction. Fifteen focused cases and a fresh complete inventory remain required afterward.

Existing bounded results remain:421 root tests,204 frontend tests, lint/typecheck, manifest-bound production UI/API builds and OpenAPI passed; unit source is `.local/phase9-17-agency-response-all-unit/unit.trx`. Both-product agency response browsers passed22 checks each plus SQL readback. Five commercial and ten servicing SQL/browser cases are members of the full550 inventory and are reused by exact name/result identity, never represented as newly rerun cases. Thirteen live retained servicing stages and the original underwriting aggregate run afterward. The expected strict total is1887 unique cases; it must not be reduced to accommodate failures.

Diagnostic64243 retains production1c7b285/source9f1637c hashes in `.local/phase9-final-v4-start.json`. Older continuations44048/80151 must stop without stages on its failed result. Original repair57391 must also refuse the additional commercial migration case without stages. Repair65117 then owns the focused test-only correction, v5 and all sequential continuations. Its report is authoritative; no duplicate acceptance run should be launched. Preview identities remain API88392/web2432 until the verified restart updates `.local/phase9-16-preview-pids.json`.

## Requirement-to-runtime review map

| Requirement | Concrete acceptance owner | Remaining final evidence |
| --- | --- | --- |
| OPS-01 | OperationalTaskCommand/Api/BrowserTests, TaskContextAcceptance and DriverTask tests; saved task and related-record navigation | Current full TRX, task collector and retained record readback |
| OPS-02 | OperationalWorkflowUnderwriting/Servicing/Agency/Match tests and concurrent job workflow tests | Current full TRX; prior09-04 completion remains bounded evidence |
| OPS-03 | OperationalCommunicationTests/AttachmentTests, both-product communication browser and agency-response tests | Current full TRX/collector, retained response request readback after restart |
| OPS-04 | OperationalPdfSql/Servicing/Template tests; quote, servicing, cancellation and regeneration document tests | Current full TRX/collector plus retained parsed/rendered PDF evidence already recorded in09-06/16 |
| OPS-05 | OperationalFileApi/Command/Metadata/Legacy and DocumentUpload/TaskAttachment tests | Current full TRX, ready-byte download checks, actual file hashes across initialization/restart |
| OPS-06 | OperationalDeliveryPack/Recovery/Revocation tests and both-product communication browsers | Current full TRX/collector; retained six-failure/seventh-success identities and SQL readback |
| OPS-07 | OperationalOccurrence/Incident/Claims/Recovery tests, historical context and both-product browsers | Current full TRX/collectors; saved MT/CC incident and administrator result readbacks |
| OPS-08 | OperationalMid/Servicing/Cancellation/Recovery/Browser tests and commercial exclusion | Current full TRX/collector; original MID intent/work/submission and retry history after restart |
| CC-05 | Commercial incident/claims facts and product-specific document generation/delivery | Same current commercial operational evidence; do not close from Phase8 payloads alone |
| POL-01 | Persisted policy task/document/note/message/incident entry points | Phase9 portion only; finance remains Phase10 and the compound requirement stays open |

The source denominator remains62 direct controls,34 explicit inherited controls,118 selected controls,509 display occurrences,33 commercial aliases and10 branches, plus13 separately retained inherited agency-open-item displays. Final status requires the actual current-source collectors, not just this map or static path existence.

## Preservation and acceptance ordering

Reviewed the queued sequence: strict inventory/source check; reused exact legacy cases; operational evidence collectors; retained servicing and underwriting; recheck source; verified-owned preview shutdown; two complete additive initializations with all captured business-row and key/document-file fingerprints; restart in finally; health and actual retained navigation/API/SQL readbacks. SQL and browser acceptance remain sequential. Process identity and listener ownership must match before stopping either preview process.

The initialization helper refuses an already-used evidence directory, uses the original demo password without printing it, and requires the preview workers to be stopped before capture. It compares a canonical fingerprint containing table/column/key identities, every captured row hash and file path/hash after each initialization. Final counts must come from the resulting report; the earlier74892-row/175-table/167-file migration proof is not a substitute.

## Open items and limits

- Complete diagnostic, reviewed correction, focused regression and fresh full regression are pending.
- Current operational/retained aggregates and final preservation/restart are pending.
- Final source-ledger statuses,09-17/18 summaries and goal verification must be written from those actual reports.
- 09-UI-REVIEW records four nonblocking refinements for Phase13; no unresolved HIGH/CRITICAL product finding is currently identified, but final regression remains a blocking acceptance gate.
- Human business and assistive-technology UAT, hosted CI, Docker runtime, real provider certification and production deployment are not performed.
- Demo retry scheduling was compressed for two retained jobs. Genuine attempts and stable identities are verified; elapsed real-duration backoff is not claimed.
- Preserve frontend-code, original demo keys/files and the inherited next-env.d.ts/tsconfig.json edits.
