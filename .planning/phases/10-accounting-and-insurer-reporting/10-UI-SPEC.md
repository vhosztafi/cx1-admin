---
phase: 10
slug: accounting-and-insurer-reporting
status: approved
shadcn_initialized: false
preset: none
created: 2026-09-24
---

# Phase 10 UI design contract

Derived from the decoded `pAccounting()` source, Phase 9 handoff, current shell/components and finance requirements. Design approval does not imply an implemented or visually verified page.

## System and visual hierarchy

Reuse the existing Next.js shell, IBM Plex Sans, Panel/DataTable/Status/SectionTabs, white panels on #f6f7f9, #14161c text, #3f5ef5 primary accent and existing 238px sidebar/62px topbar/314px rail. Keep existing 4/8/12/16/24/32px spacing, 13px body/table and tabular right-aligned money. Focus on selected period, agency/provider and the balance equation. Never replace real balances with prototype figures. Negative balances visibly say Credit balance; an empty state distinguishes no movements from zero balance.

## Screens and actions

| Surface | Contract |
| --- | --- |
| Overview | Persisted period selector and scoped KPIs: written premium/tax/fees/commission/net insurer payable, cash received, closing/overdue balances, earned premium with pro-rata monthly basis, ageing and linked attention rows. Show metric basis and as-of cutoff. Export period downloads an exact snapshot. Post journal opens an authorized reasoned correction flow; it is never a toast-only generic journal. |
| Transactions | Filtered posted insurance/cash/correction entries with reference, policy/agency, type, effective date, posted date, signed components, due/status and source drill-through. Paid/allocated/write-off labels require saved finance evidence; no fabricated statuses. |
| Broker accounts | Agency balances, pinned terms, due/overdue/last receipt and selected agency statement. Statement table shows opening, each dated debit/credit/running balance and closing; download exact generated version. Selected agency deep link retains scope. |
| Payments | Receipt table and View detail, payer identity assignment, allocated/residual amounts, invoice candidate selection, amount entry and reasoned unallocation. Preserve receipt/invoice selection and typed amount after a conflict. Unidentified receipts cannot allocate. |
| Reconciliation | Bank import/list/detail, real ledger candidate matches, split/partial match, unmatch, reasoned duplicate exclusion and explanation. Banner shows actual unresolved variance; explaining does not silently zero it. |
| Bordereaux | Provider/period batches and prior submitted downloads. Detail shows exact version, source count, totals, each failing row/field and Correct/Exclude actions including link-styled source cells. Revalidate after a change. Valid version alone enables Submit. CSV download has version/hash and submitted history. |
| Refunds | Detail and audit for request, entitlement, requester, independent approvals, reserved/queued, provider acknowledged/application pending, paid/failed/rejected. Limit rule and second approval requirement are shown from saved version. Never label queued as paid. |
| Period close | Show period range/state, prerequisites, actual unexplained exceptions and reasoned close action. Historical closed journals remain inspectable. Later correction opens in an eligible open period and links original. |
| Policy finance | Linked policy transactions, obligations, allocations, credit/refund outcome and accounting dates under current policy scope; support compound POL-01 without exposing other agency records. |

The Agency Open in Accounting link carries its agency ID. The Admin Open bordereaux link has a real Phase 10 destination, while its source control remains Phase 11 owned. Finance-category reporting links must retain applicable filters. Every original control/displays gets a separate source-ledger binding and saved-record browser check.

## Copy and state

Actions use explicit labels: Record receipt, Assign payer, Allocate amount, Reverse allocation, Import bank lines, Match line, Explain variance, Exclude duplicate, Request refund, Approve refund, Reject refund, Queue demo payment, Generate statement, Generate batch, Correct mapping, Exclude row, Re-run validation, Download CSV, Submit demo batch, Close period, Post correction. Error copy names the failed precondition and keeps user input. Changed record/period returns a review conflict. Uncertain adapter outcomes say “Awaiting confirmation”; demo results say “Demo paid” or “Demo submitted” only after their separate local application is saved. Downloads and statements state exact period/version.

## Accessibility, responsive and recovery

At 390px stack filters and panels; financial tables may horizontally scroll in a labelled focusable region with visible context, but the page itself must not overflow. At 200% zoom commands and error text remain reachable. Forms use persistent labels, error summary and field links, dialogs have focus trap/return, and state changes use text/live regions. Do not use color alone for credit, overdue, exception or approval status. Money is submitted as canonical decimal text and calculated in integer minor units client-side. Preserve selected IDs, ETag, command key and unsaved input across uncertain responses; replay the same intent and reload before a changed request. On permission loss clear inaccessible detail and disable download; a cached statement never bypasses current authority. No sensitive finance payload in localStorage.

## Checker result

Copywriting PASS; visuals PASS against source tabs and inherited links; color PASS; typography PASS; spacing PASS; registry safety PASS (existing components, no new UI package). Runtime browser and human UAT remain separate evidence.
