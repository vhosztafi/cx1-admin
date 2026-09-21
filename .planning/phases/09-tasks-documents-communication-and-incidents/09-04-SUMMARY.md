---
phase: 09-tasks-documents-communication-and-incidents
plan: '04'
status: complete
completed: 2026-09-21
requirements_completed: [OPS-02]
---

# 09-04 — Published workflow tasks

Published, closed SettingVersion rules now generate real scoped tasks from quote/servicing referrals and information-request decisions, matching requests, issued-term renewal reminders, agency follow-ups and owned job exceptions. WorkflowTaskBinding retains immutable typed provenance, original source/rule JSON and SHA-256 hashes. Migration20260921151327_WorkflowTaskBindings enforces unique logical rule/source identities, typed source FKs, task/subject ownership and append-only/downgrade guards. Storage increment097cbbf and pure-rule increment2c23835 preceded the runtime increment.

`WorkflowTaskService.Reconcile(Guid ruleVersionId,string sourceKind,Guid sourceEventId,CancellationToken)` validates the latest effective published definition, reauthorizes the actual originating internal actor/parent and serializes creation by logical rule/source key. Task, required checklist, initial event, binding and audit commit together. New wording does not duplicate old work; a new source event does. Current source recovery, replacement evidence and later rule publication never complete/reopen human tasks. Missing/revoked authority, foreign/unowned sources, future/stale/draft definitions and ambiguous legacy match ordering fail closed.

`WorkflowTaskScanner.Scan` processes bounded64-event batches per rule/source kind, returning typed review issues and revisiting offsets. `WorkflowTaskDispatcher` integrates with the API hosted services through explicit Development opt-in `Cover:WorkflowTaskWorkerEnabled`; defaultoff follows the existing renewal-worker pattern. Missing-only fictional rule/checklist seeds are wired into demo initialization; existing scopes and source records are preserved. The hosted browser test proves the dispatcher creates an actual task without direct materializer calls. The retained demo upgrade and complete operational story remain09-16/18, with original bytes, keys and policies unchanged so far.

Task views add optional OpsWorkflowProvenance (first rule identity/version/title, source identity, current condition and change flag). The existing task UI renders the rule and separate outstanding/resolved/not-due/unavailable source state. Current-source reads never write the task ETag/history. Owned saved-record links and source-specific checklist definitions remain real task data; completing them does not make an insurance decision.

## Verification

- Strict.local/phase9-04-final-strict:41 unique passing cases,33units (16workflow+17task) and8realSQL scenarios; no skips or duplicate cases. Sources: final-unit, final-families and hosted-browser. Initial gate expectation53 incorrectly assumed27task-only units; inventory confirmed17task+16workflow and the final41 includes exactly8SQL cases. No test result was dropped to hide a failure.
- Seven final workflowSQL cases cover all8typed adapters/five families, concurrent creation/restart/revision/new event behavior, task/checklist/history atomicity, actor revocation, unknown-job rejection, actual quote/servicing query decisions, issued-term due windows, matching parent/timestamp ambiguity, original agency activation and PI evidence replacement, preserved evidence bytes, missing-only seeds and invalid-latest scanner behavior. Final report.local/phase9-04-final-families.
- Existing task command/API regressions also passed in.local/phase9-04-family-green (7total cases including2manual-task regressions; overlaps are not added to final strict totals).
- Actual hosted dispatcher plus18browserchecks and independent SQL readback passed.local/phase9-04-hosted-browser. Evidence.local/browser-evidence/tasks/CoverMGA_Test_11f85889c3aa4819873d2f70a5090e8d. Earlier direct-materializer browser run also passed; it is superseded, not counted twice. Desktop/390px workflow panel inspected; current source-hash validator passes.
- Root411, frontend177, API/operations54checks pass. OpenAPIvalid with87existingwarnings; generated schemas/types agree. TypeScript and focused ESLint pass. Isolated Next productionbuild.local/phase9-04-browser-build.log passes; retained preview untouched.
- Actual RED for missing immutable guard recordedstorage-red-r3; earlier fixture corrections were not business-proof RED. Materializer missing-class RED recordedmaterialize-red. Fixture failures (notification ownership, future draft timestamp, required query condition, matching-rule scope) were fixed without weakening production guards.

## Review and boundaries

Inline security/diff review found no unresolved HIGH/CRITICAL issue for this slice. Matching history lacks a sequence/current-decision pointer; tied latest timestamps produce an explicit review issue instead of arbitrary SQL ordering. Job sources resolve through the actual typed owning request, never arbitrary OutboxWork.SubjectRecordId or payload GUID. Current identity is required before duplicate task disclosure. Scanner issues are logged with stable rule/source identifiers and codes, without payloads or credentials.

OPS-02 is complete. OPS-01/source-specific final visual reconciliation remains09-17; document attachments remain09-08 and other operational producers remain their later plans. No human business/assistive-technology UAT, hostedCI, Docker runtime, retained Phase9 demo upgrade or phase-wide completion is claimed. No sales-funnel edits, new privilege grants, real delivery/payment or deployment occurred. Next:09-05 durable files and recovery.
