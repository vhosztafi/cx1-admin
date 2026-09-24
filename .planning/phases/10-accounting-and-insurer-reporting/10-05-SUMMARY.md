---
phase: 10-accounting-and-insurer-reporting
plan: '05'
subsystem: finance-ui
tags: [finance, receipts, statements, react, nextjs, playwright]
requires: [10-02, 10-03, 10-04]
provides: [saved-receipt-workspace, payer-and-allocation-ui, agency-account-statement-ui, live-browser-journey]
affects: [10-11, 10-12, 10-13]
tech-stack:
  added: []
  patterns: [exact-saved-id-navigation, frozen-idempotent-command-retry, integer-pence-display]
key-files:
  created:
    - apps/backoffice/lib/finance-api.ts
    - apps/backoffice/components/finance/receipts.tsx
    - apps/backoffice/components/finance/accounts.tsx
    - apps/backoffice/components/finance/workspace.tsx
    - apps/backoffice/tests/finance-receipts.test.mjs
    - scripts/verify-finance-receipts-browser.mjs
  modified:
    - apps/backoffice/app/(workspace)/[section]/page.tsx
    - apps/backoffice/app/(workspace)/agents/[agencyId]/page.tsx
    - apps/backoffice/components/agencies/agency-detail.tsx
    - apps/backoffice/app/globals.css
decisions:
  - Finance commands retain their serialized body, key and If-Match value across uncertain responses; a stale version requires review before a new command.
  - Invoice candidates derive residual and payment state from scoped ledger applications, then resolve the saved obligation ID through transaction detail before allocation.
  - Agency and receipt or statement IDs stay in the URL; finance reads and downloads use current server authorization and no-store responses.
requirements: [FIN-03, FIN-01]
requirements-completed: []
metrics:
  completed: 2026-09-24
  duration: approximately 90 minutes
  tasks: 2
  tests: 5 frontend unit, 10 live browser checks
---

# Phase 10 Plan 05: Saved receipts and broker accounts

The accounting workspace now shows saved cash, payer history, invoice applications, residuals, account movements and immutable statement versions through the current finance API.

## What changed

- `/accounting?tab=payments&agencyId=...&receiptId=...` lists scoped receipts, opens an exact saved receipt, records cash, assigns its payer, offers outstanding invoice candidates, applies an amount and saves a reasoned reversal. Receipt details show persisted cash, allocated and residual amounts, assignment version and application state after reload. The agency Accounts tab links to the exact agency account for staff with both agency and finance roles. The accounting server page checks the finance role before rendering the workspace; API reads and writes independently check current stored authority.
- `finance-api.ts` uses canonical decimal request strings and checked integer pence. It derives invoice residual and payment state from scoped ledger debtor movements; a posting alone never labels an invoice paid. It resolves a candidate transaction to the actual obligation ID through `GET /api/v1/finance/transactions/{id}` before sending an allocation. BigInt formatting and aggregation avoid losing pennies near the safe-integer bound. Saved GUID comparisons accept casing differences without weakening exact identity.
- Mutations use the existing `POST /api/v1/finance/agencies/{id}/receipts`, `/receipts/{id}/payer`, `/receipts/{id}/allocations`, `/allocations/{id}/reversals` and `/agencies/{id}/statements` contracts. The immutable command object retains body bytes, idempotency key and assignment ETag after a lost or uncertain response. A 409/412 conflict retains the typed invoice, amount and reason and exposes review of the current saved version before creating a new command. Denied reads clear stale finance details.
- `/accounting?tab=accounts&agencyId=...&statementId=...` shows saved agency and relationship receivables, provider payable, ledger source movements, statement list/detail, opening + debits − credits = closing, saved period and source cutoff, due dates and overdue flags. Statement download checks the exact selected version's returned ETag before offering bytes; the server checks authority again before returning them. Receipt cash is described as entering suspense once, with application changing debtor balance without another cash movement.
- Responsive panels use the existing back-office primitives. Tables remain horizontally scrollable on narrow screens; the selected agency, form controls and recovery actions remain reachable by keyboard.

## Verification

| Check | Result | Evidence |
| --- | --- | --- |
| Failing-first frontend test | Failed on the absent finance client before implementation | `node --test apps/backoffice/tests/finance-receipts.test.mjs` initial RED run |
| Frontend unit tests | 5 passed, 0 failed, 0 skipped | `node --test apps/backoffice/tests/finance-receipts.test.mjs` |
| TypeScript | Passed | `node_modules/.bin/tsc.cmd --noEmit --incremental false` from `apps/backoffice` |
| ESLint | Passed with zero warnings | `node_modules/.bin/eslint.cmd . --max-warnings 0` from `apps/backoffice` |
| Live Chrome collector | 10 passed, 0 page errors | `.local/phase10-05-browser/browser-report.json` |
| Visual evidence | Desktop receipt and 390px receipt/account screenshots | `.local/phase10-05-browser/receipts-desktop.png`, `receipts-mobile.png`, `accounts-mobile.png` |
| Staged diff | Passed, no tracked deletions | `git diff --cached --check`; `git diff --diff-filter=D --name-only HEAD~1 HEAD` |

The live collector used owned API port 5185 and Next port 3185; it did not touch the existing 3100 preview. It signed in as the local demo finance actor, recorded a receipt and verified the exact ID and £25.00 residual after reload, assigned its payer, induced a stale assignment ETag and retained a selected invoice and typed £10.00 amount. It then dropped a successful allocation response, retried the same body/key/ETag, proved only one saved application and a £15.00 residual, reversed it with a reason and saw one linked reversal and £25.00 residual after reload. It generated a saved statement, checked its equation and download, then checked 390px layout and keyboard access. Final saved browser IDs are in the report: receipt `00296b11-6f65-4e8a-83f2-7cd38221d650`, allocation `1a47ad12-7bcd-4c67-9129-52fd61673f98`, statement `8d471358-8275-41db-aa33-a3a29d29ac5a`.

The retained `CoverMGA_Demo` database was advanced with documented additive `--initialize-demo` only. `.local/phase10-05-browser/pre-db.txt` and `post-db.txt` show the same 235 agencies, 162 users, 41 policies, 72 journals and 72 obligations; migration history advanced from 95 to 100 without a reset. The demo clock and existing files/keys were preserved. Browser-generated fictional receipt, allocation, reversal and statement rows were retained as saved evidence. No bank or insurer provider was called.

## Review and deviations

**[Rule 1 - Saved response casing]** During the first live reversal journey, SQL saved the reversal but the client compared a lowercase response GUID with an uppercase saved selection and showed an uncertain result. Response IDs and reversal linkage now compare as case-insensitive GUIDs; a unit test and live browser journey pass. Commit `b0f3fcd`.

**[Rule 1 - Exact money]** Review found floating display and unchecked aggregate addition could round large pence values. Browser calculations and labels now use checked integer pence and BigInt aggregation; the large-value unit case passes. Commit `b0f3fcd`.

**[Rule 2 - Linked agency account]** The inherited agency header said its balance was unavailable, while the new accounting route supplies saved balances. The placeholder was removed and finance-capable agency staff get an exact agency account link; staff without finance access see an access-specific explanation. Commit `b0f3fcd`.

The live collector waits for the saved reversal row and account basis before asserting the corresponding UI state, because immediately adjacent independent GETs can overlap a still-running command. This made its evidence deterministic without changing the cash service. No unresolved HIGH or CRITICAL finding remains in this slice.

## Scope and handoff

This plan adds no schema, migration or server endpoint; those were supplied by 10-02, 10-03 and 10-04. It uses the already generated API contracts without changing their shape. The account workspace is scoped to one agency at a time; a finance user may enter a saved agency ID, and dual-role staff can follow the agency record link. The receipt list currently shows the first 50 saved rows and the count, while direct saved receipt URLs still open older records. Broader list pagination and cross-role agency discovery are follow-up UX work. FIN-01 and FIN-03 remain subject to the remaining Phase 10 plans and final verification; this summary does not mark them complete.

The owned preview processes on 5185/3185 are stopped after evidence collection. `next dev` generated untracked `apps/backoffice/AGENTS.md` and `CLAUDE.md`; they were read for Next guidance and were not staged. Inherited dirty `next-env.d.ts`, `tsconfig.json` and `.idea/` were not staged or reverted.

## Task commits

1. `b0f3fcd` — `feat(10-05): wire saved receipts and broker accounts` (implementation, frontend tests, live collector).
2. Summary commit recorded in the final executor message.

## Self-Check: PASSED

The six new source/test files and this summary exist; commit `b0f3fcd` is present. The final browser report states `passed: true` with ten named checks and no page errors. No tracked file was deleted in the implementation commit.
