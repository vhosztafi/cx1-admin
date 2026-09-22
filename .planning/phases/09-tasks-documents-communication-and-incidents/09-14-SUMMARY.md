---
phase: 09-tasks-documents-communication-and-incidents
plan: '14'
status: complete
completed: 2026-09-22
requirements-completed: []
---

# Motor Trade MID submissions and recovery

Initial issue, adjustments, renewal intents and cancellation removals now feed exact,
persistent demo MID submissions. The policy Vehicles tab and vehicle version history show
actions, effective dates, accepted/rejected/pending outcomes, safe reasons, attempts and the
real exception task. Unknown retry responses preserve actor/body/key/ETag and block navigation.
Commercial Combined cannot enter MID. No real regulator/provider connection occurs.

## Implementation

Backend bf66d55; UI/browser97620d8. MidSubmissionRegistration.Register consumes existing
PolicyMidIntent or CancellationConsequence WorkId/payload without replacing an operation.
Initial issue now writes a new-business intent atomically. RegisterMissingInitial is an
explicit missing-only internal demo bridge; reads never create reporting work. MidSnapshots
pins original source/base hashes and uses previous slice or issue-decision BaseVersionId.
Vehicle report-mid declarations and covered trade plates determine inclusion. A registration
replacement removes old/adds new, changed vehicle/cover emits change, removed reporting emits
remove, and unchanged adjustments are not-required. Renewal updates retained registrations.
Cancellation keeps its original scenario/work, pins a separate MID provider scenario and waits
until effectiveAt. Later policy versions cannot receive an earlier result.

MidSubmission/MidResult are immutable, with unique intent/work/version association and exact
provider provenance. Migration20260922135419_OperationalMid, Guards partial and EF snapshot
protect source, work identity and downgrade. MidSubmissionWorker.ExecuteProvider/Apply stores
one deterministic provider operation separately, then fences current original authority and
lease at application. Identical events deduplicate; changed events quarantine. Post-effect
revocation retains the effect without applying a local result. Manual retry expands the same
transient operation budget. Definitive rejection remains visible and cannot be retried as an
uncertain transport failure. One JobException maps to an actual policy workflow task.

MidEndpoints list/retry and generic job read enforce scope, closed inputs, CSRF, signed paging,
ETag and idempotency before receipt disclosure. Program/MidDispatcher are wired; development
worker flag Cover:OperationalMidWorkerEnabled defaults true. Original producer and registered
queue envelopes remain retained. OPERATIONS-CONTRACTS documents signatures and DTOs; generated
operations/adapters preserve legacy envelopes alongside mid-submission-2.

## Accepted evidence

.local/phase9-14-accepted-strict:1306 unique passing cases/10 real SQL/no skips. Inputs:
units-all1296, sql-final8, final-browser-rejection2. Do not add earlier diagnostic runs.
The20 MID unit cases cover both actual motor product codes, declarations, covered plates,
registration replacement, change/remove/renewal/no-change, invalid source/product/basis and
intervals. Initial rule assertions were observed RED, then GREEN; frontend2 also RED→GREEN.

SQL proves new issue intent, transactional rollback, existing initial bridge/replay, scoped
API/CSRF, actual two-slice adjustments for both products, original/previous-slice hashes,
old result applied after later issue without changing later source, cancellation before/at
boundary and original work/scenario, commercial exclusion, provider timeout/recovery, stale
lease, duplicate/quarantine, immutable storage/downgrade and revoked execute/late apply/read.
Definitive rejection records safe reasons/result and one exception without transport retry.

Final browser manifest.local/phase9-14-browser/motor-trade.json passes source collector9checks
plus SQL readback. Evidence root CoverMGA_Test_ceeda57823df42ac83d1024842935081. It exhausts six
real attempts, opens the persisted exception task, loses a committed retry response, preserves
the command, then records one result on attempt7. Exact items/hash/job/submission and one task
survive. Vehicle historical view and page reload show the same result. Desktop/mobile images
inspected; the item table scrolls within the mobile viewport. Preliminary browser evidence is
superseded. Root420/frontend200; build/typecheck/lint pass; OpenAPI0errors/104warnings.
All acceptance processes ended; no unchanged suite needs restarting.

## Boundaries and next work

OPS-08 remains compound through source reconciliation09-17 and retained demo09-18. Renewal
transformation has unit coverage and consumes its existing issue intent; full retained renewal
journey remains aggregate acceptance. No retained demo migration/bridge, real MID certification,
human business/assistive UAT or production deployment is claimed. frontend-code and retained
keys remain untouched. Continue09-15 effective cancellation operations; reuse this exact MID
operation and preserve existing cancellation notice receipts. See09-15-INTEGRATION-NOTES.
