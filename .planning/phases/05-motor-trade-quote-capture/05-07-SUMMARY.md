---
phase: 05-motor-trade-quote-capture
plan: '07'
status: complete
completed: 2026-09-16
requirements_completed: []
requirements_supported: [QUO-05]
---

# 05-07 — durable quote lookups and manual recovery

Implementation `a509e6e` connects the previously committed lookup rules/storage to scoped commands, immutable selections, a durable deterministic demo provider and leased Development dispatcher. Proposer/driver/premises address, vehicle and licence controls use actual saved target IDs. Query and initial fingerprint derive from the held saved revision. Current authority precedes command replay; fresh selection checks quote ETag, source revision and target fingerprint. Candidate patches and manual reasons append a revision and provenance atomically. Request deduplication is distinct from exact command replay.

Six scenarios expose one/multiple/no matches, rejection, transient retry and timeout after durable provider completion. Provider identity/outcomes survive worker recreation; stale workers cannot apply. Exhausted leases publish a failed private outcome atomically. Work and generic attempt/result/audit payloads retain opaque identities, while candidates are available only through owned quote reads. The editor reloads the latest100 requests and retains exact commands through uncertain outcomes. Unsaved or stale inputs cannot apply results. Vehicle readiness modes derive only from immutable selections and the selected revision's vehicle fingerprint; unrelated edits preserve provenance, while changed/removed vehicles invalidate it. Demo licence results do not verify entitlement or replace evidence.

The generated API contract was refined explicitly in05-DATA-API-DESIGN: request identifies revision/target/scenario rather than accepting a parallel caller query; selection supplies a stored candidate ID or manual reason rather than caller candidate values. Manual declaration edits use normal draft saves. Browser checks validate real lookup responses against this schema. Demo migration and six versioned scenarios were appended to the preserved CoverMGA_Demo database without reset.

## Verification

- Final fresh `.local/phase5-lookups-complete-20260916` and matching.log: **649 passing =549unit+100integration;73realSQL;0skips**. Result assertion649/73passed; integration8.8346minutes. Earlier runtime-only full run648/72passed in `.local/phase5-lookup-backend-final-20260916`; provenance was added afterward and is covered by the final649run.
- Four focused SQL tests passed in `.local/phase5-lookup-provenance-focused-20260916`: deduplication, rollback, owned reads, current authority before replay, six durable scenarios, provider-success-before-apply recovery, expired-worker/forged-result rejection, all-six-lease exhaustion, selection-vs-save race, selection rollback, and vehicle provenance across unrelated/changed/removed targets. API case covers cookies, CSRF, strict envelopes, ETags, exact replay, scoped private DTOs and current suspension.
- 78frontendtests pass in `.local/phase5-lookups-web-tests.log`;294contracttests/949controls/337operations pass in `.local/phase5-lookups-contracts-final.log`. Lint/typecheck pass in `.local/phase5-lookups-lint-final.log` / `typecheck-final.log`. Final production build `.local/phase5-lookups-build-visual-final.log` passes (BACKOFFICE_API_ORIGIN5087), including type compilation after the class-only styling correction.
- Actual Chrome `.local/phase5-lookups-browser-accepted.log`: Combined **QT-MT-0000000121 revision8** and RoadRisks **QT-MT-0000000122 revision7**. Verifies multiple selection/reload, no-match/manual, lost-response exact replay, retained rejection, fail-once and timeout recovery, editing while pending without overwrites, stale selection lockout, all lookup target controls, actual response schemas,314pxrail and390pxcontainment. `.local/browser-evidence/quote-lookups/report.json` retains IDs. Final styled-control reload in `.local/phase5-lookups-visual-smoke.log` passed both saved fixtures and refreshed desktop/mobile screenshots; screenshots inspected.
- Initial browser preview used the wrong runtime API origin and failed safely; corrected to5087. Initial test changes hit the existing SQL attempt-budget constraint and xUnit/raw-string compilation errors; these were corrected and superseded by passing focused/final runs. See05-07-PROGRESS for details, not acceptance claims.
- All owned previews stopped after verifying process identity: final API67508/web49308. No running test remains. Diff check clean; frontend-code unchanged; no real provider calls or human UAT claim.

## Boundaries and next

Plan05-07complete. Full readiness/rating/issue and final QUO signoff remain gated. Next05-08revision-bound evidence, then05-09history/lifecycle,05-10discovery/matching and05-11phase acceptance. Existing fictional histories and failed outcomes are retained.
