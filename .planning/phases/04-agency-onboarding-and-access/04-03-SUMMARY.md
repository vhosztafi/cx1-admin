---
phase: 04-agency-onboarding-and-access
plan: '03'
subsystem: agency-evidence
tags: [sql-server, evidence, verification, uploads, nextjs]
requires: [04-02]
provides: [immutable-agency-evidence, bounded-evidence-api, current-readiness-checklist, evidence-workspace]
affects: [04-04, 04-05, 04-06, 04-07, 04-08, 09]
requirements-completed: []
completed: 2026-09-14
key-files:
  created: [backend/src/BackOffice.Application/Agencies/AgencyEvidenceRules.cs, backend/src/BackOffice.Application/Agencies/AgencyActivationRules.cs, backend/src/BackOffice.Infrastructure/Agencies/AgencyEvidenceService.cs, backend/src/BackOffice.Api/AgencyEvidenceEndpoints.cs, apps/backoffice/components/agencies/agency-evidence.tsx, scripts/verify-agency-evidence-browser.mjs]
---

# 04-03 — Evidence and current agency readiness

Completed across **a825a83** (storage/service), **0c9f605** (API/readiness), and **b9eb1ee** (workspace, recovery, source-aligned rule and expiry verification).

Agency-owned files retain bounded bytes, name/type/hash and explicit demo screening in SQL. Evidence and check attempts are immutable, own their agency through composite FKs, and bind relevant canonical input fingerprints and rule versions. Ordering uses identity ordinals even when clocks repeat. Explicit staff attestations require a reviewed file and nonempty notes; provider-style checks record deterministic pass/refer/unavailable results without external contact. Old successful results never become current simply through changed dropdowns or formatted references.

The API checks current roles before receipts, serializes writes under the parent lock and returns ID-only receipts/ETags followed by authorized reads. Multipart bodies are bounded before parsing; CSRF uses its required header without early form buffering. Downloads are scoped attachments with nosniff/no-store. Lists are paged and scoped. Readiness holds a repeatable-read aggregate transaction and evaluates current fields, conditions, evidence/rules/expiry, declarations, exact commercial values and manager eligibility. It returns individual paths/stages and remains blocked on unavailable broker-admin/distribution prerequisites until their owning plans provide actual records.

The wizard exposes the saved FCA result at stage1, uploads/attestations/demo checks/history and required-document table at stage4, and a live actionable checklist at stage6. Checklist buttons save the selected owning stage before navigation. File selection and notes protect navigation; uncertain operations retain exact File/FormData or JSON, key and ETag. Replay refreshes the parent version and current readiness. Stale submissions preserve inputs until explicit replacement. Upload/file and evidence history lists have paging. Current results are distinct from immutable recorded history, and unsaved agency edits cannot display a saved check as current.

## Verification

- Full backend gate: **207 passed, 24 real-SQL scenarios, no failures/skips**, `.local/phase4-evidence-complete-results`.
- After the final PI source correction, the targeted evidence suite passed **11 unit + 3 real-SQL cases**, `.local/phase4-evidence-source-rule-results`. The legacy-rule upgrade retains v1 and creates v2 rather than rewriting history; a later rule version makes old evidence stale.
- Pure readiness suite includes48 cases for required/conditional inputs, all8 evidence kinds, dates/limits, missing dependencies and format-only denial. SQL/API evidence covers ownership, byte limits/signatures, immutable attempts, rollback/replay, stale writes, CSRF/roles, protected download, missing/failed/stale/unavailable results, scope-bound cursors, current manager behavior and London-day expiry from stored proof.
- Frontend **20 tests pass**; ESLint, TypeScript and production build pass. Contracts **75 pass**,324 operations,949 mapped controls remain the verified API baseline from0c9f605; this final UI slice does not change contracts.
- Final built `verify-agency-evidence-browser.mjs` passes format-only denial, refer then successful demo check, invalid-file input retention, lost upload response with same-key retry and one stored file, DPA attestation/download, stale attestation notes retained, explicit reload, input-change invalidation, owning-stage navigation/reload and390px layout.
- `verify-agencies-browser.mjs` passes the original six-stage/save/reload/stale/retry/abandonment/filter flow after integration. The desktop rail remains314px. Screenshots compare runtime evidence desktop/mobile with locally rendered source stage4 in `.local/browser-evidence`.
- Inline review corrected explicit select labels, reused form styles, required-document table, paged document selection, evidence-specific recovery copy and generic saved-field checklist labels. `git diff --check` passes. No human UAT or hosted CI is inferred.

## Source correction and remaining phase ownership

Visual comparison found the prototype's PI minimum is **£1,700,000 any one claim**, not the earlier assumed£1,300,000. New demo initialization uses the source value. Existing untouched seeded v1 rules get an additive v2; older rules/evidence remain immutable and stale. Custom later configurations are preserved. The native demo was additively initialized and final browser journeys passed; fictional records were not reset. File screening remains explicitly a demo signature/type check, not a malware scan or regulatory certification.

This completes the evidence plan, not agency activation or all AGY requirements. 04-05 provides viable staged/active broker administrators and invitation lifecycle. 04-06 provides explicit effective distribution eligibility, independent approval and terms. Those dependencies are deliberately unavailable rather than fabricated today. 04-08 performs whole-phase source/UI/security acceptance; Phase9 integrates preserved file IDs and follow-up tasks. No real delivery, broker portal or sales-funnel changes.

Next: **04-04**, durable protected demo notification commands, worker/provider receipts, safe status projections and retry UI. No active preview or test process remains at this boundary.
