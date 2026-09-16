# 05-08 progress — revision-bound evidence

## Verified pure input rules

`b543b81` adds QuoteEvidenceRules and10unitcases. Fingerprints bind a currently applicable requirement, actual stable driver ID where relevant, the insured declarations, relevant driver/business/previous-insurance subject and trusted capture pins. Driver reordering or another driver's changes do not invalidate that driver's fingerprint; own subject, insured, requirement or pinned-version changes do. Duplicate, missing, foreign and inapplicable item/requirement combinations fail. No-claims proof applies only while source declarations require it. This kernel does not authorize requests or mark evidence received.

Files are bounded to10MiB; supported media PDF/PNG/JPEG/plainUTF8text require matching extensions and signatures. Filenames reject traversal, controls, formatting characters and dangerous separators. Plain text rejects invalidUTF8, controls and HTML-like leading markup. Retained byte copies and SHA256 bind the exact validated bytes. Screening is explicitly a deterministic demo check, not a malware scan or verification of document contents. Attestation/withdrawal reasons are nonblank, bounded and reject unsafe controls.

All559backend unit cases pass, including10newcases,0skips in fresh `.local/phase5-evidence-rules-20260916` and matching.log. No new SQL/API/UI or full-suite acceptance is claimed. Last full integrated baseline remains05-07's649cases/73SQL,78frontend,294contracts/337operations and two-product Chrome121/122. No owned processes remain; frontend-code unchanged.

## Next implementation

1. Implement QuoteEvidenceFile, QuoteCaptureEvidence and append-only withdrawal records with quote/revision/file/item ownership constraints and migration. Inspect current persistence helpers/quote triggers and agency evidence before modification. File bytes live in SQL for the MVP and must never enter audit/receipt/list DTOs; retain content hash and demo screening state. Protect download through current quote scope with attachment disposition and nosniff.
2. Implement closed upload/download/list/attach/withdraw contracts under held QuoteScope and SqlCommandBoundary authorization-before-replay. Upload requires bounded multipart body; attachments bind current revision/fingerprint, owned screened file, applicable requirement and actual item. Withdrawal requires its own evidence ETag and bounded reason. Same-quote equality of relevant fingerprints allows carry-forward; stale and withdrawn associations remain historical.
3. Project actual evidence requirements/current associations and readiness from persisted records only. Keep full assessment globally blocked until05-10/11. Integrate actual evidence controls, lost-response exact replay, stale input handling and demo screening labels.
4. Real SQL/API tests for rollback, source/file/quote/item ownership, revoked authority before replay, hash/restart, stale/carry-forward/withdrawal and scoped download; actual two-product browser uploads/downloads/withdrawal/replay/stalehistory, then fresh full backend assertion and web/build/contract checks before05-08completion. No QUO signoff before05-11.

## Inspected dependencies

05-08PLAN,05-07SUMMARY,STATE/ROADMAP/config/PROJECT, existing QuoteEvidenceRequirements, AgencyEvidenceRules/Service, QuoteLookupRules/Tests, data/API design, context/UI/spec patterns and acceptance backlog. Current storage has no quote evidence models. Existing agency evidence must not be mistaken for same-quote authorization. Future service should translate evidence-file-size to413; generic QuoteInputException currently maps only quote-input-too-large to413. Pure input checks deliberately do not open an endpoint or change readiness.


## Verified storage prerequisite

`ecab402` adds QuoteEvidenceFile, QuoteCaptureEvidence and QuoteEvidenceWithdrawal plus migration20260916122335_QuoteEvidenceStorage. File ownership derives through its immutable quote FK (no independently mutable agency copy). SQL checks enforce10MiB/actual byte length/SHA256, supported media and explicit demo screening. Composite FKs bind evidence to its same-quote source revision and file; actual unique driver membership is checked on insert. Append-only triggers protect bytes, attestations and withdrawal events. One withdrawal per evidence is enforced; withdrawal cannot precede attestation. Attestations carry an immutable RowVersion for their evidence ETag; current/stale/withdrawn state will be derived, not rewritten into historical rows.

The isolated SQL test passed in `.local/phase5-evidence-storage-focused-verified-20260916` (1case,10seconds,0skips). It rejects foreign file/revision/item, absent/wrong requirement target, bad hashes/lengths/reasons, cross-quote/duplicate/backdated withdrawal and history mutation. A fresh DbContext retains exact bytes/hash and event history. EF model synchronization passed in `.local/phase5-evidence-model-check.log`. Initial test compilation errors (raw JSON string braces and calling a default-interface async factory member through a concrete type) were fixed before that passing run.

Storage full regression PASSED: `.local/phase5-evidence-storage-final-20260916` and matching.log contain660=559unit+101integration,74realSQL,0skips,7.5947minutes. Actual660/74result assertion passed. Session93726completed. Migration remains applied only to isolated test DBs, not CoverMGA_Demo. No preview processes.


After storage verification, scoped services were implemented below; closed HTTP contracts remain next. SQL applicability checks do not substitute for service-level QuoteEvidenceRules.Prepare, current held authority, fingerprint/ETag checks or read redaction. Do not expose Content through list/receipt/audit records. Preserve legacy agency evidence behavior.


## Scoped service checkpoint — 2026-09-16

`b632dc7` implements UploadAsync/AttachAsync/WithdrawAsync/DownloadAsync, private file metadata reads and derived evidence applicability. Current held QuoteScope and capture eligibility precede command receipt replay. Upload captures copied, validated bytes/hash; attach checks current quote ETag, revision, applicable requirement/item fingerprint and same-quote screened file before appending attestation. Withdrawal checks evidence ETag, records one immutable reason/actor/time event and retains bytes. All effects, quote activity and command receipt share the SQL transaction. Receipt bodies contain IDs only. File/evidence lists project metadata, never byte arrays; download materializes owned bytes only after current read authority.

Read model returns current requirement fingerprints and derives current/stale/withdrawn/missing states. Unrelated material-facts edits carry a business proof forward; changed business inputs make it stale. Reads of an owned historical revision assess against that proposal; foreign revision/file/quote IDs fail. Every read stays within the held authority transaction. Attachments do not append a proposal revision or touch quote ETag; attestation ETag belongs to immutable evidence and unique withdrawal plus held scope guards repeated withdrawal. HTTP DTOs, route registration, multipart/download headers, UI and readiness integration remain unimplemented.

The expanded real-SQL command/read test passed in `.local/phase5-evidence-reads-focused-20260916` (1case,11seconds,0skips); initial command-only run also passed. Covers uploaded byte/hash preservation after new service instance, metadata redaction, exact replay, foreign file/revision denial, source/fingerprint staleness, current/carry-forward/history/withdrawal projections, upload/attach/withdrawal transaction rollback, evidence ETag, duplicate withdrawal and suspension-before-replay denial for all writes.

FULL SERVICE REGRESSION IS ACTIVE: exec session94761 writes `.local/phase5-evidence-services-final-20260916` and matching.log. Expected661=559unit+102integration/75realSQL. First check the existing session/log, then assert actual661/75 with no skips; do not start a duplicate test or claim this result before completion. No owned previews. Next implement closed HTTP routes and real-cookie API tests using the existing QuoteHttpInput parser, bounded multipart handling, scoped attachment downloads and safe error mapping (evidence-file-size→413). Keep full readiness blocked until actual evidence projection is connected and later assessment gates are available.
