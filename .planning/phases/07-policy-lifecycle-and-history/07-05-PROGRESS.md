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
