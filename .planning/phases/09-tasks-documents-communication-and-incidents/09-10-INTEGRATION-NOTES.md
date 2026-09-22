# 09-10 delivery integration discovery

Prepared during09-09 final browser verification; not implementation or completion.

-09-09 exports ThreadService.Create/CreateDraft/UpdateDraft/ReadDraft/List/Messages,
  CommunicationScope.HoldThread/Audience/ValidateSelection and normalized draft
  associations. Hold original parent/identity and exact attachment scope before
  child draft/update locks. Thread audience/parent is immutable. Queuing must snapshot
  a draft under its ETag, not allow a provider to read editable current contents.
- Canonical schemas are OpsPackWrite (exact documentVersionIds, recipients, subject,
  body), OpsDelivery and OpsDeliveryAttempt in scripts/operations-contracts.mjs.
  Message DTO state vocabulary includes draft/queued/sent/failed/superseded; delivery
  uses queued/delivered/failed/superseded. Do not confuse sent state with draft save.
- Reuse SqlJobLeases and persistent DemoProviderOperation. ClaimCoreAsync has an
  explicit kind allowlist that must be extended for the new owner. OwnedAsync fences
  token, attempt and lease expiry. MarkTerminalAsync owns JobException deduplication;
  new delivery state/history must stay consistent with failure and retry.
- Existing QuoteDeliveryWorker and ServicingDeliveryWorker demonstrate deterministic
  accept/reject/transient-once/timeout-after-success results and pinned request hash,
  operation/scenario identity. Their underwriting-specific scope/current-cycle
  prerequisites are not suitable for general messages. Inspect their current helper
  signatures and preserve existing owner behavior when integrating a new job kind.
- New provider path must validate current sender, recipient address and exact file
  authority before the external effect and again before application. Provider outcome
  persistence remains honest if authority changes after the effect. No transport or
  real email is introduced. Lease loser cannot apply; retained result remains reusable.
- WorkflowTaskSources.Jobs.cs maps each work kind to actual owner rows. Add typed
  delivery→OperationalSubject ownership for terminal exception tasks; never trust
  arbitrary OutboxWork.SubjectRecordId as an agency. Keep rule/event deduplication.
- Read full09-10-PLAN,09-DATA-CONTRACTS,09-UI-SPEC,ADAPTERS and current permissions/
  API contracts before implementation. Need meaningful RED/GREEN and isolated SQL
  provider/crash/retry/scope/pack-history tests plus browser failed→retry→delivered.
  Current communication browser is13checks per product and covers drafts only.
