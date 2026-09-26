# Local acceptance record

Status: Phase 13 acceptance in progress. Phase 12 production ends at `7179127`; its closeout is `329f9c7`. This record distinguishes fresh checks from reused historical evidence. It is not a production certification or human UAT sign-off.

## Evidence available

| Scope | Result and source |
|---|---|
| Original prototype identities | 949/949 mapped, zero missing, 537 referenced API operation identities validated, twelve phase verification files linked. `node scripts/build-milestone-acceptance.mjs` regenerates [implementation-evidence.json](implementation-evidence.json). A reviewed mapping is not itself a live test. |
| Current backend unit tests | 1,400 passed, zero skipped: `.local/phase13-tests/unit.trx`. |
| Current frontend tests | 214 passed, zero skipped: `.local/phase13-web-tests.txt`. |
| Current root tests | 450/451 initially passed; one historical/current reporting mapping comparison was corrected, then all four ownership cases passed. All 451 distinct cases have passing results across this run and correction. `.local/phase13-tests/root.txt`, `quote-ownership-corrected.txt`. No runtime failure was hidden by the correction. |
| Phase 12 changed behaviour | Nine distinct backend cases including five isolated SQL, 45 API contracts, 12 saved-browser assertions; TypeScript/lint/API build. [Verification](../../.planning/phases/12-dashboards-search-and-reports/12-VERIFICATION.md). |
| Phase 11 changed behaviour | Seventeen distinct backend cases including ten SQL, API contracts and saved administration/security/browser families. [Verification](../../.planning/phases/11-configuration-and-account-administration/11-VERIFICATION.md). |
| Historical broad SQL baseline | Phase 10: 1,400 unit + 577 integration = 1,977 unique cases, 512 named real SQL, zero skips; 451 root/214 frontend, finance/operations browsers and ten restart readbacks. [Verification](../../.planning/phases/10-accounting-and-insurer-reporting/10-VERIFICATION.md). This six-hour integration run predates Phase 11/12 and is not claimed against today's exact source. |
| Earlier lifecycle and operational flows | Phase 9 broad gate plus 13 servicing stages, 37 quote/client/agency journeys, MID/retry/communications/documents and Commercial Combined flows. [Verification](../../.planning/phases/09-tasks-documents-communication-and-incidents/09-VERIFICATION.md). |

## Remaining acceptance scope

Owned application restart, keyboard/narrow-screen navigation and current handover documentation complete in the remaining Phase 13 slices. Human business/assistive-technology UAT, SQL engine restart, Docker runtime, hosted CI and production deployment remain unperformed. A SQL Server service restart would disrupt other databases on this workstation; it is not part of the isolated fixture check.

Known limitations: retained historical Commercial Combined PDF continuation spacing is tight; earned report months must fit one configured accounting period; CSV and screen paging deliberately rerun current data. Retained privileged-reviewer provisioning was rejected by automatic approval review in Phase 11 and has not been bypassed. Local deterministic adapters do not make real customer, carrier or payment calls.
