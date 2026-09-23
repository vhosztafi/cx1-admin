---
phase: 09
plan: '18'
status: final-current-source-gate-running
---

# Full regression and preserved demo restart

The final current-source v7 integration run is in progress. This summary is a checkpoint, not a completed plan. Its unit gate passed 1,337/1,337; integration discovery contains exactly 550 cases. The immediately preceding complete v6 run passed all 550 integration cases and all 1,337 units, with 485 named real SQL/process-restart cases, 1,887 unique IDs and no skips. The v6 source snapshot covered 1,883 tracked files and the integration assembly. Two subsequent commits corrected only renewal and client browser verification scripts; the corrected paths passed targeted and retained collectors. Final acceptance must come from the v7 finish and streaming strict report, not an inferred reuse of v6.

Ten operational browser families and retained demo readbacks passed. Clean retained servicing passed 13 live stages and reused ten precisely identified v6 integration cases; clean underwriting passed four stages including 37 quote/agency/client journeys. The exact report paths and the earlier failed attempts are documented in `09-18-REVIEW.md`. The product preview was built from the frozen Phase9 acceptance source and used the original demo keys and file root. No external provider/payment/customer calls were made.

Two additive initialization runs preserved every captured business row and key/document file: 89,821 rows across 176 tables and 215 files with identical before/after fingerprints. The initial wrapper was interrupted after a successful preview launch because its background child processes held redirected handles; it does not claim a complete run. A later verified owned restart and eight retained browser/API/SQL readbacks passed. Evidence is `.local/phase9-final-v6-preservation-v3/initializations/report.json` plus `.local/phase9-final-v6-preservation-v3/readbacks-report.json`. The current preview identities are in `.local/phase9-16-preview-pids.json`; inspect owner, binary and listener before changing processes.

The full TRX is large enough to exceed PowerShell `[xml]` document limits. `.local/phase9-final-v7-stream-check.ps1` is a streaming strict checker that verifies the actual result identities and counters against discovery, timestamps, source/assembly hashes, unique IDs, nonzero native SQL and no skips while retaining the complete TRX. `.local/phase9-final-v7-followthrough/report.json` is the single guarded continuation for that check, root tests, frontend tests/lint/typecheck/build, and final per-binding source-ledger reconciliation. Its report must pass before this plan's summary, requirements or roadmap are changed to complete.

POL-01 remains partial through Phase10 finance. Phase11/12 controls retain their future owners; Phase13 owns human business/assistive-technology acceptance. The old retained Commercial Combined PDF pack has a medium final-page margin/header limitation, while newer schedules have the expected header and margin. Demo retry scheduling was compressed for two jobs, with actual attempts, stable identities and saved readbacks verified. Hosted CI, Docker runtime and real provider certification remain outside this local gate.
