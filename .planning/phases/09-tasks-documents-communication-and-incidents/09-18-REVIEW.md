---
phase: 09
plan: '18'
status: in-progress
---

# Final verification review

Current full regression v4 runs in session64243 against production1c7b285/source9f1637c. Its exact source/assembly hashes and550-case inventory are retained in `.local/phase9-final-v4-start.json`; output is `.local/phase9-final-v4-sql.log`. An active process or discovered inventory is not passing evidence. Previous full attempts current/reviewed/v3 were interrupted to fix query entry, selected-version document requests and agency-sharing open items; retain them as diagnostic evidence only.

| Gate | Evidence and current status |
| --- | --- |
| Backend units |1337passed in `.local/phase9-17-agency-response-all-unit/unit.trx`, copied byte-for-byte to v4/unit. Counted once. |
| Full integration |550discovered, including485realSQL/process-restart. Session64243 active; final TRX and strict inventory/source validation pending. |
| Root/frontend/build |421root,204frontend,lint/typecheck,current manifest-bound production UI/API build andOpenAPI passed. See09-17 review/checkpoint for reports. |
| Agency response scope/lifecycle |MT/commercial terms and explicit delivered-message tracking/closure passed scoped SQL; both-product browser2/2 with22checks each and SQL readback. Retained migration preserved74892original rows/175tables/167files. |
| Source review |Original118control/509display/33commercial/10branch denominators preserved; supplemental13raw agency open-item displays mapped independently. Final current runtime evidence reconciliation pending. |
| Operational collector |Queued after full regression, strict source/inventory and legacy accounting; validates actual current fingerprinted browser/SQL reports. No duplicate25-case suite. |
| Commercial/servicing SQL browsers |Five commercial and ten servicing cases are included in the550-case inventory. Reuse actual full-run results with names/IDs/timestamps and TRX hash; no synthetic reports or claim of repeated execution. |
| Retained journeys |Thirteen live servicing stages plus original underwriting aggregate are queued sequentially after the operational collector. |
| Demo preservation |Earlier two initializations preserved74856rows/175tables/167files; subsequent additive response migration preserved74892rows/175tables/167files. Final fresh snapshot/two initializations/restart/readbacks remain pending after retained journeys. |
| Human UAT |Unperformed. Engineering evidence does not substitute for business or assistive-technology acceptance. |

`.local/phase9-final-v4-check.ps1` requires successful unchanged completion, verifies every captured runtime source hash and exact discovered/executed test names, then invokes the strict no-skip/nonzero-SQL validator in a child PowerShell. Expected total1887=1337units+550integration, with485realSQL/process-restart cases. Minimums must not be reduced to accommodate failures.

Guarded continuation session44048 (`.local/phase9-final-v4-followthrough.ps1`) waits for this exact finish marker before sequential strict validation, legacy case accounting, operational collector, retained servicing and retained underwriting. Its report is `.local/phase9-final-v4-followthrough/report.json`; it stops on the first failure. Do not duplicate any queued acceptance stage. Final preservation and goal-backward review remain outside this continuation.

Runtime/UI/contracts/scripts remain frozen during the full run. Preview API88392/web2432 use the original keys/files and current acceptance build. Verify identity before any later process stop. All original demo rows, issued versions, journals, document bytes and frontend-code remain preserved. POL-01 remains partial throughPhase10. CC-05/OPS completion requires actual final evidence; no phase completion is asserted here.
