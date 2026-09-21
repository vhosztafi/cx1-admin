---
phase: 09-tasks-documents-communication-and-incidents
plan: '05'
status: complete
completed: 2026-09-21
requirements-completed: []
---

# Durable files, current authorization and recovery

Implemented and verified the file-storage slice. OPS-05 remains compound: actual document/version history and its UI are owned by09-07/08. No document generation, human UAT or completed milestone is claimed.

## Delivered

- `FileRules` validates safe names, exact PDF/PNG/JPEG MIME/extension combinations, deterministic headers/trailers and actual length bounded by20MiB or a lower supplied limit. Eighteen pure cases cover unsafe/control/device/path names, mismatches, malformed/truncated content and size bounds. This is demo signature screening, not antivirus or full document parsing.
- `IOperationalFileStore` / `OperationalFileStore` persist generated temporary/ready objects in a configured private nonstatic volume. Nonseekable reads consume at most one byte beyond the bound. SHA256 and length are checked before publishing and on every ready read. Flush, immutable same-volume rename and verified existing-ready fallback recover concurrent finalization and process-boundary crashes. Windows junction rejection is exercised against an actual generated junction.
- `FileObject`, `OperationalFileModel` and migration `20260921162248_OperationalFileObjects` preserve content metadata, original subject, creator and typed source. SQL rejects changed metadata/ownership, deletion, invalid state reversals and downgrading retained identities. Exclusive unique legacy FKs reference original agency/quote/servicing evidence. Insert guards independently verify their parent, screening, metadata and actual byte hash.
- `FileService.Upload` checks current actor/typed parent before consuming bytes, stages outside `SqlCommandBoundary`, and rechecks authority before idempotency replay. Pending metadata and durable finalization work commit atomically. Lost responses replay the same work; failed/replayed staging can leave only temporary orphans.
- `FileFinalizationWorker.Process` holds original authority and parent/file/work SQL locks through bounded local rename and ready publication. SQL transaction locks fence this local operation instead of a remote-provider lease. A process death rolls back SQL while a completed rename remains recoverable. Transient storage failures retry with a six-attempt bound; invalid content/path failures quarantine. Attempt history and terminal job exceptions persist. The workflow adapter resolves file failures through the owned FileObject/OperationalSubject and creates one source-bound follow-up task.
- `BridgeLegacy` / `DownloadFile` retain original evidence bytes, IDs, screening and ownership, including previously screened text/plain content. No copy into the new filesystem and no changes to existing evidence or issued snapshots.
- `CleanupExpiredTemporary` shares the file identity application lock with metadata insertion. Only unreferenced temporary objects older than24hours can be deleted. Ready, pending-referenced and quarantined-referenced objects are preserved.

## HTTP and contract refinement

The additive staging resource uses `POST /api/v1/records/{recordId}/file-uploads?name={encodedFilename}` with a raw bounded file body, matching Content-Type, CSRF and Idempotency-Key. It returns202 `OpsFileUpload` and Location. `GET /api/v1/file-uploads/{uploadId}` reads status; `/content` serves verified attachment bytes with private/no-store and nosniff. Every operation checks current original-parent scope. Pending/quarantined, foreign-parent and revoked identities cannot download. Detected ready-content corruption persists quarantine/audit and returns a safe error without paths.

The upload ID is the separate WorkId, not FileObject.Id or a disk key. The approved multipart `uploadDocument` response remains a DocumentVersion owned by09-07; the staging resource does not impersonate one. OpenAPI, JSON schema and generated TypeScript include the closed staging DTO and three verified routes. Legacy text remains under the original evidence/bridge paths; later document read models must preserve that distinction.

SQL stores SHA256 as binary-collated lowercase64-character hexadecimal, matching retained evidence and filesystem interfaces, instead of the initially proposed binary32 representation. It represents the same256-bit digest; SQL and runtime validate exact length/content. Default Development storage is the API `.local/operational-files`, with explicit absolute `Cover__FileStoragePath` supported. `Cover__FileWorkerEnabled=true` enables the Development dispatcher; default off preserves older/test databases. Private-volume ACLs must prevent untrusted concurrent path replacement. Setup documents migration, retention and backup requirements.

## Verification

Authoritative current gate `.local/phase9-05-final-strict`: **47 unique passing cases, 4 real-SQL scenarios, no skips**, accepted by `scripts/assert-test-results.ps1`. Unit report has35 cases (18 file rules and17 actor-capability cases); integration has12 (8 filesystem,4 real SQL/API). SQL cases run sequentially against owned native SQL2022 databases.

The current SQL/API scenarios prove metadata/terminal-state guards; all three typed evidence sources; SQL rollback and after-commit lost response; exact replay/changed-key conflict; after-rename crash/restart; pending/quarantine denial; checksum/actual-byte round trip; expired orphan cleanup with referenced preservation; source-owned workflow deduplication; current revoked authority; HTTP CSRF/headers/foreign scope; application-host restart; actual hosted dispatcher processing; and persistent quarantine after ready bytes are tampered with. Quote/servicing bridging uses normally issued cover and a real servicing draft, preserving original quote file records and policy snapshot.

Additional checks:412 root tests (`.local/phase9-05-root.log`),55 API/operations contract tests (`.local/phase9-05-api-contracts.log`), OpenAPI validation (`.local/phase9-05-openapi-lint.log`,87 existing warnings), clean diff checks. Relevant builds passed through test compilation. No UI changed, so no new browser/UI acceptance is claimed. Application-host restart here is not the later retained-demo process-restart acceptance.

RED-first evidence: missing FileRules/store/metadata/service symbols and missing HTTP routes. Tests exposed and fixed concurrent pending disappearance, a legacy hash collation conflict and a too-short test command key. Review added atomic quarantine on download-time corruption and SQL-coordinated orphan cleanup before the final gate.

## Continuity

Earlier increments: `ce1fbe5` rules, `4f122b5` store/recovery, `8836f0d` SQL metadata. Current service/API/contracts increment accompanies this summary. Retained `CoverMGA_Demo`, preview processes, original data-protection keys and `frontend-code/` remain unchanged. No verification process remains active. Continue09-06 PDF rendering, then09-07 versioned generation/document composition;09-08 owns document UI. OPS-05 and prototype document controls stay pending until their downstream history/UI obligations are verified.
