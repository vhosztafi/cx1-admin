#09-15 preparation after MID source audit

Execute only after09-14 closes. Original CancellationConsequence holds policy/term/transaction/
version/decision/work/payload/hash. Existing CancellationNoticeWorker Deliver writes a legacy
CancellationNoticeReceipt and Apply finishes cancellation-notice work; preserve existing bytes,
IDs and receipt state, no automatic resend. Its current Validate checks owned issued decision
and original cancellation setting but needs15 current-authority integration.

DocumentService.RegisterCancellationNotice already bridges an exact notice consequence to
one DocumentVersion (unique CancellationConsequenceId), physical generation work and original
policy version. It intentionally generates an internal PDF separately from legacy delivery.
Do not create a second notice document/version just to implement15. Inspect audience promotion
and immutable delivery source rules before connecting new notices to actual document delivery.

No runtime DocumentWithdrawal/WithdrawnEffectiveAt storage found; API contract field exists.
Add append-only withdrawal/receipt rather than delete historical document bytes. Task closure
must use exact eligible source/term and append events, not broad policy-subject task deletion.
WorkflowTaskBinding has immutable sourcekind/sourceevent/PolicyTermId/TaskId; OperationalTask
has state/type/eventsequence/completionreason. Reconcile(existing binding) returns its task
without closing it; use explicit eligible closure with state/rowversion/event guards.

09-14 owns cancellation-mid-removal operation. MidSubmissionRegistration preserves original
CancellationConsequence WorkId/payload/scenario, pins separate provider setting and delays
NextAttemptAt until effectiveAt. Delegate to it; do not enqueue a new MID operation in15.
Commercial cancellation emits no MID and EL-specific withdrawal only if cover was present.
