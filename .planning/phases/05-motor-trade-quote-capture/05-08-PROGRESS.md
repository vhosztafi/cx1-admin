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
