---
phase: 06-underwriting-and-first-policy-issue
plan: '12'
status: complete
completed: 2026-09-17
implementation_commit: 82aa340
requirements_completed: []
---

# 06-12 — Issue confirmation and persistent policy record

Implemented in `82aa340`. Current server capability enables issue from the
accepted quotation. Confirmation names client, agency, cover start/term, accepted
terms and exact opening amount due. Penny-safe presentation distinguishes net
agency remittance, separate remuneration and direct collection. The frozen
command keeps its body/key/ETag through uncertain retry and checks the current
account before dispatch. Successful issue navigates to the real returned policy.

Policy header, overview, risk, cover/endorsements, drivers, vehicles, transaction,
opening obligation/journal and document requests read the persisted immutable
version. Future inception is labelled honestly. Documents say generation requested;
no money is shown as collected. Servicing controls remain explicitly unavailable.
Source quote/client links and the bound quote receipt survive refresh.

## Verification

- **104 frontend tests** pass in `.local/phase6-12-web-final-2.log`; lint passes in
  `phase6-12-lint-final-2.log`, typecheck in `phase6-12-typecheck-final-2.log`, and
  production build in `phase6-12-build-final.log`, API origin5087. Meaningful initial
  RED evidence: `phase6-12-red.log`. ES2017 typechecking caught BigInt literal syntax;
  constructor notation preserves exact arithmetic without changing the target.
- Actual Chrome both-product journey passes in `.local/phase6-12-browser-2.log`.
  Saved receipt/report and inspected desktop/390px screenshots:
  `.local/browser-evidence/underwriting-issue/`. First run stopped on an ambiguous
  duplicate quote-link locator after successful issue; retained receipt enabled
  readback/resume without issuing that quote again.
- Both products verify current servicing denial, account-switch rejection,
  freshly superseded acceptance with retained form/current-state recovery,
  double activation causing one dispatch, lost committed response and exact
  retry, uncertain Escape prevention and focus wrap. Both actual receipts agree
  with policy/term/version/transaction/obligation IDs, balanced journals and three
  requested documents. Tabs, quote readback,314px rail and390px containment pass.
- Demo policies: Combined `PL-MT-0000000001` /35db03d6-abff-4b5d-87ad-7b5fdbb9a2a3,
  Road Risks `PL-MT-0000000002` /ab9da1df-ae05-4340-be78-9af1d3154a51. Opening due
  £1106.10 and£628.50 respectively; no cash receipt.
- No backend change in this plan: retain fresh06-11 baseline834backend/165realSQL,
  zero skips. Rating expiry rejection is verified by those real SQL/API service
  scenarios; browser expiry presentation remains part of06-14 final acceptance.

## Remaining boundaries

Policy discovery/client lists/safe own-agency sharing remain06-13. Conditional
capacity-source supplement, restart and retained journey regression remain06-14.
No compound requirement is completed here. No sales-funnel edit, external message,
payment, reset, portal or hosted deployment. Human UAT, hosted CI and Docker remain
unperformed. Owned preview processes are retained for the immediately following
plan; IDs are recorded in `.local/phase6-12-preview-pids.json`.
