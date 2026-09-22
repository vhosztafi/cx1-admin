---
phase: 09
plan: '18'
status: in-progress
---

# Final verification review

The first reviewed full run was interrupted after a current-build commercial browser exposed missing selected-version document request history. Its start metadata and source/assembly hashes remain in `.local/phase9-final-reviewed-start.json`, failure log and interruption marker alongside it. A shared request-history fix is undergoing focused verification. The next full run is prepared as `.local/phase9-final-v3-run.ps1`; it has not yet run. Neither an active process nor a discovery count is passing evidence.

| Gate | Evidence and current status |
| --- | --- |
| Backend units | 1,328 passed; original `.local/phase9-final-current/unit/unit.trx` copied unchanged to the reviewed result directory. One run, counted once. |
| Full integration | 549 discovered cases, including 484 real-SQL/process-restart cases. Fresh full run, final TRX and strict inventory comparison pending after the request-history fix. |
| Earlier full attempt | `.local/phase9-final-current` SQL attempt interrupted to fix the query-correspondence entry. Excluded from acceptance; retained for diagnosis. |
| Reviewed full attempt | `.local/phase9-final-reviewed` SQL attempt interrupted after the missing request-history failure. Also excluded from acceptance. |
| Root/frontend/build | Current bounded root 420 and frontend 203 passed; lint and validated production build passed. Exact report references remain in 09-17 review/checkpoint. |
| Source review | Original control/display/branch denominators preserved; implementation mappings exist. Final current runtime evidence reconciliation pending. |
| Operational collector | Run only after the active full suite finishes; reuse its fingerprinted browser/SQL reports. Do not launch a duplicate 25-case suite. |
| Commercial and servicing browser cases | Already included in the 549-case inventory. Final accounting must reference their actual successful full-run results; do not fabricate separate TRX reports or claim wrappers were rerun. |
| Retained journeys | Retained underwriting and servicing live stages still required sequentially after the full suite. |
| Demo preservation | Earlier two initializations preserved 74,856 rows across 175 tables and 167 files. Final snapshot/two initializations/restart/readback remains pending after retained journeys. |
| Human UAT | Unperformed. No engineering report substitutes for business or assistive-technology acceptance. |

`.local/phase9-final-v3-check.ps1` checks successful completion, unchanged captured sources/assembly, exact discovered/executed integration names and the strict no-skip result gate in a child PowerShell. Expected unique count is 1,877 if the complete current inventory passes. The unchanged assembly's discovered inventory and unchanged backend unit report are reused, not counted as new executions. Do not reduce the inventory or minimum to accommodate failures.

No source or compiled bundle is to be changed during this run. A material failure requires preserving its evidence and fixing the cause before claiming the affected acceptance. Preview restarts must verify recorded process identities and preserve original data-protection keys, document bytes and retained database records.
