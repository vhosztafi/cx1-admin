---
phase: 09
plan: '18'
status: in-progress
---

# Final verification review

The current source is frozen while `.local/phase9-final-reviewed-run.ps1` runs the complete discovered integration inventory. Start metadata, file and assembly hashes are in `.local/phase9-final-reviewed-start.json`. Neither an active process nor a discovery count is passing evidence.

| Gate | Evidence and current status |
| --- | --- |
| Backend units | 1,328 passed; original `.local/phase9-final-current/unit/unit.trx` copied unchanged to the reviewed result directory. One run, counted once. |
| Full integration | 549 discovered cases, including 484 real-SQL/process-restart cases. Running; final TRX and strict inventory comparison pending. |
| Earlier full attempt | `.local/phase9-final-current` SQL attempt interrupted to fix the query-correspondence entry. Excluded from acceptance; retained for diagnosis. |
| Root/frontend/build | Current bounded root 420 and frontend 203 passed; lint and validated production build passed. Exact report references remain in 09-17 review/checkpoint. |
| Source review | Original control/display/branch denominators preserved; implementation mappings exist. Final current runtime evidence reconciliation pending. |
| Operational collector | Run only after the active full suite finishes; reuse its fingerprinted browser/SQL reports. Do not launch a duplicate 25-case suite. |
| Commercial and servicing browser cases | Already included in the 549-case inventory. Final accounting must reference their actual successful full-run results; do not fabricate separate TRX reports or claim wrappers were rerun. |
| Retained journeys | Retained underwriting and servicing live stages still required sequentially after the full suite. |
| Demo preservation | Earlier two initializations preserved 74,856 rows across 175 tables and 167 files. Final snapshot/two initializations/restart/readback remains pending after retained journeys. |
| Human UAT | Unperformed. No engineering report substitutes for business or assistive-technology acceptance. |

`.local/phase9-final-reviewed-check.ps1` checks successful completion, unchanged captured sources/assembly, exact discovered/executed integration names and the strict no-skip result gate in a child PowerShell. Expected unique count is 1,877 if the complete current inventory passes. Do not reduce the inventory or minimum to accommodate failures.

No source or compiled bundle is to be changed during this run. A material failure requires preserving its evidence and fixing the cause before claiming the affected acceptance. Preview restarts must verify recorded process identities and preserve original data-protection keys, document bytes and retained database records.
