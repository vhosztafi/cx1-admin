# 09-14 preparation from claims integration

Preparation only while09-13 final CC browser acceptance runs. Execute14 after13 closes.

Observed producers (read existing code before selecting implementation):
- PolicyMidIntent is in Persistence/ServicingIssueRecords.cs and configured by
  ServicingIssueModel.cs. Composite version/transaction/term/policy FK, unique
  VersionId+Purpose and WorkId. Purpose currently only adjustment/renewal.
- ServicingIssueWriter creates one intent per issued motor version. Payload format
  policy-mid-intent-1 pins intent/policy/term/version/contentHash/action=change/
  effectiveAt/endsAt. Work kind mid-update and key mid-update/{intentId:N}.
  It does not contain per-vehicle delta; derive only from pinned owned history,
  never mutable current risk. Commercial is explicitly skipped.
- PolicyIssueWriter currently creates documents but NO initial MID intent.14 must
  add initial issue ownership and explicitly consider retained initial versions.
  Do not assume an existing issue MID row or manufacture a second existing operation.
- CancellationIssueWriter creates CancellationConsequence(kind=mid-removal), immutable
  cancellation-consequence-1 with policy/term/transaction/version/hash/effectiveAt and
  work cancellation-mid-removal. It uses the cancellation scenario setting (not a MID
  setting) and has no PolicyMidIntent. Preserve that original association and receipt;
  do not rewrite retained envelopes. Commercial cancellation omits MID.
- Canonical list /versions/{versionId}/mid-submissions and retry
  /mid-submissions/{submissionId}/retry exist contract-only. OpsMidSubmission expects
  submission/intent/version/job/state/items(kind/riskItemId/registration/action/effectiveAt)
  and safe provider reasons/reference. Discover exact normalized source shape and
  required extensions for new-business/cancellation before coding.

Claims introduced reusable patterns, not a generic claims/MID abstraction requirement:
ClaimsHandoffWorker separates provider effect and application, validates work/source/
scenario/hash, fences leases and quarantines changed duplicates. ClaimsSummaryService
shows scoped job reads/manual retry. SqlJobLeases now supports operational-claims;
WorkflowTaskSources.Jobs maps that work through ClaimsRequest→Handoff→original policy.
A revoked original actor still gets an exception task owned by an authorized internal
colleague, with original actor retained in source snapshot. Add MID mapping explicitly.
No real provider transport, settlements, payments or retained demo migrations authorised
in this slice. Keep native SQL acceptance sequential and preserve original keys.
