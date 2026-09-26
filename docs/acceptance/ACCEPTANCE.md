# Local acceptance record

Status: Phase 13 local automated acceptance and handover complete. Phase 12 production ends at `7179127`; its closeout is `329f9c7`. This record distinguishes fresh checks from reused historical evidence. It is not a production certification or human UAT sign-off.

## Evidence available

| Scope | Result and source |
|---|---|
| Original prototype identities | 949/949 mapped, zero missing, all mapped operation IDs resolve within the 537-operation contract, twelve phase verification files linked. `node scripts/build-milestone-acceptance.mjs` regenerates [implementation-evidence.json](implementation-evidence.json). A reviewed mapping is not itself a live test. |
| Current backend unit tests | 1,400 passed, zero skipped: `.local/phase13-tests/unit.trx`. |
| Current frontend tests | 214 passed, zero skipped: `.local/phase13-web-tests.txt`. |
| Current root tests | 450/451 initially passed; one historical/current reporting mapping comparison was corrected, then all four ownership cases passed. All 451 distinct cases have passing results across this run and correction. `.local/phase13-tests/root.txt`, `quote-ownership-corrected.txt`. No runtime failure was hidden by the correction. |
| Phase 12 changed behaviour | Nine distinct backend cases including five isolated SQL, 45 API contracts, 12 saved-browser assertions; TypeScript/lint/API build. [Verification](../../.planning/phases/12-dashboards-search-and-reports/12-VERIFICATION.md). |
| Phase 11 changed behaviour | Seventeen distinct backend cases including ten SQL, API contracts and saved administration/security/browser families. [Verification](../../.planning/phases/11-configuration-and-account-administration/11-VERIFICATION.md). |
| Historical broad SQL baseline | Phase 10: 1,400 unit + 577 integration = 1,977 unique cases, 512 named real SQL, zero skips; 451 root/214 frontend, finance/operations browsers and ten restart readbacks. [Verification](../../.planning/phases/10-accounting-and-insurer-reporting/10-VERIFICATION.md). This six-hour integration run predates Phase 11/12 and is not claimed against today's exact source. |
| Earlier lifecycle and operational flows | Phase 9 broad gate plus 13 servicing stages, 37 quote/client/agency journeys, MID/retry/communications/documents and Commercial Combined flows. [Verification](../../.planning/phases/09-tasks-documents-communication-and-incidents/09-VERIFICATION.md). |

## Remaining acceptance scope

Owned application restart, keyboard/narrow-screen navigation and current handover documentation are complete; see the evidence below and HANDOVER.md. Human business/assistive-technology UAT, SQL engine restart, Docker runtime, hosted CI and production deployment remain unperformed. A SQL Server service restart would disrupt other databases on this workstation; it is not part of the isolated fixture check.

Known limitations: retained historical Commercial Combined PDF continuation spacing is tight; earned report months must fit one configured accounting period; CSV and screen paging deliberately rerun current data. Retained privileged-reviewer provisioning was rejected by automatic approval review in Phase 11 and has not been bypassed. Local deterministic adapters do not make real customer, carrier or payment calls.
## Owned restart and visual acceptance — 26 September 2026

Eight browser checks passed in `.local/phase13-tests/restart-browser.json`: the same authenticated session, favourite ID/filters and three product quote IDs survived owned API/frontend restart; finance export remained forbidden to the underwriter; repeated quote/report navigation, skip link, drawer Escape/focus return and narrow page bounds passed. `.local/phase13-tests/preservation.json` verifies the same one Data Protection key file/hash. The database and file/key location stayed unchanged; the SQL engine was not restarted.

Four additional loaded-state visual checks passed in `loaded-visual.json`, with no browser page errors. Initial narrow screenshots captured loading states, so they were superseded by `*-loaded.png` captures. Visually inspected the source prototype, current desktop dashboard/reporting, loaded narrow tasks/search and report layout bounds. The sidebar, typography, panel treatment and controls follow the prototype; actual counts and role-specific queues replace fixed samples. Wide record tables scroll within their panel at 390px instead of expanding the page.

Screenshots are in ignored `output/playwright/phase13/`. Scripts: `verify-acceptance-browser.mjs prepare|verify` and `verify-acceptance-visual.mjs`. Browser cookies are stored only under ignored `.local/phase13-tests`; never publish that storage-state file. The owned preview remains available at localhost:3193 with API 5095. Historical retained preview and SQL service were not stopped.

The final shell check confirms the implemented Password tab without credential changes (`account-shell.json`). Sixteen local documentation links resolve. See [phase verification](../../.planning/phases/13-complete-demo-and-acceptance/13-VERIFICATION.md) and [developer handover](../HANDOVER.md).

## Compound record reconciliation — 26 September 2026

Current source wiring was reviewed against existing passing phase evidence; this is not a fresh issued-policy browser run in the Phase 11/12 fixture.

| Requirement | Current wiring and recorded runtime evidence |
|---|---|
| AGY-03 | agency-detail.tsx loads persisted products, permission/access and activity; Accounts connects the agency-scoped accounting workspace. Phase 4 verifies agency administration and Phase 10 verifies ledger/statements and scoped balances. |
| AGY-04 | Agency sharing provides restricted quote/policy projections and public response/open items. Private operational tasks are excluded. Phase 4/6/9 evidence covers grants, relationship scope and response disclosure; AgencySharingProjectionTests and OperationalAgencyResponseTests retain the regression cases. |
| POL-01 | policy-record.tsx and commercial-policy-record.tsx connect product sections, Motor Trade drivers/vehicles, history/transactions, PolicyFinance, RecordDocuments, RecordTasks, RecordCommunications and RecordIncidents to actual policy/version IDs. Phases 7/8 verify product/lifecycle views, Phase 9 documents/tasks/communication/incidents and Phase 10 reconciled finance. |

These three implementation requirements are complete on combined source review and recorded runtime evidence. Human end-to-end review remains outstanding. ACC-02 is only partially verified: owned application restart passed, but the shared SQL Server engine was not restarted. Its recovery procedure is documented; no database restart pass is claimed.
