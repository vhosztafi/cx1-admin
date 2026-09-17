---
phase: 07-policy-lifecycle-and-history
plan: '03'
status: complete
completed: 2026-09-17
requirements: [POL-02, POL-03, POL-06]
requirements_completed: []
production_commit: 6192d4a
---

# 07-03 — persistent drafts and editing leases

Policy servicing now has real SQL-backed drafts, append-only revisions and
five-minute fenced editing leases. The policy screen creates/resumes a draft;
/drafts/[id] saves request details, renews/releases ownership, performs authorised
takeover and explicitly abandons a draft. Takeover makes the previous editor
read-only while preserving visible local input. Issued cover remains unchanged.

## Implementation and refinements

- Additive ServicingDraftStorage migration, model and guards enforce compound
  policy/base-term/base-version ownership, same-draft current revision, unique
  revision sequence, immutable base provenance, canonical UTF-8 SHA256, append-only
  revisions and one active adjustment/renewal per term. Cancellation may coexist.
  Abandoned drafts cannot reopen or accept revisions. No old migration changed.
- Current identity/roles and policy ownership are held before receipt replay.
  Policy write locking starts with UPDLOCK after existing scope locks; term,
  draft, lease and revision follow. ETags and authenticated holder/token checks
  prevent old saves, heartbeats or releases affecting replacement ownership.
- Internal servicing/underwriter/senior-underwriter can write. Takeover requires
  underwriter/senior-underwriter and a10..2000-character reason. No administration
  or agency bypass. Acquisition replaces GUID fence and increments generation;
  renewal preserves it. Ownership changes and abandonment reasons are audited.
- Proposal parser embeds the closed07-01 schema, rejects duplicate/null fields,
  foreign base/target IDs, unknown payload fields and invalid change identities.
  It bounds input at2MiB,100 changes and1000 aggregate array items. The shared HTTP
  reader still defaults to1MiB for existing quote commands.
- GET /terms/{termId}/drafts supplies the term ETag used by create. Draft/lease
  commands return the complete draft and new ETag, a deliberate response-contract
  refinement avoiding a guessed parent version. Creation Location is the draft
  URL. Actual API routes/DI, current capability policies, CSRF/no-store and strict
  request boundaries are wired. Eight catalogue operations are implemented;
  later servicing endpoints remain pending. Catalogue remains379 operations.
- Reused existing IBM Plex/Panel/forms/layout. New route and client components
  retain a frozen uncertain command for exact retries and preserve dirty proposal
  text across polling/takeover. Five-second ownership refresh is an in-app notice;
  no external message is sent. Abandonment requires reason and explicit checkbox.
- Singleton business/whole-cover targets use policy ID; policyholder uses client
  ID; driver/vehicle/premises/cover sections use retained issued identities.
  Full nested dependency/date/readiness rules and typed editors remain07-04.

## Measured evidence

- Fresh `.local/phase7-03-verified-final-results`, start cutoff in
  `.local/phase7-03-verified-start.txt`:672 unit +3 real SQL/API cases,675 total,
  zero skips. `scripts/assert-test-results.ps1` verified counters, unique results
  and timestamps. Logs:phase7-03-verified-unit.log and-verified-sql.log.
- SQL scenarios cover valid and foreign existing policy bases, duplicate active
  draft races, append-only/hash guards, current revision ownership, two actors,
  takeover, exclusive expiry, stale renew/release/save, stale ETag, reauthorizing
  successful receipt after role revocation, abandonment and issued snapshot/hash
  preservation. HTTP checks cover missing/invalid CSRF, strong ETag, lease header,
  unknown fields, correct Location, replay and persistent readback.
- `.local/phase7-03-storage-3.log` additionally passed the2 retained policy storage
  regression cases. SQL tests use isolated disposable SQL2022 databases.
- `.local/phase7-03-browser-final.log`:2 actual two-user Chrome journeys, Road
  Risks and Combined, with saved readback/reload, lease takeover/local-text
  retention, old-holder denial, CSRF negatives, renew/release/reacquire/save,
  explicit abandonment and unchanged issued bytes. Final report/screenshots:
  `.local/browser-evidence/servicing-draft/`. Desktop/390px layout inspected;
  no horizontal overflow or page errors. Browser fixtures are additive drafts.
- `.local/phase7-03-migration-preservation/{before,after}.txt`: all44 retained
  count/SHA256 sets identical across the additive local-demo migration. No seed
  or reset. Old issued-history guards were also exercised in SQL tests.
-104 frontend tests:phase7-03-web-tests.log.348 root checks plus OpenAPI lint:
  phase7-03-contracts-final.log (26 unused-component warnings, no errors).
  Four source checks rerun after coverage update:phase7-03-source-final.log.
- Typecheck/lint final logs and reviewed Next build passed. API Release final
  build has0 warnings/errors. `git diff --check` passes; frontend-code unchanged.

## Review and limitations

Initial SQL RED failed on the absent ServicingDraft table. Proposal RED failed
compilation on the missing parser. Final checks pass after correcting an EF test
factory call, React render-time clock use, populated-field accessible labels,
shared OpenAPI response mutation and quote-specific creation Location reuse.
All seven source controls owned by07-03 have measured runtime evidence.

This completes the draft/lease slice, not Phase7. No rating, servicing evidence,
terms, adjustment/renewal/cancellation issue, finance payment or external delivery
is claimed. Full backend/retained browser suite and phase verification remain
07-16; human business/assistive-technology UAT, hosted CI and Docker are unperformed.
Proceed sequentially to07-04. Existing ServicingProposalTests.cs is a prerequisite
parser suite; extend it for typed rules rather than replacing its tests.

Owned previews: `.local/phase7-03-preview-pids.json` (API50060, web74428 at recording).
Always verify executable/command line before stopping an owned process.
