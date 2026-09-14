---
phase: 04-agency-onboarding-and-access
plan: '02'
subsystem: agency-drafts
tags: [sql-server, nextjs, agency, concurrency, onboarding]
requires: [04-01]
provides: [persistent-agency-drafts, atomic-agency-api, six-stage-agency-workspace]
affects: [04-03, 04-04, 04-05, 04-06, 04-07, 04-08]
requirements-completed: []
completed: 2026-09-14
key-files:
  created: [backend/src/BackOffice.Application/Agencies/AgencyDraftRules.cs, backend/src/BackOffice.Infrastructure/Agencies/AgencyDraftService.cs, backend/src/BackOffice.Api/AgencyEndpoints.cs, backend/src/BackOffice.Infrastructure/Persistence/AgencyModel.cs, backend/src/BackOffice.Infrastructure/Persistence/AgencyRecords.cs, apps/backoffice/components/agencies/agency-wizard.tsx, scripts/verify-agencies-browser.mjs]
---

# 04-02 — Persistent agency drafts and workspace

Implemented in **2253702** (storage/API/contracts/tests) and **5636154** (workspace/browser checks). Existing Agency IDs and client relationships survive migration and additive initialization. Agency references use a transactional SQL sequence; gaps after rollback are allowed. Strict partial JSON preserves every reviewed source field and all 24 fixed choice families, with structured addresses, exact money and integer commission basis points. Unknown fields, forged state, invalid nulls and malformed values are rejected.

Create/save commit details, current stage and optional product selections under one transaction with audit, immutable activity and an ID-only command receipt. Current internal authorization precedes receipt replay; stale writes retain the existing record. Read-only underwriting access is distinct from agency-admin/system-admin writes. Lists use actual bounded projections and authenticated, filter-bound cursors; account/user/action values that do not exist yet remain explicitly unavailable. Internal broker scope is still denied; real broker identity behavior belongs to 04-07.

The agency directory, record tabs and six-stage wizard persist first save, Back/Continue/step navigation and Save and exit. Reload restores the saved stage. An uncertain save freezes further edits and retains its exact payload/key/ETag for retry; stale edits remain visible until explicit replacement. Draft abandonment preserves history. The prototype's 314px rail, blue record header, table structure, progress toggle and summary are present. Dynamic manager choices retain IDs even when display names duplicate, and activity has cursor paging.

## Verification and review

- Full backend suite: **145 passed, 21 real-SQL scenarios, zero failures/skips**, verified by `assert-test-results.ps1` against `.local/phase4-draft-final-results`.
- SQL tests cover legacy migration, unchanged identities, parallel unique references, rollback, FK/JSON/step/duplicate-reference constraints, immutable activity, repeated seed, atomic detail/product saves, changed-intent replay, stale/competing writes, current-role revocation and filtered/wrong-user cursors. Shared `PartyPagingTests` covers expiry, tampering and changed scope/order. External broker sessions are not enabled by this slice; capability tests deny agency-scoped actors.
- Frontend: **18 tests passed**, ESLint clean and production Next build/type checking passed.
- OpenAPI generated and linted; **74 contract tests passed**, 320 operations and 949 reviewed controls remain mapped.
- Built Chrome journey passed six-stage save/reload, lost-response replay without duplication, retained stale edits, persisted product/account/activity values, abandonment and URL-filter reload. Toggle works, desktop rail measures 314px, 390px viewport has no page overflow.
- Visual inspection used `.local/browser-evidence/agency-onboarding-desktop.png`, `agency-onboarding-mobile.png`, `agency-directory-desktop.png` and the locally rendered source. Layout remains a Phase 4 foundation; final complete-stage visual review is required in 04-08 as evidence/invitations/approval controls arrive. This is agent inspection, not human UAT.
- Inline code review checked ownership, mutation receipt scope, raw DTO bounds, concurrency, unavailable projections and role boundaries. Corrected manager display/duplicate names, missing progress toggle, main-contact projection and truncated activity history before commit. `git diff --check` passed.

## Refinements and remaining ownership

AgencyOnboarding follows the shared surrogate UUID record pattern with unique AgencyId FK. Search/concurrency fields are projected relationally; other typed declarations stay in JSON instead of duplicating authority. Both create and save use `{details,onboardingStep,products?}`. These refinements are recorded in DATA-MODEL, DATA-API-DESIGN and OpenAPI.

An upgrade test initially failed because raw SQL interpreted JSON braces as formatting placeholders; parameterizing the JSON fixed the test and the full suite passes. A browser harness initially removed its POST interceptor during navigation, racing an unrelated read. Keeping the scoped interceptor installed removed that harness race; no production timeout was hidden.

Next **04-03** adds immutable owned evidence/files, deterministic checks and complete activation validation. 04-05 must extend abandonment to revoke staged/pending invitations in the same transaction before enabling invitations. 04-06 adds independent activation/terms decisions. 04-07 implements trusted scope. AGY requirements remain pending overall, including cross-phase accounts/sharing obligations in ACCEPTANCE-BACKLOG.md. No sales-funnel edits, real delivery, deployment, hosted CI or human acceptance claimed.
