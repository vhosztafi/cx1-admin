---
phase: 06-underwriting-and-first-policy-issue
plan: '09'
status: complete
completed: 2026-09-17
implementation_commit: 9d68ee6
requirements_completed: []
---

# 06-09 — Quotation and acceptance workspace

Implemented in `9d68ee6`. Quote Overview and Underwriting actions open the
Quotation workspace, with current/historical structured terms, cover and premiums,
endorsements, retained proposal/rating/settlement, exact-version proof, scoped
recipients, real delivery attempts and named acceptance. Existing prototype
panels, colors, fields and314px action rail are reused. Quotation documents are
truthfully described as structured information; generic PDF generation remains
Phase9. Policy issue remains unavailable until06-11..12.

## Behavior and recovery

- Preparation retains exact server cycle/rating/template IDs. Signing proof uses
  actual uploaded bytes, owned file associations and independent underwriting
  review. Attachment keys and applicability include terms/submission identities,
  preventing an old proof draft from following a new version.
- Recipient selection uses current same-client/relationship contacts. The UI
  distinguishes queued, completed, failed and superseded demo delivery. Original
  job recovery has bounded server eligibility and verifies exact delivery kind.
- Acceptance requires a named person, explicit offset/time, closed channel and
  actual reviewed proof matching current terms/fingerprint/cycle. Current and
  historical acceptance remain visibly separate. Withdrawal invalidates current
  acceptance without deleting prior facts; fresh proof permits fresh acceptance.
- Shared confirmation retains exact request body/key/ETag after uncertain replies,
  traps focus/Escape while unresolved, checks current account before dispatch and
  retains inputs on stale denial with explicit current-state readback.
- Parent refresh remounts dependent resources even when the quote ETag is stable.
  Histories have independent protected paging. Reads use visible recovery;
  mutations are never automatically retried with new identities.
- Added focused quotation helpers rather than enlarging generic commands; strict
  send receipt validation rejects missing job identities and nonqueued outcomes.
  Six new unit cases first exposed the missing queued-receipt validation, then pass.

## Verification

-100frontend unit tests, lint, typecheck and productionbuild pass. Logs:
  `.local/phase6-09-web-final.log`, lint-final, typecheck-final, build-final.
-317contract/source cases pass `.local/phase6-09-contracts-final.log`.
  Diff check passes; frontend-code unchanged. No backend runtime change in this
  slice:06-08's799backend/144realSQL/no-skips gate remains the backend baseline.
- Actual Chrome both-product prepare/sign/send/accept, same terms after signature,
  lost committed send exact retry, Escape/focus containment, persisted reload,
  proof withdrawal/fresh acceptance and314px/390px containment passed:
  `.local/phase6-09-browser.log`. Script `verify-underwriting-terms-browser.mjs`.
- Actual negative journey passed with `--negative`, log
  `.local/phase6-09-browser-negative.log`: retained queued/transient and rejected
  delivery; ended selected contact, switched user and superseded terms create no
  extra delivery. Original server scenario restored by appended identical values;
  historical settings and pinned requests remain retained.
- Final `--readback` passed `.local/phase6-09-browser-readback.log`. Desktop and
  mobile viewport screenshots inspected in
  `.local/browser-evidence/underwriting-terms`; no horizontal overflow. Browser
  exposed missing accessible select labels/form wrapper, corrected before final
  build. One earlier lost-response run timed out on a following read; SQL retained
  exactly one successful delivery. Visible read recovery is covered in the harness;
  the final both-product and negative runs passed without that failure.
- Preserved examples: CombinedQT-MT-0000000289 quote
  `360d641b-a8ad-4eeb-bf4f-1e428730cf73`, terms
  `53e2d90a-472e-4b48-a278-2ef03f34a39a`, current acceptance
  `ccd14663-f3cb-42cd-934f-3b26d678059c`; RoadRisksQT-MT-0000000290 quote
  `5cd5a9c0-451f-4d91-a746-542afb73c47b`, terms
  `7b525a56-1b09-4e7a-9818-03ff161197e7`, current acceptance
  `dc278408-9ab0-49cc-8c7d-53a4fc1c58e4`. Each retains two acceptance records and
  actual reviewed fictional files. Reports include delivery/contact/proof IDs.
  Negative quote `bba128e4-2751-4036-8354-69274e45049f` remains an explicit draft
  with retained failed/delivered history.
- Only identified owned preview PIDs38212/52200 were stopped after verification.
  No real email, provider, payment, deployment, database reset, Docker, hostedCI or
  human/assistive-technology UAT is claimed.

## Next

Execute06-10 policy storage and balanced first-issue posting. Compound requirement
completion and the conditional capacity source supplement remain06-14 gates.
