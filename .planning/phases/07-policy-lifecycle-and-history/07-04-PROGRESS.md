# 07-04 execution checkpoint — not complete

2026-09-17. Sequential inline execution under the approved `--auto` scope.
Production checkpoint: `f654ae8`. Plans 07-01..03 remain complete; 07-04 remains
the earliest unfinished plan. No completion SUMMARY or source coverage claim.

## Implemented and wired

- Pure `ServicingProposalRules` projects typed patches onto exact issued base,
  preserving its bytes and excluding derived pricing/sections/driver-basis and
  account ownership from editable capture. Identity-keyed reorder is ignored in
  comparison. Typed add/update/remove validates stable and nested ownership,
  foreign targets, duplicate effective targets and dependent references.
- Common/London date intent, gap/fold offsets, exclusive term end, latest issued
  effective time including future slices, cover-only date mode, driver backdate
  prohibition and current senior authority are assessed server-side. Invalid
  dates remain capture with blockers, never valid progression authority.
- Cumulative slices each receive capture/readiness/section checks. A later
  correction cannot hide an earlier invalid state. Existing shared quote rules
  are reused without borrowing evidence/lookup/underwriting authority.
- `SaveAsync` validates in held current scope before appending. Failure leaves
  ETag/revisions unchanged; incomplete valid-shaped details remain saveable.
- Additive `GET /api/v1/drafts/{draftId}/editor` returns saved revision, scoped
  client target, catalogue versions, typed base/proposed values, stable diff,
  blockers and cumulative slices. Real API route, current authorization,
  no-store and strong draft ETag; generated closed contracts updated.
- Actual SQL demonstrates persisted readback, unchanged issued snapshot/hash
  and registration rows, atomic conflict rejection and revoked-role denial.

## Fresh verification

- 689 unit cases, including 21 `ServicingProposalTests` cases; no skips.
  `.local/phase7-04-backend-unit-final/unit.trx` and matching `.log`.
- 3 SQL/API cases; no skips. `.local/phase7-04-backend-sql-final/sql.trx`
  and matching `.log`. Includes new typed projection test, retained lease/
  authorization/replay test, and actual WebApplicationFactory API/CSRF test.
- `scripts/assert-test-results.ps1` verified 692 distinct passing cases, 3 real
  SQL scenarios, fresh since 2026-09-17T11:40:00Z, from copied original reports
  in `.local/phase7-04-backend-evidence`. No edited report counters/timestamps.
- 349 root contract/source tests passed; `.local/phase7-04-backend-contracts-final.log`.
- OpenAPI lint passed, 380 operations, 24 unused-component warnings;
  `.local/phase7-04-backend-openapi-final.log`.
- `git diff --check` passed. No sales funnel changes or demo database reset.
- Meaningful failing-first evidence: missing proposal rules, incomplete vehicle
  readiness, SQL conflicting changes accepted by the previous target-only
  validator, and missing editor read. See `.local/phase7-04-proposal-red.log`,
  `phase7-04-expanded-red.log`, `phase7-04-sql-red.log`, `phase7-04-editor-red.log`.

## Continue here

1. Implement typed frontend picker/dialogs for all eight source categories,
   using existing source capture controls and current catalogue. Consume the
   new scoped editor read, checking revision/ETag before displaying saved diff.
   Preserve dirty local state and frozen uncertain retries on conflicts.
2. Connect explicit clearing/dependency UX: updates now support
   `payloadMode: "replace"` (checkpoint `cad94b0` below). Normal partial patches
   still retain omitted scalar fields; arrays replace explicitly. Editors that
   submit a complete typed target should use replacement for explicit clearing.
   Specified-vehicle/driver-reference removals need deliberate dependent edits,
   not silent dangling-reference cleanup. Make any needed closed contract
   refinement with negative tests, rather than allowing arbitrary JSON paths.
3. Add requested-by, offset choice, common/cover-only later dates, precise
   field/slice errors, full before/after display, removal of proposed changes,
   supported sections for both Motor products, and focus restoration.
4. Add meaningful frontend diff/date tests and
   `scripts/verify-servicing-editors-browser.mjs`. Verify every category,
   incomplete driver save/reload, date overrides and rejection, both products,
   persisted SQL/API readback, desktop and 390px. Update owned source entries
   only after those checks. Then write 07-04-SUMMARY and advance to 07-05.

No 07-04 browser, frontend, human UAT, rating, issue, renewal or cancellation
completion is claimed. No new migration was needed for this checkpoint.
The owned Release API/web previews now run the 07-04 date/review checkpoint.
PID record is `.local/phase7-04-preview-pids.json` (verify live command lines
before stopping). The earlier 07-03 PID record is obsolete.

## Autonomous continuation — explicit clearing contract

Checkpoint `cad94b0` adds update-only `payloadMode: "replace"` to the closed
typed change contract. It replaces the selected target while preserving its
stable envelope-owned identity. Add/remove reject the mode. Existing partial
patch callers keep their semantics. Foreign ownership, system fields and
dependency validation remain enforced. This enables clearing an optional driver
field, explicitly removing a dependent vehicle owner, and deselecting a cover
section without retaining inactive limits/excesses.

Meaningful failing-first tests captured unsupported replacement before the
implementation in `.local/phase7-04-replacement-red.log`. Final verification:
26 focused unit cases, 1 real SQL persistence/authorization case, 12 servicing
contract tests; no skips. `assert-test-results.ps1` verified the 27 backend cases
in `.local/phase7-04-replacement-verified` with timestamps after12:00UTC.
Logs: `phase7-04-replacement-verified-unit.log`,
`phase7-04-replacement-final-sql.log`, `phase7-04-replacement-contracts.log`.
OpenAPI lint passed (24 unused-component warnings); `git diff --check` passed.
No UI/browser completion is claimed; 07-04 remains open. Next implement the
typed UI using this explicit replacement mode and deliberate dependency edits.

## Autonomous continuation — dates and saved comparison UI

Checkpoint `72b2e55` wires the saved editor projection into the workspace. It
only displays comparison for an exact draft/revision/strong-ETag match. Draft
and editor reads run together; mismatched reads wait for the next refresh.
Dirty local edits are retained and explicitly excluded from the saved review.
Before/after values and readiness messages are readable, with cumulative saved
dates. Requester kind/name, London date/time/offset, shared versus individual
cover dates and removal of proposed changes are wired to persisted proposals.
Common date changes clear a stale offset; switching to shared mode refuses to
silently discard cover overrides. All edit controls obey the lease/uncertain
retry gates already in place.

Verification:108 frontend tests (including4 new meaningful date/stale-diff
cases), typecheck, lint and production Next/API builds passed. Final logs:
`.local/phase7-04-date-review-web-tests.log`, `-typecheck-final.log`,
`-lint-reviewed.log`, `-web-reviewed.log`, and `-api-build.log` (same prefix).
`verify-servicing-date-review-browser.mjs` passed both products: London gap/fold
feedback, persisted offset/requester, real saved comparison/blocker, retained
local text across polling, proposal removal persisted, unchanged issued JSON,
no page errors or390px overflow. Report/screenshots under
`.local/browser-evidence/servicing-date-review`; desktop/mobile inspected.
`phase7-04-date-review-browser-final.log` is the final browser log.
The existing two-user lease browser regression also passed for both products:
`.local/phase7-04-date-review-lease-regression.log`.

The browser harness explicitly prepares one driver change through the real API
to exercise comparison/removal; it does NOT claim typed picker coverage.
Cancellation drafts are used as disposable-but-retained test records because
they may coexist; all test drafts are abandoned, never deleted or reset.

Next: build the eight typed picker/edit dialogs and explicit dependent edit UX,
consume the current catalogue/base from the editor projection, cover the full
typed data/clear/reload and later cover-date browser journeys, verify focus
restoration and source-owned controls, then complete07-04. The date fields and
saved review components should be reused, not reimplemented. Existing focused
frontend tests should be extended. No plan completion or human UAT claim.

## Autonomous continuation — typed named drivers

Checkpoint `7cbb2dd` implements named-driver add/edit/remove dialogs. This is
two of the eight source picker categories, with editing of existing proposals
as well. `servicing-driver-editor.tsx` is a focused component rather than putting
all categories in one large editor file; future categories should compose beside
it. `QuoteDrivers` now accepts an optional targetId, retaining the whole risk
context while limiting controls to that driver and its five history groups.
Default quote behavior remains unchanged. Scope-independent driver-basis and
business declarations are not silently modified from this item editor.

Stable change/item IDs survive editing, save and reload. Updating an issued
driver uses explicit typed replacement, so clearing a field persists. Removing
a newly added driver cancels its addition. Removing an issued driver creates a
typed removal. Existing dependency validation remains in force. Dialogs retain
values through a two-user takeover; Keep form and return exposes the existing
lease controls, then Resume driver form restores the same values. Native modal
focus is restored, and 390px dialog contents remain within the viewport.

`servicing-change-form.ts` supplies local typed context projection only, never
server authority. It follows the saved/common effective dates and typed
replacement semantics; UUID casing cannot invent a foreign identity. Server
capture, ownership and readiness validation remains authoritative.

Final evidence:
- 111 frontend tests, including7 servicing date/projection/identity cases;
  `.local/phase7-04-driver-web-tests-final.log`.
- Typecheck, lint and Next build passed: `phase7-04-driver-typecheck-reviewed.log`,
  `phase7-04-driver-lint-copy.log`, `phase7-04-driver-web-build-final.log` in.local.
- Both-product, two-user actual Chrome journeys passed:
  `.local/phase7-04-driver-browser-complete.log` and
  `.local/browser-evidence/servicing-driver/report.json`. The test creates and
  persists incomplete drivers, all five nested history groups, edits/reloads
  stable identities, explicitly clears an issued DOB, proposes issued-driver
  removal, verifies modal retention across takeover, focus and390px containment,
  and confirms unchanged issued JSON. Desktop/mobile dialogs were inspected.
- Meaningful failing-first module/UUID cases retained in
  `phase7-04-driver-form-red.log` and `phase7-04-driver-id-red.log`.
- No new backend changes/migrations in this checkpoint; existing SQL regression
  evidence remains applicable and browser calls use real persisted APIs.

Only source options CTL-48357417c7d9 and CTL-d95a33ac4ba9 were marked verified.
Other controls and the complete plan remain open. A final help-text-only edit
removes an inappropriate quote-evidence navigation reference from servicing;
lint passed after it. The owned preview has the verified behavior immediately
before that copy edit; the next frontend rebuild will include the revised copy.

Next: six remaining categories — add/remove vehicle, premises, trade activities,
cover/limits/excesses and policyholder correction. Reuse the typed projection,
date/review components and explicit replacement contract. Resolve specified
vehicle and other dependent declarations explicitly; implement and verify the
complete later-cover-date journey, source row actions and overall07-04 checks
before creating a completion SUMMARY. Current preview record remains
`.local/phase7-04-preview-pids.json` (API71960, web37800; verify before stopping).

## Specified vehicle dependency checkpoint

Commit `4bd9a3f` adds optional closed vehicle-only selection/requirement
 declarations, with matching backend and local form projection. Removing a
selected vehicle requires explicit deselection; a removed vehicle cannot be
selected. Conflicting global requirement values are rejected. Repeating the
same selection preserves array order and avoids false material changes.

Evidence: 30 focused unit tests and one real SQL test passed, no skips, verified
by assert-test-results.ps1 from `.local/phase7-04-specified-verified`. The SQL
case proves persisted declarations/projection and unchanged issued data.
Thirteen contract tests and OpenAPI lint pass (24 existing warnings). All113
frontend tests, typecheck and lint pass; logs use `.local/phase7-04-specified-*`.
Failing-first backend, membership-order and frontend cases are retained there.

This is a dependency refinement, not vehicle UI completion. Six picker
categories still remain. Next implement the vehicle dialog using QuoteVehicles
with targetId scoping, explicit selection/requirement controls, and a removal
confirmation. Synchronize any explicit global requirement choice across prior
vehicle declarations before applying to the draft. Preserve incomplete capture,
lease retention, item identities, and issued state. Rebuild the owned API/web
previews before fresh browser testing; current previews predate this refinement.
No plan completion SUMMARY or additional source coverage is claimed.

## Typed vehicle dialog checkpoint

Commit `cd54956` implements add/edit/remove vehicles with source fields and
modification rows, explicit selection/global requirement choices, stable IDs,
and explicit replacement clearing. Cancellation of a proposed addition clearly
removes its declaration. Existing vehicle removal requires deselection. A
requirement choice synchronizes other vehicle declarations with visible copy.
QuoteVehicles targetId mode keeps ownership/premises context but hides unrelated
portfolio/trade-plate controls and register row actions; default quote use stays
unchanged. Dialog values survive takeover, return and reacquisition.

Verified:114 frontend tests, typecheck, lint, Next build and4 source checks.
Logs `.local/phase7-04-vehicle-*`; failing-first synchronization test retained.
Both Motor Trade products passed actual Chrome persistence, incomplete capture,
modification IDs after reload, addition cancellation, issued registration clear,
explicit deselection/removal, two-user lease retention, focus restoration and
390px containment. Issued snapshots remain unchanged. Evidence:
`.local/browser-evidence/servicing-vehicle/report.json` and
`.local/phase7-04-vehicle-browser-reviewed.log`. Desktop/mobile screenshots were
inspected. Earlier browser attempts failed on build-origin/test-selector setup;
the corrected run completed both journeys. No new backend changes since the
verified dependency checkpoint. Only source options CTL-06d9667cb5b0 and
CTL-2ce7af342224 gained verified coverage.

Next: premises, trade activities, cover/limits/excesses and policyholder
correction, then all remaining07-04 source/browser checks including later cover
dates. Do not complete the plan yet. Full phase gate and human UAT remain open.
Owned previews now API58760 and web42752, with current API and web builds; verify
identities before stopping. Build Next with BACKOFFICE_API_ORIGIN set to5087;
rewrites are baked into the build. Preview record is still
`.local/phase7-04-preview-pids.json`.

## Typed premises checkpoint

Commit `4fe6e84` adds premises add/edit/remove proposal dialogs. Source fields
remain product-specific, with whole capture context and a single targetId in
QuoteRepeatedSection. Nested address clearing uses explicit replacement; IDs
survive edits/reload. No backend production change or migration was required.

115 frontend tests,31 focused backend unit tests, typecheck, lint and Next build
pass; logs `.local/phase7-04-premises-*`. The new unit case checks postcode
clearing and rejects deleting premises referenced by covered sections. This is
regression coverage of existing backend rules, not a claimed failing-first fix.
Both-product Chrome journeys persist new premises and edits, cancellation of
additions, two-user modal retention, focus restoration and390px containment.
Combined also verifies exact buildings amount and clearing an issued postcode;
the Road Risks fixture has no issued premises, so that branch is not claimed.
Issued snapshots remain unchanged. Evidence:
`.local/browser-evidence/servicing-premises/report.json` and
`.local/phase7-04-premises-browser.log`; desktop/mobile images were inspected.
Actual persisted browser APIs use native SQL; no fresh standalone SQL suite
was needed for this UI-only production change. CTL-8c4c56e91474 is verified.

Next: trade activities, cover/limits/excesses and policyholder correction, then
remaining07-04 source and complete later-cover-date/dependency journeys. Do not
complete07-04 yet. Owned API58760 unchanged, web12364 now serves this build;
verify process identities before stopping. The preview PID file is current.
