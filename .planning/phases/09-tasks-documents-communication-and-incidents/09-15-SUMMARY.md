---
phase: 09-tasks-documents-communication-and-incidents
plan: '15'
status: complete
completed: 2026-09-22
requirements-completed: []
---

# Effective cancellation operations

Original cancellation work now delivers its exact notice through the persistent demo adapter, withdraws applicable certificates at the effective instant, and cancels eligible bound renewal tasks. Commercial Combined creates no MID and withdraws only covered EL certificates. Original payloads, legacy receipts and PDF bytes remain intact. MID continues through09-14.

## Implementation

CancellationOperationsAuthority holds current original actor and typed policy scope, validates issued version/decision/original envelope, then workers fence owned leases. Task heads lock before the original work. CancellationOperationsWorker.Apply appends certificate/task effects and receipt atomically; future effects cannot be claimed/applied. CloseCancelledTerm retains checklists and excludes manual, unrelated, terminal and source-changed tasks. Term-level withdrawal metadata also covers later historical certificate renders without changing downloads.

PrepareNotice associates one CancellationNoticeDispatch with an exact original PDF before MessageDeliveryService.Queue inserts attachments. DeliveryAuthority rebuilds current recipient/content authority on every attempt. This narrowly scoped SQL exception leaves generic document sharing unchanged. ApplyNotice records the child delivery outcome on original work; existing legacy receipts never resend. RecordNoticeUnavailable preserves live leases and records one exception after expiry. Cancellation delivery retries retain original work; arbitrary resend returns409.

Migration20260922144152_CancellationOperations, CancellationOperations.Guards and EF snapshot add immutable owned receipts, dispatches, withdrawals and task closures. SQL guards reject mixed legacy/new receipts, early effects, altered provenance and destructive downgrade of retained records.

GET /api/v1/versions/{versionId}/cancellation-consequences is current-scope, private/no-store and exact-version. Program/DI/development dispatcher and generated operations contracts are wired. Both policy UIs show actual documents, delivery attempts, task/MID links and effective outcomes. Commercial negative credit formatting now preserves the sign; zero eligible tasks are described honestly. Full signatures are in docs/design/OPERATIONS-CONTRACTS.md.

## Acceptance

Fresh .local/phase9-15-accepted-strict contains only1319 unit and11 SQL cases from units-all and sql-current; assert-test-results verified1330 unique passes/11realSQL/no skips. RED business rules/API missing-route cases preceded implementation. SQL proves rollback, exact effective instant, current authority, expired lease, immutable/mixed receipt/downgrade protection, legacy compatibility, both CC EL branches, retained certificate bytes and later renders. API host disposal/recreation after provider persistence proves recovery with one provider effect; this is not an OS process restart.

Current browser collector verifies7 Motor Trade and7 Commercial checks with SQL readback. Roots: CoverMGA_Test_0bd3ceb8dbcf406b9a9796370269afee (MT), CoverMGA_Test_4c495d8193d74d0baad96bae11ddd9b1 (CC). Desktop/mobile screenshots were visually reviewed. Browser waits for provider delivery AND original notice receipt before SQL counts. Root420/frontend200, corrected production build, lint/typecheck passed; OpenAPI0 errors/104 existing warnings. git diff --check passed. No acceptance process remains active.

## Boundaries

No retained CoverMGA_Demo migration, production transport, cash refund or human UAT is claimed. Preserve inherited Next generated config changes. OPS requirements remain compound through17/18. Continue16 demo/legacy bridge,17 source obligations,18 final acceptance.

Reviewed implementation commits: backend0789bbd; UI/browserf613754.
