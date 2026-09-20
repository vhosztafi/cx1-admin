---
phase: 08-commercial-combined-back-office
plan: '09'
status: complete
subsystem: commercial-policy-issue
requires: [08-08]
provides: [atomic-commercial-first-issue, commercial-capacity-fence, immutable-issue-capacity-decisions, selected-commercial-documents, commercial-first-policy-readback]
affects: [08-10, 08-12, 08-13, 08-14, 08-15]
requirements-completed: []
completed: 2026-09-20
---

# 08-09 — Atomic Commercial Combined first issue

Implementation commit: `8e1c55b`.

An authorized underwriter can issue accepted Commercial Combined cover through the actual back office. Current scope and authority precede replay; a new issue checks every dated whole-book capacity interval and atomically retains policy/version, exposure/decision, balanced finance, selected documents/outbox and the exact receipt. Two independently scoped competing quotes cannot consume the same remaining capacity.

## Delivered

- `CommercialExposureLock.AcquireAsync/RequireAsync/ForQuoteAsync` implement the canonical transaction-exclusive `CoverMGA.CommercialExposure` fence before retained parent scope locks. Every negative acquisition result becomes a bounded busy conflict. Initialization follows the same ordering. Application and SQL guards reject unfenced exposure, decision and limit writes.
- `CommercialExposureService.AssessIssue/Record` and `CommercialExposureRules.WithinAuthority` apply independent book, binder and actor ceilings. The selected current grant covers every dimension and active condition and is pinned in both the immutable decision and issued source. A higher book publication cannot broaden an older authority. All effective breakpoints, including another policy's future inception and exposure from a retired source binder, remain included.
- `CommercialExposureIssueDecision` and migration `20260920053538_CommercialAtomicIssue` add exact source/hash/time/grant/limit provenance, complete assessed intervals and append-only guards. The migration introduces immutable `Policy.ReferencePrefix`, preserving existing references and assigning new commercial policies `PL-CC-` plus ten digits. Its guarded downgrade restores the computed reference before dropping the prefix dependency; Motor Trade issued history survives round-trip migration.
- Shared `QuoteIssueService` and `PolicyIssueWriter` retain the existing closed command, ETag, CSRF, current authorization and idempotency boundary. Projection/decision, finance, document/outbox and bound quote writes share that transaction. `CommercialDocumentSelection.Kinds` chooses schedule/statement plus EL certificate only when selected. Product-specific demo templates retain exact source version and remain pending generation. Motor Trade still requests all three documents.
- `PolicyIssueSnapshot` and bundled `PolicySnapshotShape` validate closed `issued-commercial-1` data: normalized property locations, per-location property sections, selected liability/BI/extensions, glass basis without an invented monetary limit, term, exact premiums and authority/source provenance. No Motor Trade risk fields are introduced.
- The issue receipt and first-policy readback add `commercialExposureDecisionId`. Capacity409 responses expose bounded dated aggregate intervals, selected publication IDs/hashes and effective limits only after current internal issue authorization. They contain no foreign policy identities and are non-cacheable. Advisory exposure GETs remain explicitly pending08-10.
- The shared confirmation validates product, selected-cover document count and decision identity. Busy/unknown outcomes retain the original command key; arbitrary server diagnostics are not UI copy. `CommercialPolicyRecord` renders persisted source/cover, posting and document requests; the quote links to its issued policy. Complete commercial policy tabs/history/exposure are the next plan.
- `08-09-DECISIONS.md` and `docs/design/COMMERCIAL-EXPOSURE.md` document final signatures, JSON/HTTP fields, lock ordering and downstream ownership. Source-ledger district-capacity and EL-certificate branches now retain actual SQL/browser evidence; display controls remain08-10. Denominators166/109/60/298/7 are unchanged.

## Verification

Strict `.local/phase8-09-final` accounting verifies56 unique passing backend cases, including19 real-SQL scenarios, with no skips:

-36 focused units: `.local/phase8-09-current-unit/unit.trx`.20 dated exposure/authority cases,3 selected-document cases,6 commercial snapshot cases and7 retained Motor Trade snapshot cases.
-12 issue/migration/retained Motor Trade SQL cases: `.local/phase8-09-issue-final-sql/sql.trx`,5m25s. Independent agency/client quote race; exact replay after unrelated capacity consumption and lower publication; revoked grant denial; five-second lock contention and same-key recovery; injected late rollback without orphan effects; future collision with same/replacement binder; EL on/off; Motor Trade source identity, three documents, contention/replay/rollback; downgrade/upgrade preserving issued Motor Trade JSON/hash/reference/finance/documents.
-7 HTTP/storage/selector/rating cases: `.local/phase8-09-http-storage-sql/sql.trx`,2m43s, including6 SQL. Actual scoped/non-cacheable capacity409 and agency denial; valid-but-unfenced SQL inserts rejected with51920; retained exposure source/child/limit constraints; independent temporal selector parity; rating success/stale configuration/revoked requester and current API assertions.
-1 complete actual browser issue case: `.local/phase8-09-browser-59c34f2d-59de-4fbc-ac22-4c4d6dff32e0/sql.trx`,5m01s. Artifacts `.local/browser-evidence/commercial-capture/CoverMGA_Test_066690d7d91e467da78ddeacb9cb37d2/` contain report, validated receipt/policy JSON, carrier/terms evidence and desktop/390px screenshots. The journey captures all109 source questions, resolves exact carrier/proof/internal decisions, prepares/delivers/accepts terms, issues through UI and verifies immutable readback after reload. No page overflow; unsupported commercial servicing actions absent.
-385 root contract/source tests: `.local/phase8-09-final-root.log`;164 frontend tests: `.local/phase8-09-final-web.log`;4 additional source-ledger checks after evidence update: `.local/phase8-09-source-ledger.log`. API contracts45 passed; contract lint validates422 operations with56 warnings. Typecheck, lint and production web build pass. Backend `.local/phase8-09-http-build.log` has zero warnings/errors; current model has no pending changes. `git diff --check` passes.

Meaningful RED cases preceded document selection, commercial snapshot and frontend receipt implementation. Subsequent test-fixture corrections retained all guards: EL selection is a risk declaration, deselection clears employer limit/ERN, and revocation targets the commercial grant. Older rating assertions were updated for implemented06/07 submission and referred state. Strict final accounting includes only successful reports. The final explicit response no-store change is verified by the focused HTTP test; it does not change the preceding browser's successful issue path.

## Review and next ownership

`08-09-REVIEW.md` passed with no unresolved HIGH/CRITICAL finding. The live demo database/services/keys, published history and sales funnel are preserved. No real provider/email, human UAT, hosted CI or Docker run is claimed. Documents are durable generation requests; operational rendering/delivery remain Phase9. Full phase regression and preservation remain08-16.

Continue08-10: scoped quote/draft/policy exposure reads with capability-shaped privacy, coherent effective/known-time provenance, complete commercial tabs/schedules/history, exact selected-version source values and retained Motor Trade policy views. Commercial servicing issue remains unavailable until its owning plans. CC-03/04 and compoundCC-05 remain open until their complete workflows are delivered.

## Self-Check: PASSED

Implementation, reviewed contracts, source evidence and successful current verification exist. Strict accounting contains only executed successful cases. Remaining display, servicing and document-operation boundaries are explicit.
