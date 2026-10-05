# v1.1 prototype alignment

Approved 2026-10-05. Authority: `docs/prototype/Cover MGA Back Office-4.html`, its extracted template and render data. The user excludes Commercial Combined completely. The existing Motor Trade funnel is `F:/Scratches/AscendX/cx1-implementation`, not the historical `frontend-code/` snapshot. Backend/core changes needed for faithful behavior are authorized; finance/history/security invariants remain binding.

This is a source comparison baseline, not a claim that screenshots or user journeys have passed. Each section requires actual interaction and visual evidence before final acceptance. Existing control-count inventories establish coverage candidates, not parity.

| Source section | Implementation owner | Specific comparison/required work | Phase |
|---|---|---|---|
| Shell/shared record header | workspace-shell, record-header, globals.css | Shell dimensions/font already match; restore type selector; align record tab spacing and action grouping; suggestions/counts deferred to discovery phase | 14, 23 |
| pClients / pClient | clients workspace, client-detail, contacts/support components | Verify list columns/filter strip and client record tabs/fields/actions; avoid implementation-only details in user flow | 15 |
| pMatch | matching workspace and record | Evidence, relationship disclosure, decision placement and safe agency response | 15 |
| pAgents / pAgency / pNewAgency / pPortal | agency list/record/onboarding/sharing | Six-stage onboarding, agreement/contact layout and contextual invitations/actions; portal remains internal reference | 16 |
| pNewQuote | quote-create, quote-demo-import; external Motor Trade funnel | Standard create has sequential selection, demo import bypasses it with fixed identities; ensure client/agency/contact/product context precedes all capture and survives resume | 17 |
| pQuote / pRisks | quote-wizard, quote-underwriting, risk review | Compare blue header/tabs/rail; capture belongs in existing funnel; back office handles review/decisions/issue | 18 |
| pMta | servicing-workspace and Motor Trade funnel | Current independent section editors differ from requested funnel; use prefilled funnel then Changes / Review & rate / Documents with grouped before/after and visible blockers | 19 |
| pPolicy / pDriver / pVehicle / pAsAt | policy-record, policy risk/history | Compare actual tabs/rails and linked risk records; effective and known times must select actual issued versions | 20 |
| pRenewal / pCancelReview / pIssued | renewal/cancellation/issue components | Stage navigation, financial review and issued result/link parity | 21 |
| pTasks / pTask | task queues and record | Queue filters, bulk actions, checklist/comments and contextual linked record | 22 |
| Documents/messages tabs | operations document/communication components | Prototype action placement and saved delivery/retry states | 22 |
| pLogClaim / pClaim / pEscalation | incidents and escalation | Motor Trade risk selection, handoff, response thread/outcome and policy context | 22 |
| pAccounting | finance/workspace | Compare tabs, dense ledgers, allocation/refund/period actions and financial invariants | 23 |
| pReporting / pDashboard / pSearch | reporting, dashboard, search | Saved-data cards/queues, global suggestions/counts, exact filters and source links | 23 |
| pAdmin / pAccount / pLogout | administration/account/shell | Configuration tabs, approvals, personal profile/password/MFA/logout | 24 |
| pCcPolicy and CC capture branches | Existing CC implementation | Excluded; no CC redesign/integration/audit; retain existing data/code | — |

## Priority journey contracts

New Motor Trade quote: client selection or creation → agency and broker contact → eligible product/term → existing funnel capture → saved back-office review → rate/referrals → terms/acceptance → issue.

Motor Trade MTA: selected issued policy/version → effective date/requested-by → prefilled existing funnel → saved draft → grouped before/after review → rating/referrals → acceptance → issue. Draft save never changes in-force cover. Material edit invalidates applicable proofs.

Funnel host integration must remove fixed demo identity assumptions, enforce allowed origins/current actor/relationship ownership, retain typed answers/stable child IDs, and support loss-of-response retries and resume. Raw funnel state and contractual proposal remain separate; conversion needs explicit round-trip coverage.

## Final engineering verification — 2026-10-05

Phases 14–24 implement the approved Motor Trade/shared-section alignment. The section checks use the supplied prototype source and actual saved fictional application records. Visual review confirms the shared typography, narrow sidebar, blue record headers, tab order, contextual actions, panels and responsive containment. This establishes source-driven alignment, not pixel equality or human business acceptance. Different record values, actual authorization and persisted prerequisites replace prototype placeholders.

| Section | Delivered behavior and actual evidence |
|---|---|
| Shared shell/dashboard/search | Prototype shell, greeting, contextual quote/search/tasks, current saved queues and source drill-down. `discovery-alignment-report.json`, `visual-dashboard-desktop.png` and mobile capture. |
| Clients/contacts/matching | Saved client portfolio and current term premiums, sole-relationship contacts, restricted support preview, captured matching signals, reasoned query/link and exact lost-response replay. `party-alignment-report.json`, client/matching screenshots; 12 current party contracts include consent and agency disclosure. |
| Agency/onboarding/sharing | Prototype tabs and published product facts; real 90-day comparison and broker balances; six-stage incomplete onboarding save/exit/resume and activation blockers; administrator invitation/suspension entry points and internal sharing boundaries. Party/discovery/section reports. Invitations, agreements and independent approvals retain their existing normal workflows; no new broker portal is implied. |
| Quote entry/capture/review/issue | Client, agency, broker and eligible product precede creation. Existing cx1-implementation capture saves raw answers and typed proposal with stable identities. Supplemental source declarations include driver history and Combined requested cover. `quote-issue-alignment-report.json`: complete fictional API prefill, actual source-funnel proposer edit, normal rating/proof/terms/acceptance, UI issue, exact lost-response replay, balanced opening journal and saved policy reload. This was not a blank fourteen-stage manual entry test. |
| MTA capture/review/issue | Issued-base prefill in the same source funnel, effective/requested-by context, Changes / Review & rate / Documents, grouped before/after and additional reviewed declarations. `mta-funnel-report.json` (phase 19) and `mta-issue-alignment-report.json`: neutral capture, unchanged issued cover before issue, lease/current-version fencing, retained raw state, lost save plus denial/exact retry and one issued transaction with linked balanced finance. |
| Policy/driver/vehicle/history | Prototype tabs/actions, linked saved risk records, issued history and explicit effective/known times. Phase 20 policy evidence plus final section desktop/mobile captures. |
| Renewal/cancellation/issued confirmation | Normal experience and proof review, saved terms/acceptance and UI issue; cancellation financial/notice review and approved issue. `lifecycle-issue-alignment-report.json`: lost issue responses followed by denial and exact retry, saved issued confirmations and London effective/recorded dates. |
| Tasks/documents/communications | Actual saved task completion; actual generated PDF bytes and checksum; exact generation replay, recipient/version-bound document pack and saved delivered demo outcome. Phase 22 operations evidence and `operations-outcome-alignment-report.json`. |
| Incidents/claims/capacity | Historical-cover incident handoff, exact lost-response replay and received read-only provider summary. Future incident correctly remains a draft. Separate normal past-inception fixture supports historical cover. Combined capacity query, reviewed uploaded carrier letter, recorded decline and authority context reload. Operations/capacity outcome reports. |
| Accounting/reporting | Saved accounting tabs, receipt/payer assignment, exact allocation, cancellation credit refund with two independent approvals, separately saved paid demo payment; reporting filters, favourites and source CSV. `finance-alignment-report.json`, discovery evidence. Temporary fictional countersigner finance access was granted and restored through independently approved user administration. |
| Administration/account | Exact eleven tabs and ten category cards, real configuration routes, saved profile, sign-out-everywhere, password change/restoration, actual TOTP enrollment, recovery login and MFA disable/restoration. `administration-account-alignment-report.json`; no secrets retained in evidence. |

Reports and screenshots live in ignored `output/playwright/v1.1/`; reproducible journey scripts are versioned under `scripts/verify-*alignment*.mjs`. Fixture credentials, database connections and recovery material remain private under `.local/`. Section screenshots include 1560px desktop and 390px mobile views; wide tables/tabs use their own scroll containers. Earlier failure screenshots remain diagnostic history, not successful evidence.

Final focused checks: TypeScript and changed-component ESLint; 42 funnel/servicing/finance contracts; 16 cancellation/renewal/claims/document contracts; 12 client/agency contracts; two isolated SQL Motor Trade multi-date issue tests, including rollback at issue graph boundaries, competing requests, exact replay, preserved old snapshots/journals and atomic finance. Earlier phase-specific checks remain recorded in their summaries.

The hosted funnel/back-office identity and raw-answer connection is explicitly approved; see `docs/MOTOR-TRADE-FUNNEL-CONNECTION.md`. Work is committed locally in both repositories. This milestone does not claim hosted rollout, production providers, real money/delivery, human UAT or the previously deferred SQL-engine restart. CC was excluded; historical code/data and accepted v1.0 debt remain intact.
