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
