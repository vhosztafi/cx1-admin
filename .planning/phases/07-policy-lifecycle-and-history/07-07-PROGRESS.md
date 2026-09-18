# 07-07 progress — capacity lifecycle (incomplete)

## Verified prerequisites

2026-09-18. Sequential inline execution, no agents. Prior07-06 is complete and
committed in3d8111a; state/roadmap reconciled in a0ea297. Phase7 remains6/16 and
55/65 implementation plans; do not mark POL-04 or07-07 complete.

60fd66e adds ServicingCapacityRules with exact draft/revision/cycle/rating/case/
referral/provider/submission/hash identity, exact current response/state/target,
full exposure interval and withdrawal/reopen/late/duplicate delivery fences.
CapacityRules.Covers extracts unchanged subject-neutral extent arithmetic for both
subjects; no fake QuoteId. These are prerequisite predicates, not service authority.
13 new servicing cases plus6 retained quote capacity cases passed (19 total):
.local/phase7-07-domain-green/unit.trx. Initial domain red was compilation failure
for missing types, not a claimed successful executable behavior-red test.
Full backend unit regression passed772/772,zero skips in
.local/phase7-07-domain-regression/unit.trx. No full integration suite is claimed.

6f06144 adds ServicingCapacityCase, dedicated model partial, and additive migration
20260918045829_ServicingCapacityCaseStorage. Compound referral ownership, unique
referral, current unexpired rating and pinned active provider guards, immutable
source/reason and deletion prohibition are enforced in SQL. Initial storage only
permits draft/superseded states; further owned submission/response migration must
replace narrow state guard before any queued/approved lifecycle can be exposed.
No public capacity API or UI is activated by this prerequisite.

Real SQL red: both products fail at missing ServicingCapacityCase before migration
(.local/phase7-07-case-storage-red.log and TRX). Green: both products pass after
additive down/up against existing issued/rated graph, negative compound owner,
wrong binder/provider, premature approved state, actor/reason, expiry/creation-time,
duplicate case, mutation/deletion and superseded-reactivation assertions. Issued
snapshot remains unchanged. SQL fixtures are generated via actual rating with
10000 tools limit; no fabricated referral rows.

Final result gate passed21 cases including2SQL,zero skips,cutoff
2026-09-18T04:58:00Z, using.local/phase7-07-case-storage-green (unit/sql subdirs).
Logs .local/phase7-07-case-storage-green.log and phase7-07-case-storage-unit.log.
The19 unit cases overlap the full772 and are not added to that total. EF model
snapshot matches after migration. git diff --check passed. Migration generation
required existing dotnet-ef outside restricted tool-home access; no restore/install
or database update was performed by generation.

## Next required work

1. Complete servicing capacity submission/message/response graph, compound current
   pointers, reviewed supplied-response evidence purpose and condition/resolution
   ownership. Use additive migrations; do not rewrite committed case migration.
2. Commands/worker: scoped create/submit/chase/query reply/supplied response,
   withdraw/reopen/senior assignment; provider operation/inbox/attempt persistence,
   exact immutable response and late/revoked/current-cycle fences. Current scope
   and grants before replay. New submission invalidates previous applicability.
3. Integrate actual capacity extent/current carrier conditions into referral
   authority and readiness without bypassing independent proof or actor grants.
4. Strict scoped API/DTO/OpenAPI and real UI/history/parent links/similar cases;
   exact retry/recovery, both-product SQL/HTTP/browser verification and source
   coverage review. Only then write07-07-SUMMARY and advance07-08.

Source controls remain unverified for07-07; no browser/HTTP capacity result is
claimed. Review07-07-PLAN,07-DATA-API-DESIGN refinement, existing quote capacity
analogues and exact servicing condition/evidence records before extending them.
Proposed filenames refined into feature-specific persistence/model/rules files,
consistent with prior servicing modules. All code is committed. No tests running.
Demo DB/preview were not restarted or migrated for this storage prerequisite:
preview still serves07-06, owned PIDs in.local/phase7-06-preview-pids.json. Verify
process identity before stopping. No reset/reseed; frontend-code unchanged.
Human UAT,hostedCI,Docker remain unperformed.

## Capacity creation command verified — 2026-09-18

Production commit f5585cc implements ServicingCapacityService.CreateAsync over
existing immutable case storage. Provider derives from pinned binder; callers
cannot choose another provider. Hold current policy/draft/cycle/rating/base,
current underwriting-escalate role and effective same-product/binder grant,
current lease and active provider before receipt lookup. A new command additionally
requires exact draft and referral rowversions, one case per referral and non-declined
current referral. Atomic case/audit/receipt advances draft ETag. Escalation records
a draft case only; it never grants excess cover, sends a provider request or issues.
No HTTP route/DI/UI has been activated by this prerequisite service.

Tests are in ServicingCapacityCommandTests.cs, invoked by the two capacity-create
scenarios in ServicingRatingRequestTests.cs, using actual rating/referral generation.
Both products pass: capability/missing parent/cycle/referral, stale parent/child,
wrong lease, inactive provider, persisted provider/rating ownership, exact replay,
duplicate new key, changed-key intent, one audit, expired lease, removed current
role and revoked effective grant before replay. Issued snapshot is unchanged.

Final evidence .local/phase7-07-create-reviewed/sql/sql.trx and unit/unit.trx:
2 real SQL scenarios +19 focused unit cases,21total,zero skips. Result gate passed
with cutoff2026-09-18T05:12:00Z. Log.local/phase7-07-create-reviewed.log.
No full SQL or new browser result is claimed. Earlier772-unit baseline unchanged.
Initial red .local/phase7-07-create-red.log is missing-service compilation failure,
not an executable behavior-red pass. First SQL run .local/phase7-07-create-green.log
failed at the test fixture's incomplete UserAuthorityGrant revocation: mandatory
RevokedBy/RevocationReason absent. Fixture corrected using the established audited
revocation pattern; no production guard weakened. Failed run is not counted.

NEXT: submission/message/response graph, current pointers, reviewed supplied
response evidence and conditions; service/worker and final HTTP/UI/browser work
remain as listed above. Preserve f5585cc creation behavior when extracting shared
held-capacity authorization. No plan summary/completion: still6/16,55/65, POL-04open.
No active tests or browser sessions. Demo database/preview not changed by this
checkpoint; no reset/reseed or frontend-code edits. git diff --check passed.

## Submission storage verified — continuing inline, 2026-09-18

User explicitly corrected premature10–15minute stops. Intermediate commits are
checkpoints, not stopping points. Continue execution through context compaction;
only stop for verified phase completion or a concrete blocker. No new permission
is required. Sequential inline/no agents remains the approved plan.

1b02fd4 adds ServicingCapacitySubmission and current owned submission pointer.
Migration20260918055242 is additive; compound case ownership, UTF-8 SHA256 request
hash, unique case sequence/work, pinned outbox/scenario, current rating/referral/
provider source guard, append-only history and monotonic pointers verified. Draft
withdrawal retains pointer; same submission cannot requeue. No carrier approval
state is permitted without future response graph. Body has explicit SQL20000-byte
limit because EF nvarchar(max) would not enforce MaxLength10000 alone.

Real SQL red: both products fail on missing submission table. Initial6SQL pass,
then final body-limit review prompted new assertions/run. Final25tests=19unit+
6SQL pass,zero skips,cutoff2026-09-18T05:54:00Z in
.local/phase7-07-submission-storage-reviewed. Includes both products each for
capacity-storage,capacity-create,capacity-submission-storage; existing migration
down/up and immutable issued snapshot preserved. No shared demo upgrade/reset.

NEXT ACTIVE: selected-evidence storage red tests currently running, then owned
immutable association/review links plus queue manifest completeness. See new
capacity-selected-evidence scenarios and ServicingCapacitySubmissionStorageTests.
These are uncommitted until verified. After that implement submission command,
responses/conditions/worker, HTTP/UI/source review.07-07 remains incomplete.
