---
phase: 07-policy-lifecycle-and-history
plan: '05'
status: complete
completed: 2026-09-17
requirements: [POL-04, POL-06]
requirements_completed: []
production_commit: 7de730a
---

# 07-05 - persisted servicing rating and cumulative pricing

Saved Motor Trade adjustments can now rate and re-rate through the actual UI,
.NET API and SQL-backed dispatcher. Complete cumulative risks are rated for each
London effective date; incremental annual movements earn to term end, with signed
premium, tax and commission and one configured adjustment fee. Issued snapshots
remain unchanged. Rating is explicitly distinct from approval, acceptance and issue.

## Implementation

- Immutable input/result records pin revision, issued base, configuration, full
  risk slices, stable change IDs, operator, time and hashes. Three additive SQL
  migrations retain compound ownership. Deterministic provider attempts survive
  failures and lost responses without duplicated results.
- Apply-time current grants, configuration, revision/base and lease fences reject
  stale authority. Superseded, abandoned and revoked late results remain historical.
- Strict rate/history/job/retry routes check current scope, CSRF, strong versions,
  holder-bound editing lease and command keys. Retry extends only eligible exhausted
  transient jobs and preserves attempts. Signed history cursors bind actor roles,
  route, page size, draft version and expiry; reads are no-store.
- The review panel shows pending, failed, expired, stale and historical states,
  component totals, dated annual/prorated slices, one fee and immutable provenance.
  Local dirty changes cannot be rated. Lost-response retries retain the exact body,
  key and ETag. Mobile tables scroll inside their region instead of wrapping digits.
- API/DI commit 4a930af; UI/browser commit 7de730a. Earlier prerequisite commits and
  failing-first evidence are retained in 07-05-PROGRESS.md.

## Measured verification

- 725 backend unit tests passed, zero skips: .local/phase7-05-final-unit/unit.trx.
- 13 real SQL scenarios passed, zero skips: .local/phase7-05-http-reviewed/sql/sql.trx.
  Includes both products, hosted worker HTTP routes, operator retry, rejection,
  fail-once/timeout deduplication, revoked grants, changed rules, temporary cover,
  exact expiry, stale/abandoned history, storage and immutable issued state.
- Result assertion verified 46 passing cases (33 focused unit +13 SQL) with cutoff
  2026-09-17T18:25:00Z. The 33 cases are included in the full unit suite, not extra.
- 119 frontend cases passed: .local/phase7-05-ui-tests.log. Three new state tests
  cover exact expiry, stale draft versions, missing details and pending/failure.
- 352 root contract/source cases passed: .local/phase7-05-final-contract-tests.log.
  OpenAPI validates in .local/phase7-05-http-openapi-rerun.log with 24 existing
  warnings. A prior Node shutdown crash is retained and not counted as a pass.
- Typecheck, lint and production build passed: .local/phase7-05-ui-final-typecheck.log,
  .local/phase7-05-ui-lint-reviewed.log and .local/phase7-05-ui-final-build.log.
  API Release build passed without warnings/errors in .local/phase7-05-preview-api-build.log.
- Both real Chrome product journeys passed in .local/phase7-05-rating-browser-reviewed.log;
  report/screenshots: .local/browser-evidence/servicing-rating/. They verify two
  effective dates, saved UI rate/re-rate, one fee and component arithmetic, lost
  committed response with identical retry and no duplicate cycle, reload persistence,
  superseded history, mobile containment and unchanged issued snapshots. Their own
  drafts are abandoned at completion; all rating records remain stored.
- Desktop/mobile evidence was inspected; table readability was corrected and rerun.
  Exact expiry is covered by real SQL plus frontend clock-boundary tests, not by
  changing the shared demo clock. Human UAT is not claimed.

## Source coverage and boundaries

The three rating actions owned by this plan are verified: Review & rate now,
Re-rate, and Re-rate combined risk. They use the same persisted full-risk command
in the Review & rate panel. Source fixture amounts/names are not business values.

The broad source-field extraction still includes composite review fields whose
owning behavior belongs to later plans: referrals/evidence 07-06, terms/acceptance
07-08, financial balance 07-09/Phase10, and issue/document consequences 07-10/Phase9.
These are not presented as implemented or as rating authority. Detailed header
parity and the full 393-field inventory remain part of the 07-16 source audit.
No compound POL requirement is marked complete by this plan alone.

The existing demo was upgraded additively and seeded idempotently without reset.
Owned preview PIDs are in .local/phase7-05-preview-pids.json (API5087/web3100).
frontend-code is unchanged. Hosted CI, Docker runtime and human assistive-technology
UAT remain unperformed. Next: 07-06 evidence and referral decisions, inline.
