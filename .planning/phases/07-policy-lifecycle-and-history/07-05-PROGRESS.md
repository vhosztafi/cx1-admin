# 07-05 execution checkpoint - not complete

The approved plan remains in progress. Do not create a completed SUMMARY or mark
source actions verified from these prerequisite checks. Continue inline with no
agents; preserve all earlier phase and plan work.

## Verified calculator checkpoint: 393d4c2

ServicingRatingRules rates each full cumulative risk through QuoteRatingRules,
then calculates incremental annual differences from the preceding slice. Each
signed movement earns through the existing term end using London calendar days
and the annual anniversary denominator; tax/commission round from each rounded
premium movement. One separately supplied servicing fee is charged. The shared
new-business fee is ignored. The calculator does not imply collection/refund.

Complete cumulative change IDs, strict instant ordering, in-term dates, bounded
penny-exact inputs/rates/totals and the existing resolved-term rules are checked.
Return values copy retained change IDs. Inputs are trusted server projections,
not public client-supplied premium authority.

Twelve new cases failed before implementation (.local/phase7-05-pricing-red.log),
then 35 targeted cases passed (.local/phase7-05-pricing-green.log). The independent
mixed-date fixture gives premium101.91/tax12.23/commission10.19/fee15/gross129.14/
net118.95; return premium, leap/DST, short period, half-penny rounding, malformed
schedules, bounds and both shared Motor Trade pricing configurations are covered.
Full unit regression:713 passed, zero skips, .local/phase7-05-cycle-final/unit/unit.trx.

## Verified rating-cycle storage checkpoint: a0f3f31

Additive ServicingRatingCycleStorage migration adds ServicingCycle and nullable
ServicingDraft.CurrentCycleId. It changes no old migration or issued data.
Compound FKs retain exact draft/policy/term/base/revision and product/configuration
ownership. Version pins and exact UTF-8 SHA256 input are immutable; one persisted
outbox work item owns each cycle. Current cycle cannot cross draft/policy, move
backwards or target a superseded/noncurrent revision. Superseded cycles cannot
regain authority. Rating requires the current active draft revision/pointer.

The SQL test initially failed on missing storage, then passed. It downgrades the
new empty cycle table only inside a disposable test database containing an issued
policy, reapplies the migration, and verifies the issued JSON/hash is unchanged.
It rejects foreign draft/pointers, bad hash, altered input/revision, deletion,
rating without a current pointer and reactivation after supersession.
Review reproduced a missing scenario through SQL NULL comparison; the guard now
explicitly rejects NULL as well as mismatched scenario. Red evidence:
.local/phase7-05-null-scenario-red.log. Final affected SQL verification is recorded
below after completion. Demo database was not reset or upgraded at this checkpoint.

## Next implementation work (required for plan completion)

1. Define the closed persisted rating input/result contracts. Bind all dates,
   stable identities, base/revision/configuration, request clock and fee setting
   in the hash. Source runtime is QuoteRatingEligibility; do not invent a quote
   or reopen the bound source. Reuse its pure helpers/current eligibility through
   a servicing scope. Existing helpers in ServicingDraftService are private and
   require deliberate factoring rather than duplicate unsafe scope logic.
2. Result storage is now implemented and verified in a447229 (see below). Retain
   its compound ownership and immutable guards while wiring the worker.
3. Implement ServicingRatingService, worker and job retry/history. Current grants
   precede receipt replay/retry. Save/abandon invalidates applicability while late
   provider outcomes remain historical. Queue real persisted work; revalidate
   applicability on worker completion. Pin fictional servicing fee15 separately
   from new-business fee35 and retain current agency commercial terms.
4. Wire strict CSRF/ETag/lease/no-store API contracts, DI/worker dispatch, actual
   pending/failure/expired/rerate/history UI and both-product browser checks.
5. Run fresh negative/race SQL cases, contract/UI checks, source review and final
   plan verification. No API operation or source control is newly promoted here.

The planned service filename is not the pure-calculation responsibility; the
additional Application/Policies/ServicingRatingRules.cs is the justified module
boundary. ServicingRecords/Model remain the persistence ownership boundary.
07-04 closed in0d38d91 with summary7036e8e; state advanced in c621ecf. Phase7 remains
4/16 complete and the milestone53/65 implementation plans;07-05 is not complete.

## Final checkpoint verification

713 full unit cases passed in .local/phase7-05-cycle-final/unit/unit.trx. After
the NULL-scenario fix,5 affected real SQL cases passed, zero skips, in
.local/phase7-05-cycle-reviewed/sql/sql.trx; log .local/phase7-05-cycle-reviewed-sql.log.
These cover cycle storage/upgrade, existing draft commands/storage/API and typed
proposal persistence. Earlier5-case SQL results are superseded by this final run.
No worker, public rating API or rating browser result is claimed yet. Reviewed
storage committed in a0f3f31; no unresolved high issue in this prerequisite scope.


## 2026-09-17 rating-result storage checkpoint

Added ServicingRatingResultStorage as a separate additive migration, preserving
ServicingRatingCycleStorage history. Result ownership includes cycle/draft/revision,
work/attempt/rule/input hash plus durable provider operation. Stored output is
append-only and binds exact UTF-8 SHA256 to the provider outcome, matching scenario,
operation, completion/expiry and signed premium/tax/commission/fee/gross/net fields.
Rejected results must have zero monetary components. CurrentRatingId requires a
same-cycle/revision result and rated state; historical/rejected results cannot be
promoted into current authority. Late results may be stored for superseded cycles.
This is storage prerequisite verification, not completed worker orchestration.

The expanded SQL test first failed on missing result storage in
.local/phase7-05-result-red.log. The one-case preliminary run passed in
.local/phase7-05-result/sql/sql.trx. Final coverage now has active and superseded
variants, including positive rated-pointer promotion, immutable signed outcome,
wrong attempt/hash, balanced but provider-inconsistent amounts, and downgrade/
upgrade preservation of an existing issued policy graph. Final results are recorded
below after completion; no source action or API is promoted by storage alone.

Next refine immutable worker input and versioned servicing fee settings, then
implement request/current-eligibility/provider/apply/retry/history and real UI.
New provider result JSON format is servicing-rating-result-1 with operationId,
outcome, completedAt, expiresAt and rating (the CalculatedServicingRating shape).
The worker must store all those fields using JsonSerializerDefaults.Web. It must
read the persisted provider outcome, not accept untrusted caller-supplied money.
The result SQL guard checks all stored component columns against that outcome;
failed/superseded outcomes cannot restore draft authority. All worker/current-scope
and browser race scenarios remain unperformed until their implementation.


Final result-storage evidence:6 real SQL cases passed, zero skips, in
.local/phase7-05-result-final/sql/sql.trx and .local/phase7-05-result-final-sql.log.
This includes both active/superseded variants and retained draft/proposal cases.
Production commit a447229. The713-unit full suite from the previous checkpoint
remains prior evidence for unchanged Application code; it was not rerun for this
Infrastructure-only extension. No demo reset, public endpoint, worker execution,
new rating browser journey or human UAT is claimed. Next is the pinned request
input/configuration and request/provider/apply orchestration. Phase7 stays4/16.


## 2026-09-17 pinned input and fee-setting checkpoint

Added the closed persistence/worker ServicingRatingRequestInput and bounded8MiB
ServicingRatingInput encoder/reader. SHA256 covers exact stored UTF-8 including
all ownership/configuration pins, source hashes, requested actor/clock, fee,
commission, minimum premium, full projected cumulative inputs, schedule dates
and stable change/risk identities. Unknown/duplicate/missing fields, nested
missing constructor facts, string-to-number coercion, unsupported format,
non-UTC request clock, invalid hashes, mismatched terms/driver counts and invalid
pricing are rejected. This is an internal worker contract, not a new HTTP DTO.
The held service must supply independently authorized source projections.

ServicingRatingConfiguration strictly reads fictional GBP/calendar-day/fee
settings. ServicingRatingSeed adds a persistent version1 setting with15.00 fee
only when the scope is absent; it requires held initialization and never replaces
an operator setting. DemoDatabase includes it with underwriting demo seeds.
The demo database was not reset or re-seeded in this checkpoint.

Additive ServicingRatingSettingPin gives cycles a SettingVersion FK. The column
is nullable to preserve historical prerequisite rows without rewriting immutable
inputs; triggers require a real matching pin on every new cycle and prevent
unconfigured historical rows becoming rated. SQL binds setting scope/effective
start/fee, actor/request clock, policy/term and exact base/revision content hashes
to the hashed input. Setting provenance is immutable. No prior migration changed.

Failing-first logs: .local/phase7-05-input-red.log (4 cases),
.local/phase7-05-setting-red.log (positive configured fee fails), and
.local/phase7-05-fee-pin-red.log (missing SQL pin). Final measured results and
commit are recorded below once the verification run finishes.

Next implementation is the actual request service and persistent worker:
- Resolve QuoteScope/QuoteRatingEligibility through the bound source's read-only
  ownership/configuration context; never reopen or mutate that quote. Hold the
  underwriting agency/identity/source-quote scope before policy/term/draft/lease
  to preserve lock ordering. Current policy-draft-write permission and lease apply.
- Current eligibility runs before receipt replay; the receipt fingerprint uses
  submitted version/revision/lease/reason, not a newly generated server clock.
  Build the frozen worker input only inside a fresh command, using one captured
  requestedAt for both encoded input and cycle.CreatedAt.
- Read current servicing setting under lock; it must parse and be effective.
  Set ServicingSettingVersionId on the cycle and encode its exact fee. Preserve
  rate/rule/binder/authority/agency terms and the immutable base/revision hashes.
- Validate the full cumulative proposal assessment and project each full risk via
  QuoteUnderwritingInput before encoding. Return202 for durable pending work.
- Worker must recheck current grants/configuration/revision/base on application,
  retain stale outcomes, and use the stored provider operation for exact retries.
  Add service/race SQL and both-product UI checks before enabling rating endpoints.

No source control, rating command, worker journey or plan completion is claimed.


Final input/setting checkpoint: production0310e25.30 targeted unit cases plus2
real SQL cases pass, zero skips, in .local/phase7-05-input-reviewed/{unit,sql}.
assert-test-results.ps1 verified32 passing cases with cutoff2026-09-17T16:24:26Z.
Logs: .local/phase7-05-input-reviewed-unit.log and -sql.log. SQL includes deliberate
recomputed-hash substitutions of fee, clock, source hashes and policy; all reject,
while both current and retained superseded outcomes persist correctly. Earlier
full713-unit and6-SQL results remain historical, not rerun counts for this change.
No unresolved high finding in this prerequisite scope. Actual request/worker/API/
UI and final07-05 source acceptance remain unfinished. Phase7 stays4/16 complete.


## Verified request and worker checkpoint: e0433b9

Implemented actual transactional ServicingRatingService, held ServicingRatingScope,
and durable ServicingRatingWorker. Requests pin the full cumulative schedule,
immutable base/revision hashes, current rule/terms/runtime/scenario/fee settings
and request clock. Current scope precedes receipt replay; ETag, revision, lease,
latest issued base, readiness and materiality precede fresh work creation.

Save, re-rate and abandon supersede previous cycles while preserving queue and
result history. Provider operations are durable and deduplicated by operation
key/input hash/scenario. Fail-once and timeout-after-success recover without a
second provider result. Apply fences the actual work lease, verifies exact stored
provider bytes, and rechecks current actor grants, configuration and draft/base
identity. Late/abandoned/revoked outcomes remain historical and gain no authority.
SqlJobLeases now supports servicing-rating and terminates pending cycles with jobs.

Two correctness fixes were proven with failing-first SQL tests:
- Eligibility covers the adjustment's remaining coverage, not original inception;
  an eligible newly published rule may begin after the original policy. Original
  full-term dates remain pinned for annual pricing and London-day earning.
- Materiality is assessed across every dated slice. Temporary added cover that
  later returns to the original snapshot remains a valid adjustment.
The earlier readiness failure was a conflicting driver name in the test fixture;
its first/surname and full name were corrected without weakening validation.

Fresh final evidence: .local/phase7-05-runtime-final/{unit,sql}/*.trx;
logs .local/phase7-05-runtime-final-unit.log and -sql.log. All725 unit cases
and10 real SQL cases passed, zero skips. assert-test-results.ps1 verified735
cases with cutoff2026-09-17T17:20:00Z. SQL includes8 actual request/provider
journeys (both products, failure/retry/rejection, revoked requester, replacement
rule and temporary cover) plus2 existing storage provenance/late-output cases.
Wrong work fences, forged provider outcomes, stale ETags, save/abandon invalidation,
receipt revocation, exact operation/result counts, and unchanged issued snapshot
and bound source quote are asserted. git diff --check passes.

Failing-first evidence includes .local/phase7-05-request-red.log,
.local/phase7-05-invalidate-red.log, .local/phase7-05-worker-red.log,
.local/phase7-05-eligibility-red-current.log and .local/phase7-05-temporary-red.log.
Earlier eligibility-red and eligibility-red-valid runs failed fixture publication
constraints; only eligibility-red-current demonstrates the actual eligibility gap.

Remaining07-05 work (no completed SUMMARY yet):
1. Read/history/expired/applicability projection and exact operator retry semantics,
   including current authorization before replay and terminal failure recovery.
2. DI and hosted dispatcher, strict API DTOs, CSRF/no-store/ETag/lease contracts,
   generated contract updates and real negative HTTP tests. No rating endpoint or
   dispatcher was enabled in this checkpoint; runtime services are tested directly.
3. Real pending/failure/expired/history/component/slice UI and persisted browser
   journeys for both products; revalidate required source inventory actions.
4. Final code/source coverage review and measured summary before completing07-05.
No browser check, preview refresh, demo database initialization, business UAT or
phase completion is claimed. frontend-code remains unchanged. Phase7 remains4/16.


## Verified operator recovery checkpoint: a337dfa

ServicingRatingJobs now reads persisted job state and computes RetryAllowed from
current policy access, operator capabilities, exact cycle/configuration pins,
latest base, no existing result and the shared bounded retry budget. Read scope
uses held read locks rather than upgrading shared locks into write locks.

Retry requires current integration-retry, quote-rate and policy-draft-write
capabilities, and rechecks applicability/configuration before receipt replay.
Fresh commands also fence the job ETag, require an exhausted transient failure
with an eligible six-attempt extension (maximum18 total), and atomically reopen
only its current failed cycle. Work, provider identity, original request pins,
attempts and exception history are preserved. Reason and operator are audited.
ServicingRatingScope.Matches centralizes exact current pins for worker/retry.

Meaningful failing-first tests: .local/phase7-05-retry-red.log and
.local/phase7-05-job-read-red.log. Fresh final evidence:
.local/phase7-05-retry-reviewed/{unit,sql}/*.trx; logs
.local/phase7-05-retry-reviewed-unit.log and -sql.log.96 targeted servicing/retry
unit cases and11 real SQL scenarios pass, no skips; assertion gate verified107
cases with cutoff2026-09-17T17:45:00Z. This includes all prior10 rating/storage
scenarios plus exhausted operator recovery: denied ordinary operator, current
retry availability, stale ETag, successful expansion/replay, revoked permission
before replay, seventh attempt producing one result, and changed fee setting
rejecting an earlier receipt. The prior725-unit full run remains historical.

Remaining: full rating history/expired/applicability/component read model; HTTP
job routing/read/retry integration, strict DTOs/CSRF/no-store/ETags, hosted worker
and DI registration; actual rating UI/browser tests; final source review. No new
HTTP endpoint or hosted dispatcher is enabled yet. No phase/plan completion or
business UAT is claimed; Phase7 remains4/16 and07-05 remains in progress.


## Verified rating history checkpoint: f6994d4

ServicingRatingReadModel supplies a typed, scoped read projection for pending,
failed, current rated, expired, stale and superseded cycles. History uses bounded
sequence-keyset pages (default25, maximum50), with the current cycle included
separately even on an older page. Batches are bounded and every result/work row
is constrained to the owned draft/cycle. Empty history is supported.

The view contains stored state plus effective display state, exact request/input
and result hashes, pinned rule/terms/fee-setting IDs, job state/attempts/ETag,
component money as invariant decimal strings, complete cumulative dated slices,
change IDs, expiry, and supersession reason/time. Current applicability rechecks
configuration, latest base, current revision/cycle and expiry. Historical results
remain visible without becoming current authority; read access is reauthorized.
Unreadable legacy details are explicitly unavailable, never fabricated prices.
DraftEtag is the draft command fence, not a representation validator for the
entire time-sensitive history response; the future HTTP response must be no-store.

Failing-first evidence: .local/phase7-05-history-red.log (3 actual SQL failures on
unimplemented history). Final reviewed .local/phase7-05-history-reviewed/{unit,sql}
contains96 targeted unit cases and11 real SQL scenarios, all passing, no skips.
Logs: .local/phase7-05-history-reviewed-unit.log and -sql.log. Assertion gate
verified107 cases with cutoff2026-09-17T18:09:00Z. Additional last review added an
empty-first-load assertion and reran the Combined journey successfully in
.local/phase7-05-history-empty/sql/sql.trx (1 case, a repeat rather than another
unique scenario). That last run uses the same production code as the11-case run.
Coverage includes page boundaries and invalid bounds, current context on older
pages, stored money/slices, exact expiry, changed configuration, preserved late
results, abandonment and revoked reading permission. git diff --check passes.

Next: implement actual DI/hosted dispatcher and strict HTTP rate/history/job
read/retry routes, no-store/CSRF/ETag/lease rules and negative HTTP tests, update
OpenAPI and generated contracts; then real rating UI and both-product browser
journeys. Preserve existing signed-paging conventions when adapting the internal
sequence cursor to HTTP. Final source coverage and07-05 summary remain pending.
No endpoint, preview refresh, browser check, human UAT or phase completion is
claimed. Phase7 remains4/16 complete; frontend-code remains unchanged.
