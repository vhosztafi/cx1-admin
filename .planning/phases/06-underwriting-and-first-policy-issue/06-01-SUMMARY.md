---
phase: 06-underwriting-and-first-policy-issue
plan: '01'
status: complete
completed: 2026-09-16
implementation_commit: 4800d71
requirements_completed: []
---

# 06-01 — Strict underwriting contracts and source fixtures

Implemented both tasks in contract scope. Commit `4800d71` supplies closed versioned rating/binder/authority schemas, eight fictional configuration definitions, requested-section capture schemas, deterministic pricing/posting fixtures, seventeen positive API mutation fixtures and strict underwriting operations. The generated API contains354 operations; all Phase6 runtime operations remain explicitly pending.

Requested sections are separate from system-owned issued cover. Retained capture examples remain valid. New underwriting examples explicitly declare sections for each Motor Trade product. Rules bind pinned numeric reference metadata rather than labels; source UW-09/UW-22, W-07 and valeting/young-driver/NCB/tools factors have explicit owners and independent fixture checks.

Commands require owning quote concurrency, current context identities, exact version hashes, proof and child concurrency where applicable. No client authority/price override is accepted. Capacity history exposes authorised limits and validity dates; queued delivery is distinct from delivered. Acceptance and first issue retain durable identities without invented payments.

## Verification

- `node scripts/validate-contracts.mjs`:314 tests passed, zero failed/skipped;949 controls,5 conditional rules,354 operations. Log `.local/phase6-01-contracts-final.log`.
- After the final capacity-history schema refinement, the7 underwriting API tests passed again.
- Five generated artifacts (draft/ready quote schemas, config schema, demo fixture and OpenAPI) were byte-identical on repeated generation. Initial comparison included a newly edited generator against its old output; regeneration followed by comparison passed.
- Source checks cover19 input mappings,73 reference bindings,86 question rows,211 audited controls and their Phase6 operation dependencies. Worked pricing uses independent exact-penny arithmetic.
- `git diff --check` passed; `frontend-code` unchanged. No backend changes in this slice, so no claim of new SQL/browser/runtime verification.

## Refinements and remaining work

The quote generator owns the additive capture schemas, rather than editing the policy generator directly. Shared closed-schema helpers and separate API fixture tests keep generated operations consistent. The existing form-field test now resolves all union branches, preserving strict required-field validation.

06-02 owns runtime rules, additive storage and seed/migration verification. Subsequent plans own actual commands, UI, evidence, capacity, terms, acceptance, issue and restart testing. None of UWR01–07 is complete from contract evidence alone; future servicing, documents and finance administration retain their later-phase ownership.
