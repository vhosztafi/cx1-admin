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
2. Resolve explicit clearing/dependency UX before connecting edits: current
   object payloads are partial patches, so omitted scalar fields are retained;
   arrays replace explicitly. Never silently drop a user's clear action.
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
The owned Release API/web previews still run 07-03 builds; rebuild/restart only
those verified owned processes before 07-04 browser work. PID record remains
`.local/phase7-03-preview-pids.json` (verify live command lines before stopping).
