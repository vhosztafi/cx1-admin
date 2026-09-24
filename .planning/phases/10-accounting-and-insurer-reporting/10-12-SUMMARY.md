---
phase: 10-accounting-and-insurer-reporting
plan: '12'
subsystem: finance-ui
tags: [finance, react, nextjs, browser, saved-records]
requires: [10-05, 10-06, 10-08, 10-10, 10-11]
provides: [seven-tab-accounting-workspace, scoped-policy-and-agency-navigation, saved-bordereau-row-actions, live-browser-evidence]
affects: [10-13, phase-10-verification]
tech-stack:
  added: []
  patterns: [saved-ID-navigation, exact-decimal-money, guarded-command-readback, version-pinned-download]
key-files:
  created:
    - apps/backoffice/lib/finance-workspace-api.ts
    - apps/backoffice/components/finance/bordereaux.tsx
    - apps/backoffice/components/finance/overview.tsx
    - apps/backoffice/components/finance/reconciliation.tsx
    - apps/backoffice/components/finance/refunds.tsx
    - apps/backoffice/components/finance/transactions.tsx
    - apps/backoffice/components/policies/policy-finance.tsx
    - apps/backoffice/tests/finance-workspace.test.mjs
    - scripts/verify-finance-workspace-browser.mjs
    - .planning/phases/10-accounting-and-insurer-reporting/10-12-BINDINGS.md
  modified:
    - apps/backoffice/components/finance/workspace.tsx
    - apps/backoffice/app/(workspace)/policies/[id]/page.tsx
    - apps/backoffice/app/(workspace)/agents/[agencyId]/page.tsx
    - apps/backoffice/components/admin-workspace.tsx
decisions:
  - Finance-only actors can open the policy-scoped finance panel or agency-scoped account without gaining the full servicing record.
  - Exact statement and bordereau exports use saved version bytes and hashes; uncertain commands retain identity and reload their saved outcome.
  - Earned premium and an aggregate refund pending count remain unreported where no pinned earning schedule or refund list API exists.
requirements-completed: []
metrics:
  completed: 2026-09-24
  tasks: 2
  browser-checks: 21
  frontend-tests: 5
---

# Phase 10 Plan 12: Saved accounting workspace and scoped navigation

The seven accounting tabs now show scoped, persisted finance records and issue guarded actions; inherited policy, agency and admin links carry the saved identity to the right view.

## What changed

- Overview reads ledger, account ageing, reconciliation, bordereau and period review from live APIs. It offers saved statement export and reasoned, balanced later journal correction. Transactions distinguish posting and effective dates and retain the selected transaction across reload. Broker accounts and payments open exact saved agency and receipt IDs.
- Reconciliation exposes bank line residuals, matches, exclusions, variance explanations and reversals. Refund detail exposes request, approval rule and decision chain, payment state and guarded review actions. Period close displays blockers and requires a saved checklist and reason.
- Bordereaux use exact provider, batch, version and member IDs. Each failing member has its own mapping correction and reasoned exclusion. Validation creates a linked successor; only an eligible validated version offers submission or its saved CSV bytes. Submitted historical versions remain downloadable under current authority after a successor.
- The policy route gives finance users the scoped finance panel without opening the full servicing record. Servicing users see that panel on the policy record. Agency and admin entries lead to the selected account and bordereaux. URL state retains agency, policy, receipt, transaction and batch IDs where applicable.
- `.planning/phases/10-accounting-and-insurer-reporting/10-12-BINDINGS.md` maps all 24 direct controls, 3 inherited controls and 11 implied obligations to their current UI binding and explicitly labels two partial obligations. No schema migration, generated API contract or backend registration changed in this UI-only plan.

## Evidence

| Gate | Result | Evidence |
| --- | --- | --- |
| Frontend unit | 5 passed, 0 failed | `node --test apps/backoffice/tests/finance-workspace.test.mjs` |
| Live Chrome collector | 21 passed, 0 failed, no page errors | `.local/phase10-12-browser/browser-report.json` |
| Typecheck | passed | `tsc --noEmit --project apps/backoffice/tsconfig.json` |
| Scoped ESLint | passed, zero warnings | `eslint app components lib --max-warnings 0` in `apps/backoffice` |
| Isolated production build | passed, including Next typecheck | `next build --webpack` with `COVER_NEXT_DIST_DIR=.local-next-phase10-12` |
| Staged whitespace | passed | `git diff --cached --check` |

The browser collector uses the retained fictional local demo through owned API port 5092 and web port 3192. It generated batch `1fdcbcd1-93e8-47c5-b101-f068e3f892c8`, persisted three distinct invalid member mappings, corrected the second row after an intentionally dropped response with the same key/body/ETag, excluded the third row with a saved reason, revalidated and corrected the last row, then checked exact CSV SHA-256, submission and historical byte-for-byte download after a linked successor. It also checked scoped policy/agency links, refund entry, a denied foreign agency, reload, keyboard focus, mobile and 200% zoom. Screenshots: `.local/phase10-12-browser/overview-desktop.png`, `overview-mobile.png`, `overview-zoom200.png`.

Additive `--initialize-demo` applied only the existing schema and preserved sampled business identity counts: Policy 41, Journal 72, Receipt 9, FinanceStatementVersion 3 before and after. Migration head was `20260924152124_FinancePeriodCloseCorrections`. The collector created fictional bordereau records; it made no real insurer, bank or payment call. Existing port 3100 was untouched. Owned 5092/3192 previews remain running for downstream verification. Isolated Next output is ignored; generated `apps/backoffice/AGENTS.md` and `CLAUDE.md` remain untracked. Inherited dirty `next-env.d.ts`, `tsconfig.json` and `.idea/` were not staged.

## Review, limitations and handoff

- The source UI specification asks for monthly pro-rata earned premium, but the ledger read model does not expose a pinned coverage earning schedule with dates and versioned amounts. The overview shows exact written and cash metrics and does not invent earned premium. FIN-01 and the overall phase should remain partial until that evidence exists.
- There is no scoped refund list endpoint from which to derive a current pending count. The overview links to the saved-ID refund review; it does not display a fabricated count. See the two partial rows in `10-12-BINDINGS.md`.
- The live browser gates prove representative saved workflows, including second and third failing bordereau rows. The backend's native SQL gates from dependency plans retain direct source, race and immutability proof. Plan 10-13 should run the full current-source gate and record these two presentation gaps honestly.

## Deviations from Plan

- **[Rule 2 - Scoped policy finance access]** A finance user could otherwise reach a policy finance link only through the servicing-owned full policy page. Added a narrowly scoped finance-only entry while retaining servicing authorization for the full record. Commit `7b94130`.
- **[Rule 1 - Member issue identity]** The browser exposed inconsistent case in saved issue member GUIDs. The bordereau UI normalizes identity comparison so every invalid member receives its own correction/exclusion control. Commit `7b94130`.
- **[Rule 1 - Exact money]** Frontend sums and labels use checked integer/BigInt arithmetic, including large pence values; no floating rounding affects displayed pennies. Commit `7b94130`.

## Known Stubs

No rendered mock figures or disconnected command stubs remain in the implemented views. Input `placeholder` attributes describe expected values; they are not displayed as finance results. The two absent aggregate metrics above are explicit data-contract gaps.

## Threat Flags

| Flag | File | Description |
| --- | --- | --- |
| threat_flag: scoped-read | `apps/backoffice/app/(workspace)/policies/[id]/page.tsx` | New finance-only route branch calls the existing policy-scoped authorized finance API. |
| threat_flag: finance-command-ui | `apps/backoffice/lib/finance-workspace-api.ts` | Browser commands carry CSRF, idempotency key and version precondition; API remains authoritative. |

## Task commits

1. `7b94130` — accounting workspace, navigation, binding inventory and focused tests.
2. Summary documentation — this commit.

## Self-Check: PASSED

Implementation, binding inventory, collector and its 21-check report exist; commit `7b94130` exists. Summary evidence was checked against the final report and staged diff.
