# 07-07 progress — capacity lifecycle (complete; chronology retained)

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

## Submission and withdrawal commands verified — continuing inline, 2026-09-18

234d363 adds selected reviewed evidence links, immutable request manifests and
atomic SubmitAsync with pinned fictional scenario/outbox/audit/receipt. Exact
current role, grant, lease, provider and selected proof precede receipt replay.
29 checks (19 unit +10 real SQL) passed with no skips in
.local/phase7-07-submit-reviewed, cutoff2026-09-18T06:12:00Z.

Withdrawal now uses ActionAsync instead of raw SQL in both product tests, retains
the submission pointer/history and validates state, ETags, lease and authority.
New response rules validate closed typed limits, exact dimension, UTC reply time
and each condition against its explicitly selected dated risks.25 checks (23 unit
+2 real SQL) passed in.local/phase7-07-actions-reviewed, cutoff06:23UTC.
The first action test was compile-red for missing method; one green attempt hit
sandbox SQL encryption access and was rerun successfully with local escalation.
Response rule tests were compile-red for missing types, then4 new tests passed.
Reopen implementation exists but successful response-state reopening awaits the
response storage/worker graph. No endpoint/UI activation or completion claim.

NEXT: persistent capacity correspondence/response graph, supplied-response proof,
carrier conditions/resolution, deterministic servicing worker, assignment/chase,
read models and actual HTTP/UI verification.07-07 remains incomplete. Continue
without ending at this checkpoint. Shared demo DB and frontend-code unchanged.

## Correspondence verified — continuing inline, 2026-09-18

1d33855 contains withdrawal and response parsing. ce2906c contains immutable
staff correspondence, atomic submission message and scoped ChaseAsync with exact
receipt retries. Separate response records will carry inbound authority/proof;
staff messages cannot grant extent. Migration20260918063405 is additive.
36 checks passed (30 unit +6 real SQL), no skips, in
.local/phase7-07-correspondence-reviewed, cutoff06:35UTC. SQL first failed on
missing message table; an initial immutability assertion hit the SQL check before
the trigger and was refined to isolate the trigger. Final six SQL scenarios
cover both products for submission storage, selected evidence and submit/chase.

CURRENT ACTIVE: response evidence ownership migration/service edits in progress.
Both-product red in.local/phase7-07-response-proof-red fails because no capacity
response purpose exists. New ServicingCapacityResponseProofTests exercises that
purpose through actual upload, association and review; accepted evidence alone
must leave case queued. This is uncommitted until its migration and tests pass.
No demo DB upgrade/reset, no frontend-code edits, no07-07 completion yet.

## Supplied-response proof verified — continuing inline, 2026-09-18

c18d362 adds compound ServicingEvidenceAssociation.CapacitySubmissionId ownership,
closed capacity purpose, current submission insert guard and immutable ownership.
The shared upload/attach/review/history flow derives submission identity from the
current server fingerprint; the client cannot nominate it. Pure and frontend
matching require both exact fingerprint and explicit submission identity.
Migration20260918064844 is additive. Existing evidence UI types and generated
OpenAPI now expose the nullable source link; capacity endpoints remain closed.

Both-product real red lacked capacity response requirements. SQL green2 then
reviewed6 (capacity submit/storage including down/up, existing evidence review)
passed in.local/phase7-07-response-proof-reviewed. A final pure red caught missing
explicit source matching. Final30unit+2SQL=32 no skips pass in
.local/phase7-07-response-proof-final, cutoff06:59UTC. Six frontend proof tests,
41 API contract tests and web:typecheck passed. Source logs retain actual red
and green attempts. Initial/overlapping result totals are not additive.

NEXT ACTIVE: ServicingCapacityResponse storage tests now fail on missing response
table for both products (.local/phase7-07-response-storage-red). Response record,
model and source/history guards are being implemented; migration generation is
in flight. Tests live in ServicingCapacityResponseStorageTests and are called by
capacity response proof helper after its accepted review. Separate immutable
staff correspondence and inbound response records are intentional. Conditional
case state remains closed until carrier condition/resolution graph exists.
Continue inline through remaining response/condition/worker/API/UI work. Do not
stop at this checkpoint or mark07-07 complete. Shared demoDB unchanged.

## Carrier responses verified — continuing inline, 2026-09-18

f8b1cae adds immutable ServicingCapacityResponse storage, exact supplied proof or
persistent demo-operation/inbox provenance and same-submission current-response
FK. Case state must match the latest applicable response; withdrawal retains the
pointer, requeue requires a new submission and clears response applicability.
Late provider results retain superseded history and cannot reactivate a draft.
Demo payload definition/body/outcome/time and inbox hash must match stored result.
Migration20260918070922 is additive and down/up tested.

Initial real red: missing response table. First migration run exposed collation
mismatch between binary inbox EventId and response field; explicit BIN2 comparison
fixed it. Final36checks=30unit+6SQL,zero skips, passed in
.local/phase7-07-response-storage-reviewed, cutoff07:16UTC. Includes both products
for capacity submit with response proof/query/late provider, capacity storage with
down/up, and submission storage regressions. No worker or response route yet.

CURRENT: carrier condition storage tests first failed on missing table in
.local/phase7-07-carrier-conditions-red. New migration20260918072520/model/guards
are uncommitted while4SQL green tests run. These pin every carrier condition and
its ordered dates to the exact response manifest before conditional activation.
ServicingCapacityConditionStorageTests is nested in existing capacity-submit test.
Response storage test now expects query+conditional+late response (3 records).
Conditional state alone does not mean resolution/issue readiness. Still needed:
carrier condition proof projection/resolution; response command; worker; query
reply/senior assignment; HTTP/read models/UI/browser; remaining07-08..16. Continue
without a final checkpoint response. Demo DB and frontend-code unchanged.

## Continued execution: carrier conditions, resolutions and supplied commands

9979aa8 committed immutable carrier conditions and current proof projection.
Final 30 unit +4 SQL passed in .local/phase7-07-carrier-conditions-reviewed
(cutoff07:30UTC); initial red confirmed missing trading-history projection.
Resolution storage red2 failed on missing table, green2 passed in
.local/phase7-07-carrier-resolution-green/sql. Additive migration
20260918073955 adds exact condition/response/submission/evidence-review/grant
ownership and append-only resolution history. Explicit resolution service tests
were written next and failed compilation on missing methods, then implemented.
Current .local/phase7-07-resolution-command-green run is pending.

Supplied response command tests first failed compilation on missing command;
then2 SQL passed in .local/phase7-07-supplied-command-green2/sql. This replaces
the formerly raw valid query insertion with actual command plus exact retry.
New negative role and expired-lease retry assertions added after that run require
fresh verification. Carrier resolution/readiness tests cover upload, review
without resolution, explicit resolution, withdrawal and denied stale retry.
No new HTTP endpoints, worker or capacity UI yet;07-07 remains incomplete.
Continue inline rather than stopping at a checkpoint. Counts remain55/65plans.

2b5de00 commits supplied-response commands and explicit carrier resolutions.
Fresh reviewed evidence:25 capacity unit+4SQL=.local/phase7-07-response-resolution-reviewed,
29passed,zero skipped; assert-test-results passed with cutoff07:50UTC. Includes
both products capacity-submit chain and migration down/up capacity-storage.
Whitespace-only EOF findings from diff-check corrected after commit; rerun pending.
Current worker tests first failed compilation because worker absent; implementing
servicing-specific durable provider/inbox worker plus DI dispatcher. No HTTP/UI
claimed. Conditional-security demo scenario still queries pending exact dated
condition projection implementation. Worker test result pending.

## Durable worker and routing verified — continuing,2026-09-18

b806946 commits servicing capacity worker/provider/inbox, current identity/grants,
selected-proof checks, duplicate quarantine, late-result supersession, crash
recovery, dispatcher registration and claimable servicing-capacity job kind.
Query replies atomically retain old-query correspondence and create a new
submission; senior assignment revalidates eligibility even before retry and
changes routing without granting authority. Dated selected-premises projection
supports conditional-security responses; missing/inapplicable targets query.

Initial worker4SQL:3passed,revocation fixture failed SQL timestamp constraint;
corrected fixture timestamp. Conditional red first missed binder trigger at
120000;150000 produced actual red expectedconditional/actualqueried. SevenSQL
then exposed exact JSON date encoding mismatch; preserving validated manifest
raw date JSON fixed it without weakening guards. SevenSQL all passed in
.local/phase7-07-worker-routing-green/sql. Final change ensures approval starts
no later than now even for future servicing.26checks=25unit+1SQL conditional
passed .local/phase7-07-worker-final, cutoff08:10UTC; no skips. Diff-check clean.

Current work: integrate current exact carrier extent with referral authority and
readiness. New two-product capacity-submit red expects outstanding-condition
blocker but gets dimension-authority-required. ServicingCapacityAuthority now
implemented, .local/phase7-07-capacity-authority-green pending. No capacity HTTP
routes/read models/UI/browser yet. Counts6/16Phase7,55/65plans remain unchanged.

1265735 commits carrier authority/readiness integration. Two-product red showed
missing outstanding-capacity check; green passed; final57checks=49unit+8SQL in
.local/phase7-07-capacity-authority-reviewed (cutoff08:18UTC) passed,zero skips.
Current grants/binder unchanged; every dated blocker needs exact current extent.
Stored selected and response proof remain current, conditions require resolution;
referral readiness and retry fail after condition proof withdrawal. Existing
referral-service/authority/trading-proof regressions passed for both products.

Capacity read services: initial build red missing readmodel. Five SQL worker
scenarios plus25unit passed .local/phase7-07-capacity-reads-green, cutoff08:25UTC.
Details expose current grant/lease capability separately from immutable history;
read-only context avoids upgrading policy locks. Bounded pages retain messages,
responses, submissions and resolutions; similar cases limited to same already-
authorized policy/provider/binder/rule. HTTP routes still absent; new capacity-http
scenarios currently running red in .local/phase7-07-capacity-http-red.

1265735 and250b517 now commit authority/read models respectively. fd3a23f commits
HTTP routes and strict OpenAPI contracts. Both Motor Trade API scenarios create,
submit/retry, chase, assign, process query, reply/new submission, process a second
query, record reviewed supplied conditional response, explicitly resolve reviewed
condition proof, page immutable history and reopen while preserving response.
CSRF, unknown fields, stale versions, unknown proof/owner and invalidated cursors
are negative-tested. Initial route red404, then assertions corrected to existing
400 conventions for unknown fields and invalid signed cursors. Final27checks=
25unit+2SQL passed .local/phase7-07-capacity-http-final, cutoff09:00UTC; no skips.
All38 emitted HTTP responses validate closed actual schemas via capacity mode in
verify-servicing-proof-contracts.mjs.42 API contract tests pass; OpenAPI400
operations lint passes with34 unused-component warnings. No browser pass claimed.

UI in progress: case workspace embedded in persisted referral work, bounded
correspondence/history, submit/query/chase, supplied dated responses, reviewed
condition resolution, authority context/similar cases, assignment/withdraw/reopen.
Shared immutable proof-command retry journal extended with carrier routes;
new route-negative test failed before implementation, then7 proof tests passed.
TypeScript and production build pass. Actual browser harness added for both
products. First run stopped at login: build-time rewrite pointed to5080 rather
than owned5087 API. Rebuilding with explicit BACKOFFICE_API_ORIGIN; not an app
authentication or carrier failure. Additive migrations applied to preserved demo
through20260918073955, no reset/reseed. New owned preview IDs recorded in
.local/phase7-07-preview-pids.json. Continue browser verification, source review,
remaining gaps and then07-08 onward; do not end the turn at this checkpoint.

Final91ed93e commits UI and scoped referral discovery. Both browser journeys
passed again on final code09:25:52UTC in.local/phase7-07-capacity-browser-final.log
and.local/browser-evidence/servicing-capacity/report.json. Actual query/reply,
supplied conditional response, reviewed proof, explicit resolution and reopen
preserve issued snapshots. Final desktop/390px screenshots inspected.

Review found unfiltered historical case pagination could suppress a new request.
Exact optional referralId filter is server-scoped and cursor-bound. One SQL red
expected200/actual400, then final27checks=25unit+2SQL passed in
.local/phase7-07-capacity-filter-green, cutoff09:18UTC; no skips.38 actual bodies
validate.52 API/frontend tests pass; full web lint/typecheck/API+Next builds pass.
A SQL-only result gate correctly refused missing unit TRX; a fresh unit run
provided the required report, then the27-case gate passed. See07-07-SUMMARY.md.
Advance to7/16Phase7,56/65plans; POL-04 stays open. Continue07-08 inline.
