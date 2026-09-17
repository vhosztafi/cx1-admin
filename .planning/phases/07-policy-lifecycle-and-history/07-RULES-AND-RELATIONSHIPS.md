# Phase 7 implementation rules and relational contract

2026-09-17. Binding refinement of07-DATA-API-DESIGN for plans01..16.
All numeric underwriting/notice rules below are fictional demo configuration,
not legal requirements. Preserve current scope and permission matrix.

## Cancellation rule version demo-servicing-1

| Reason code | Minimum local notice days | Required evidence purpose | Required cancellation decision |
| --- | ---: | --- | --- |
| insured-request | 0 | cancellation-request | Current underwriter grant permitting cancellation |
| non-payment | 7 | cancellation-notice | Senior cancellation grant, separate from requester |
| non-disclosure | 7 | cancellation-notice and cancellation-reason | Senior cancellation grant, separate from requester |
| trade-ceased | 0 | cancellation-request | Current underwriter cancellation grant |
| insurer-instruction | 0 | insurer-instruction | Senior cancellation grant, separate from requester |

All evidence requires current accepted review. Record noticeDeliveredAt and exact
evidence association; minimum effective local date equals delivered local date
plus configured days. Do not treat queued delivery as delivered notice. Requested
effective instant must be inside the existing term, at or after latest issued
slice and within current permitted backdating authority. Certificate-affecting
driver changes are not part of cancellation. Require reason text for every issue
and approval; 10..2000 trimmed characters. Approval is explicit for every reason;
the first/last table distinctions specify who may decide and separation of duties.
No generic system-admin bypass. Cancellation issue still requires delegated
issue capability and all current conditions, even for the preparer.

Any already-issued later renewal term blocks cancellation of its predecessor
with code later-term-issued. Unsupported overlapping/future-slice operations
return a supported-date explanation; no silent cascade or rebase. Policy cover
outcome is scheduled until cancellation EffectiveAt. Refund approval/payment is
separate Finance authority in Phase10 and never granted by this decision.

## Renewal rule version demo-servicing-1

Invitation due:45 local calendar days before old exclusive end. Auto-lapse:
14 local calendar days after old exclusive end, only when renewal remains
unaccepted/unissued. Manual lapse requires reason and notification; it never
shortens expiring cover. Exact event operation identity is termId/lapse and
survives receipt expiry. Acceptance cannot bypass unsupported late issue.

Experience fields: observationStartsOn, observationEndsOn (exclusive),
claimCount integer0..100000, paid/outstanding nonnegative GBP decimal strings,
earnedPremium nonnegative GBP, sourceCode insured/agency/administrator,
sourceReference1..200, recordedAt/By server-owned, evidenceAssociationId.
Dates must be ordered and end no later than the configured current local date.
Unknown is represented by absence, not zero. Missing accepted evidence, missing
experience or earnedPremium=0 raises UW-31-information and blocks invitation/
issue until information is supplied and re-rated; it is not approvable as0%.
With positive denominator, loss ratio=(paid+outstanding)/earnedPremium. Compare
the exact decimal ratio to0.50 before display rounding. Above0.50 raises UW-31
requiring current senior grant. Fictional loading is8% on the calculated annual
premium, applied by the pinned rule, not entered by the decision actor. A decision
approves that exact calculated proposal or requests information/declines; it
cannot enter arbitrary money. At/below0.50 adds no experience loading.

FairValueAssessmentVersion is an immutable published assessment with Id,
ProductVersionId, BinderVersionId, validFrom/validTo UTC, outcome pass/refer/fail,
evidenceFileVersionId, approvedBy, approvedAt, reason. Only pass with accepted
evidence and coverage of renewal inception satisfies the check. Seed a labelled
fictional assessment plus actual persistent demo evidence through existing file
storage; never fabricate a February date in the UI. Missing/refer/fail blocks
invitation until a supported valid version is selected by rerating. Pin its Id
in the cycle. Broker eligibility uses current active agency/relationship/product
and commercial terms. Accounting arrears remain unknown/unavailable untilPhase10;
do not display “Account current” from agency activation alone.

## Exact provenance alternatives

Keep PolicyTransaction.SourceQuoteId as original policy lineage. Make the four
quote decision columns nullable: CycleId, QuoteRevisionId, RatingId, AcceptanceId.
Add nullable ServicingIssueDecisionId. SQL enforces:

| Kind | Quote decision columns | ServicingIssueDecisionId |
| --- | --- | --- |
| new-business | All four nonnull, existing compound FKs retained | Null |
| adjustment/renewal/cancellation | All four null | Nonnull |

ServicingIssueDecision is append-only and created inside issue transaction before
PolicyTransaction. Fields: Id, DraftId, PolicyId, BaseTermId, BaseVersionId,
RevisionId, Kind, CycleId?, RatingId?, TermsId?, AcceptanceId?,
CancellationPreviewId?, CancellationApprovalId?, EffectiveAt, InputHash,
CreatedAt/By. Adjustment/renewal require all cycle/rating/terms/acceptance fields
nonnull and both cancellation fields null. Cancellation requires preview and
approval nonnull, all cycle/rating/terms/acceptance fields null. No dummy GUIDs.
Draft kind must equal decision kind. PolicyTransaction references
(ServicingIssueDecisionId,PolicyId,Kind) to decision(Id,PolicyId,Kind).
Existing new-business provenance stays unchanged. Issue time trigger verifies
the new target term: same BaseTermId for adjustment/cancellation; renewal's new
term starts at base exclusive end, is nonoverlapping and matches accepted terms.

Required unique alternate keys and references:

- Draft(Id,PolicyId,BaseTermId,BaseVersionId); base version references existing
  PolicyVersion(Id,PolicyId) plus same-term verification through compound key.
- Revision(Id,DraftId); draft currentRevision references that pair.
- Cycle(Id,DraftId,RevisionId), referencing Revision(Id,DraftId).
- Rating(Id,CycleId); Terms(Id,CycleId,RatingId); Delivery(Id,TermsId,CycleId);
  Acceptance(Id,TermsId,CycleId,RatingId). Each child references the full available
  parent ownership key; current pointers use the same keys.
- EvidenceAssociation(Id,CycleId,RevisionId) references exact cycle/revision;
  association also owns immutable FileVersionId, purposeCode, RiskItemId? and
  visibility. Review references exact association and cycle. Withdrawal appends
  event and removes current eligibility, never changes immutable file bytes.
- Referral(Id,CycleId,RatingId); Decision references exact referral/rating/cycle.
- CapacityCase(Id,CycleId,RatingId), Submission(Id,CaseId,CycleId),
  Response(Id,SubmissionId,CaseId,CycleId). Current response is constrained to
  latest eligible submission. Conditions reference exact response/cycle;
  resolution references condition plus same-cycle evidence association.
- CancellationPreview(Id,DraftId,RevisionId) owns base/ledger/config hash and
  immutable movement JSON. Approval(Id,PreviewId,DraftId,RevisionId) references
  it and actor/current grant. IssueDecision references that exact approval set.

No polymorphic unconstrained subjectId substitutes for these references.
Evidence riskItemId must exist in exact revision and match purpose target kind;
enforce through validated stable-item projection/guard, not GUID syntax alone.
File access and current visibility are independently checked. Every immutable
record has append-only guard. Mutable current pointers never confer authority
when parent cycle/draft is superseded, withdrawn, abandoned or issued.

## Insert order and concurrency

Use the existing identity/agency/relationship scope lock order; extend it with
policy, base term, draft, lease, revision/cycle, referral/capacity records ordered
by ID, then accounting period and new issue records. Every command touching more
than one draft locks drafts by ID. Date workers and cancellation follow the same
order. No database lock survives the command transaction into user editing time.
Reauthorize before receipt replay, then evaluate fresh-command ETag and lease.

Issue insert order: issue decision; renewal target term if applicable; policy
transaction; ordered versions and registrations; obligation/components; draft
journal/lines; seal journal; requests/outbox; audit/activity; draft issued pointer
and receipt. All commit together. Cancellation locks/abandons conflicting drafts
before this write sequence. No notification is sent inside SQL transaction;
durable work is inserted there and processed after commit with deduplication.

## Bounded command additions

All new routes under existing /api/v1/drafts/{draftId}, with closed DTOs. Retain
existing operationIds; add these suffixes in07-01 rather than copy quote paths:

| Suffix | Command/body |
| --- | --- |
| evidence/uploads | fileName1..200, contentType allowlist, bytes through current bounded file-upload transport; no path from client |
| evidence | fileVersionId, cycleId, purposeCode1..60, riskItemId optional only for policy-level purpose |
| evidence/{id}/review | cycleId, outcome accepted/rejected, reason10..2000 |
| evidence/{id}/withdraw | reason10..2000 |
| referrals/{id}/decisions | cycleId, ratingId, outcome approve/conditional/query/decline/reopen, reason10..2000, conditions max20 closed typed objects |
| referrals/decisions | selected max50 unique IDs and their ETags plus same-cycle decision fields; atomic all-or-none |
| capacity | cycleId, referralId, requested dimension/target; provider derived from pinned binder |
| capacity/{id}/submissions | reason10..2000, message1..10000, evidence IDs max20 |
| capacity/{id}/responses | exact submissionId, outcome, message1..10000, reviewed evidence IDs max20, conditions max20; receivedAt validated against clock |
| capacity/{id}/actions | action withdraw/reopen/assign, reason10..2000, assignedUserId only for assign |
| capacity/{id}/conditions/{conditionId}/resolve | exact responseId, evidenceAssociationIds max20, reason10..2000 |
| terms | exact current cycle/rating IDs; no client-supplied calculated premium or endorsements |
| terms/{id}/delivery | recipientContactId, proofAssociationIds max20; scoped recipient address resolved server-side |
| acceptances | termsId, deliveryId, accepter1..200, receivedAt, channel enum email/telephone/written, evidenceAssociationId |
| cancellation-preview | GET computes exact preview without writes; POST with previewHash under draft ETag/lease rechecks and persists immutable reviewed preview, returning previewId; mismatch409 requires review |
| cancellation-approvals | previewId, previewHash lowercase64hex, reason10..2000; actor/grant server-owned |
| experience | PUT full typed experience fields above under draft ETag/lease; creates revision |

Proposal maximum100 changes,1000 risk items total and2MiB JSON; reason/requester
strings bounded2000/200. Incomplete capture is allowed but type/size/ownership is
always enforced. Current product validation controls lower domain limits. Reuse
existing file-upload limits rather than create an unlimited base64 endpoint.
Every mutation requires current capability, CSRF and operation key; child/parent
ETags and lease apply as documented in generated operation contracts.

## Planning resolution

Plans01,05..14 must consume this file and encode these choices as tests/contracts.
Migration-generated key names may follow repository conventions but must preserve
the listed semantic keys and alternatives. Source inventory/validation documents
remain the coverage authority; this refinement does not claim implementation.
