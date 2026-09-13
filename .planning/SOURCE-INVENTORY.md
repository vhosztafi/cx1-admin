# Source inventory and review map

Reviewed 2026-09-13. This is an initial method-level inventory, not the exhaustive Phase 1 control catalogue.

## Prototype

`docs/prototype/Cover MGA Back Office-4.html`

SHA256: `8F2CEBFC855FEF104A24A53CDA82DDDD7DD9774000B287EEC9BB050335048A77`

The outer HTML is a bundler. Parse the JSON string in `script[type="__bundler/template"]` to inspect actual HTML and application methods. Do not edit the source. A direct line search can print very large embedded resource payloads.

| Methods | Feature coverage | Owning phases |
|---|---|---|
| pDashboard, pSearch, pRisks, filt | Dashboard, global/advanced search, lists | 5, 12 |
| pClients, pClient, pMatch, flagSecs, flagTable | Accounts, contacts, duplicate evidence, support flags | 3 |
| pAgents, pAgency, pNewAgency, pPortal | Agency onboarding/access/accounts; internal sharing preview | 4 |
| pNewQuote, pQuote, quoteMenu | Product selection, MT and CC capture, quote actions | 5, 6, 8 |
| AUTHORITY, DIMENSION_INFO, pEscalation, escSend | Product dimensions, authority and capacity-provider response | 6, 8, 11 |
| pPolicy, policyTab, policyMenu, pVehicle, pDriver | Policy tabs and risk record details | 7 |
| pMta, rateMta, submitMta, dateRules | Draft changes, locks, rate/referral/issue prerequisites | 7 |
| VERSIONS, pAsAt, pIssued | Issued history, temporal queries and confirmation | 6, 7 |
| pRenewal, pCancelReview | Renewal/invitation/lapse; cancellation/return premium | 7 |
| pCcPolicy, PRODUCT_TABS | Product-driven sections and shared lifecycle | 8 |
| pTasks, pTask | Queues, assignment, task history and links | 9 |
| pLogClaim, logClaim, pClaim, claimDefaults | Incidents, handoff, administrator summary | 9 |
| pAccounting | Ledger, accounts, statements, payments, reconciliation, bordereaux and refunds | 10 |
| pReporting | Report categories, filters, favourites, performance measures | 12 |
| pAdmin | Users, matching rules, products, authority, workflow, templates, integrations, audit, settings | 11 |
| pAccount, faWizard, faOn, faOff, pLogout | Profile, password, local MFA/recovery and sessions | 2, 11 |
| modalVals, addTo, removeFrom, VEHLOOKUP | All modal editors, validation, additions/removals and lookup outcomes | Corresponding feature phases |

Phase 1 must inventory inline callbacks too: many workflow actions are defined in table cells or return objects and only show mock toasts. Include all modal kinds, file/export links, reporting cards, help actions and permission-disabled branches. Catalogue each control's inputs, effects, validation, permission, error state and persisted record. Do not count a method mention as verified functionality.

## Funnel reference, read-only

- `frontend-code/AGENTS.md`: snapshot-specific scope and canonical ownership.
- `frontend-code/docs/motor-trade-funnel/04-llm-implementation-handoff.md`: canonical source index for deeper Phase 1 mapping.
- `frontend-code/src/domain/types.ts`: raw inputs and option identity semantics.
- `frontend-code/src/domain/converter.ts`: projection/sanitisation; distinct from raw persisted form.
- `frontend-code/src/domain/terminal.ts`: local prepared arguments; no transport/result contract implemented.
- `frontend-code/src/development/demo-sample.ts`: sample source for field mapping, to inspect in Phase 1.

The snapshot is ignored by root Git. No modifications, copying of secrets, execution or dependency installation in that directory is required for this milestone. Legacy clarification gaps stay recorded; future API integration is a separate requirement.
