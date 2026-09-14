# 04-03 implementation progress

Storage/service commit: **a825a83**. Full backend regression: **157 passed, 22 real-SQL scenarios, zero failures/skips**, verified by `assert-test-results.ps1` against `.local/phase4-evidence-foundation-full`. Production build succeeded with zero warnings. Frontend/contracts were not changed by this storage slice; their last verified baseline is18/74 with the 04-02 browser journey passing.

Plan remains **in progress**. Do not create a completion summary or count the plan complete yet.

## Implemented storage/service foundation

- `AgencyEvidenceRules` validates allowed PDF/PNG/JPEG/text signatures and filenames, 10MiB actual byte limits, SHA-256, canonical relevant-input snapshots/fingerprints, explicit demo pass/refer/unavailable checks, manual attestation declaration requirements and evidence freshness. File screening is labelled demo-cleared; it is not a malware scan or regulatory certification.
- `AgencyEvidenceFile`, `AgencyEvidence`, `AgencyCheckAttempt` own immutable SQL rows. Composite agency/file and agency/evidence FKs prohibit cross-agency association. Identity ordinals order attempts even with repeated/frozen clocks. File bytes stay in SQL and are absent from command receipts.
- Migration `20260914101044_AgencyEvidence` adds bounds/state/hash/provenance constraints and update/delete rejection triggers. Attestation notes explicitly require NOT NULL as well as nonempty, avoiding SQL CHECK's unknown-value behavior.
- `AgencyEvidenceService` implements atomic upload, attestation and deterministic check commands with current internal permission before replay, parent ETag/lock, audit/activity, ID-only receipts and no external calls. Actor/file/rule/time/result authority is server-owned. Relevant old evidence stays immutable and becomes stale when the input or rule version changes.
- Additive `agency-compliance` setting version has explicit fictional PI minimum 1,300,000.00, TOBA2026.1, 90-day demo check validity and per-kind scenarios. Real dates use Europe/London for expiry boundaries. Existing setting versions are not overwritten.
- Unit evidence cases: 11 passed. SQL service scenario passes file bytes/hash, replay, retained failed then passed attempts, frozen-clock ordering, FK ownership rejection, immutability, reseed preservation, stale writes, permission denial and current-role revocation before receipt.

## Required next work

1. Implement complete activation validation using current evidence/rule plus all identity/contact/regulatory, signed TOBA, sufficient current PI, financial/sanctions/ownership/DPA/client-money, complete commercial/settlement terms and eligible effective products. User/approval prerequisites remain unavailable until 04-05/06 implement their records. Never return valid=true merely because this slice exists.
2. Add bounded authenticated evidence API routes, explicit authorized list/read projections, safe attachment download (nosniff/attachment), multipart request limits before buffering and strict unknown-field rejection. Register the service only with these tested endpoints. Current public API still has 04-02's unavailable evidence state.
3. Refine OpenAPI evidence mutation responses to ID-only receipts, then authorized reads; align synchronous persisted demo check attempts and file upload metadata with the actual endpoint behavior. Contract tests and all control mappings must still pass.
4. Add actual API tests for denial/CSRF/ownership, oversized and invalid upload bodies, safe download, structured checklist branches, format-only reference denial, failed/unavailable retry and relevant edit invalidation.
5. Wire stage1/4/6 evidence/checklist UI while preserving failed draft inputs and exact uncertain commands. Run frontend checks and built browser acceptance/source comparison. Preserve the 04-02 blue header, table and 314px rail.
6. Additively initialize the demo only after APIs/UI are ready; current demo has the 04-02 migration. Test databases have exercised the evidence migration. Complete full regression gates, review, then 04-03-SUMMARY and advance to 04-04.

No evidence routes or UI are claimed complete by this checkpoint. No public/real broker identity, real messages, external providers or sales-funnel changes.
