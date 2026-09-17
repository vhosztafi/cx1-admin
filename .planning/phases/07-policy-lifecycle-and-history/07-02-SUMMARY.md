---
phase: 07-policy-lifecycle-and-history
plan: '02'
status: complete
completed: 2026-09-17
requirements: [POL-01, POL-05, POL-06]
requirements_completed: []
production_commit: b2b142f
---

# 07-02 — temporal policy reads and discovery

Policy detail and discovery now select issued cover by effective and processing
cutoffs, rather than trusting current-version pointers. The policy screen exposes
both cutoffs, explicitly labelled UTC for entry and London for displayed results.
An unknown-at-cutoff selection returns a bounded not-covered response with no
snapshot and a usable empty state. Returning to current cover reloads the API.

## Implemented

- Shared PolicyTemporalSelector contains the scoped EF candidate projection,
  deterministic pure chronology and composable SQL discovery projection.
- Future adjustments and early renewal cannot displace currently applicable
  cover. Scheduled cancellations take effect at their exact effective instant.
  Transaction sequence/slice/ID resolve ties. Expiring-term selection is strictly
  before the exclusive end, without subtracting an arbitrary timestamp quantum.
- Policy and term as-at endpoints reject absent, duplicate, unknown and unzoned
  cutoff inputs. Current internal capability and same-policy scope remain required.
  Existing explicit ID reads remain; old snapshot bytes/hash are retained.
- Discovery registration matching uses the selected immutable version. The
  retained agency projection remains exactly seven fields and excludes hidden
  risk search. Cursor versioning now includes immutable version/transaction
  counts, while session writes do not invalidate it.
- OpenAPI includes the implemented temporal response union and required cutoff
  query parameters. Added module/endpoints keep the existing files manageable.

## Evidence

- `.local/phase7-02-temporal-sql-final-3.log` and fresh TRX directory:9 passed,
  0 skipped;5 synthetic chronology tests and4 real-SQL tests covering temporal
  reads, original bytes, foreign children, discovery and accepted agency-cookie
  isolation. The temporal SQL case also checks issuance changes list version
  while a session insert does not. Tests use isolated SQL2022 databases.
- `.local/phase7-02-browser-final.log`:both retained Motor Trade demo policies
  pass policy/term API cutoffs, strict query rejection, unknown term rejection,
  historical active cover, unknown-at-cutoff empty state and return to current.
  `.local/browser-evidence/policy-temporal/report.json`, desktop/mobile screenshots
  retain IDs/hashes and readback. Screenshots inspected; no horizontal overflow
  or page errors. No policy data changed by this harness.
- `.local/phase7-02-web-tests-1.log`:104 frontend tests passed.
- `.local/phase7-02-typecheck-final.log`, lint-1, web-build-2 and api-build-final
  logs passed; API build reports0 warnings/errors.
- `.local/phase7-02-contracts-1.log`:348 contract/source/gate tests passed and
  OpenAPI lint passed,379 operations. Subsequent catalogue change only marks
  the two browser/SQL-verified reads implemented; generation succeeds.
- `git diff --check` passed.

RED tests first failed for missing selector/read methods. Real SQL then caught
an EF predicate placed after a record constructor projection; the predicate now
executes before projection. Retained boundary tests were corrected to supply
processing knowledge separately from the earlier effective time. The cursor test
initially violated session expiry because CreatedAt used the real clock; its
fixture now uses the same test clock for creation and expiry. Browser automation
uses normalized datetime-local minute values and the screen reuses existing
responsive form styles. All final checks above pass.

## Review and remaining boundaries

Inline review found no unresolved HIGH/CRITICAL issue in this scope. Authorization
precedes materialization of policy data. The selector does not authorize writes
or manufacture servicing transactions. Current snapshots still use the retained
first-issue format until the owning servicing writers are implemented.

Real persisted multi-transaction future adjustment, renewal and cancellation
scenarios remain required by07-10/12/14. Their chronology is tested synthetically
here; no SQL guard was disabled to fabricate future histories. Full operational
record/history/source fidelity remains07-15/16, with generic modules inPhase9/10.
No full838-test backend rerun, all37-journey rerun, human UAT, hosted CI or Docker
execution is claimed for this focused read change.

Owned running previews are recorded in `.local/phase7-02-preview-pids.json`,
API5087 and Next3100. This supersedes the Phase6 PID file. No migration, demo
reset, sales-funnel edit, real delivery or payment occurred.
