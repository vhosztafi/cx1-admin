# 07-06 execution checkpoint - not complete

Continue inline with no agents under the approved07-06 plan.07-05 is complete;
its actual API/UI and additive demo upgrade are recorded in07-05-SUMMARY.md.
Phase7 remains5/16;54/65 total implementation plans. Do not create07-06-SUMMARY
until the storage, real service/API/UI and browser acceptance are complete.

## Verified cumulative proof prerequisite:4893ba1

ServicingEvidenceRules derives proof requirements from every immutable full-risk
slice, rather than only the final proposed state. Earlier drivers/premises retain
proof requirements when a later slice removes coverage. Stable item/purpose pairs
aggregate their applicable effective dates. Captured received flags grant nothing.
Motor-trader, per-driver photocard/driving-record, declared discount, young trading
history and selected-premises security use existing shared requirement rules.

Requirements bind exact draft/cycle/revision/rating/input hash, capture pins and
canonical full cumulative schedule including dates/trading years. Satisfaction
also requires the current context, exact purpose/target, accepted screening and
accepted review, and no withdrawal. A review test caught old requirement context
being reused with changed input/configuration despite identical IDs; context
binding now explicitly rejects it. Duplicate/foreign stable IDs and malformed
schedule/context reject before proof association.

Failing-first evidence: .local/phase7-06-proof-red-behavior/unit.trx contains4 actual
failing tests; .local/phase7-06-proof-review-red/unit.trx contains1 review failure.
Earlier compile/analyser errors are not counted as behavioral evidence.
Final .local/phase7-06-proof-reviewed/unit.trx passes8 targeted cases (4new and4
shared quote-proof cases), no skips. This is pure application-layer verification
only. No SQL evidence entities, HTTP endpoints, UI or review authority are enabled.

## Next implementation

1. Read07-06-PLAN and canonical phase design/relationships; use the real quote
   evidence/referral services as analogues without reopening or approving source
   quote associations. Existing shared pure/file-screening logic is reusable.
2. Add servicing-owned immutable file bytes/metadata, evidence associations and
   append-only review/withdrawal events. Full draft/cycle/revision/rating ownership
   keys and current-pointer FKs are required, plus immutable record guards and new
   additive migrations. Never rewrite earlier migrations.
3. Servicing decision context must hold current identity/policy scope, latest
   issued base, current revision/cycle/rating, current configuration and grants;
   command replay requires current authority. Fresh writes require draft ETag and
   editing lease. Reconstruct trusted full proposals per effective slice from
   immutable issued base + saved revision; supply these to the new pure rules.
4. Add real upload/read/attach/review/withdraw and individual/selected atomic
   referral decisions/conditions, with current dimension authority. Conditions,
   terms and carrier proof are additional scoped requirement families, not yet
   handled by this base-risk prerequisite. Approval cannot waive missing proof.
5. Add strict contracts, actual routes/DI and UI with scoped file access; test
   cross-owner IDs, wrong purpose/item, withdrawn/stale evidence, grant revocation
   before replay, insufficient dimensions and selected decision rollback. Finish
   both-product browser journeys with actual persisted files/reviews/decisions.

Actual preview remains API5087/web3100 from07-05; owned PIDs are recorded in
.local/phase7-05-preview-pids.json. The new pure prerequisite has not been rebuilt
into that running Release binary, and exposes no route. No preview refresh needed
until its consuming service exists. Preservefrontend-code and the demo database.
Native SQL2022 tests use isolated databases; never reset the demonstration data.

Source coverage:07-05 verified its three rating actions. The broad393-field
extraction includes downstream review composites;07-06 owns actual referrals,
requirements and evidence. Keep later terms/acceptance/financial/document items
with their approved owners and retain final07-16 completeness audit.
