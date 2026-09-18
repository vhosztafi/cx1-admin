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


## Verified immutable file storage checkpoint:cf3511b

ServicingEvidenceFile now stores draft-owned immutable bytes, bounded filename/media,
byte length, SHA256, uploader and UTC creation time. Screening is explicitly the
existing demo-signature-v1 outcome; it is separate from underwriting acceptance.
Alternate key(Id,DraftId) enables later association ownership FKs without borrowing
source-quote associations. No public storage locator exists.

New additive migration20260917192246_ServicingEvidenceFiles enforces actual content
length1..10MiB, matching binary-collated lowercase SHA256, supported media, safe
non-path name, existing uploader/draft and creation no earlier than the draft.
Append-only SQL trigger blocks updates/deletes (51310); time guard uses51311.
EF disables SQL OUTPUT for the triggered table. Prior migrations are unchanged.

Failing-first .local/phase7-06-files-red/sql/sql.trx has2 real failures on the absent
file table. Final .local/phase7-06-files-reviewed/{unit,sql} passes8 unit and2SQL
cases, no skips. Assertion gate verified10 with cutoff2026-09-17T19:18:00Z.
SQL tests cover both products, additive upgrade over an already issued graph,
valid bytes/hash readback, absent draft/uploader, path/media/length/hash/screening
rejection, early timestamps, immutable update/delete and unchanged issued JSON.
Debug API builds without warnings/errors; git diff --check passes.

Next: associations, append-only review/withdrawal events, full compound
cycle/revision/rating ownership, servicing decision context and actual services.
File upload/read authorization and actual signature screening must use the existing
QuoteEvidenceRules.File analogue at runtime; storage checks alone do not screen
arbitrary content. Align the servicing200-character contract with the shared
file helper's current150-character limit explicitly when exposing uploads.
No file API, review UI, association approval or whole-plan completion is claimed.
No demo migration/seed or preview restart was performed in this storage checkpoint.

EF tooling: DOTNET_CLI_HOME=.local/dotnet; build Debug --no-restore first, then
use dotnet ef migrations add ... --project backend/src/BackOffice.Infrastructure
--startup-project backend/src/BackOffice.Infrastructure --output-dir Persistence/Migrations
--no-build. The API startup lacks EF.Design, so it is not the tooling startup.
Earlier tooling startup/build failures are retained in logs, not passing evidence.


## Verified association storage checkpoint: 2e4df59

Added servicing-owned evidence associations and append-only review/withdrawal
history with full draft/cycle/revision/rating/fingerprint ownership. SQL requires
an owned file, an actual cumulative driver or selected premises target, current
rated revision at association time, ordered events and matching authority product/
binder. Review and withdrawal pointers cannot cross associations; latest review
cannot roll back and withdrawal cannot be cleared. Service-level grants, exact
requirement fingerprints and command authorization remain separate checks.

Additive migration 20260917194034_ServicingEvidenceAssociations was tested on both
Motor Trade products, including downgrade/upgrade of isolated databases containing
issued policies. The shared demo database and preview were not changed.

Final .local/phase7-06-associations-final/{unit,sql} contains 14 unit and 4 actual
SQL cases, no skips; assertion gate verified 18. Tests include a real file owned
by another cancellation draft, incorrect purposes/targets, sequence/fingerprint
errors, cross-association pointers, old acceptance rollback, irreversible
withdrawal, immutable events and unchanged issued JSON. Initial storage RED had
2 absent-table failures. The first expanded review run had 2 test-setup failures
because a second active adjustment is forbidden; using a distinct cancellation
draft respects that invariant and the final suite passed. git diff --check passed.

Next work underway: scoped servicing file upload/download service tests. No new
HTTP route or browser evidence behavior is enabled yet. 07-06 remains incomplete.


## Verified file-service checkpoint: 54d0c46

ServicingEvidenceService.UploadAsync now retains exact screened file bytes under
current identity/policy/rating/configuration scope, fresh draft ETag and actor-bound
editing lease. ServicingDecisionContext holds that boundary and advances the draft
ETag with the write. SQL command receipts reauthorize before replay; the identical
command returns the original file ID and changed bytes cannot reuse its key.
DownloadAsync separately holds current policy read scope and exact draft/file
ownership, allowing historical downloads after rating expiry without an edit lease.
No file read or upload grants an evidence acceptance.

Shared signature screening now accepts an explicitly bounded maximum filename
length; quote callers keep the existing 150-character default and servicing uses
the planned 200-character limit. Unit tests verify both boundaries; SQL service
tests persist a 200-character filename and reject 201 characters/spoofed media.

Failing-first .local/phase7-06-file-service-red/sql/sql.trx contains 2 genuine failing
SQL service tests before implementation. Final .local/phase7-06-file-service-green
contains 15 unit and 2 real-SQL cases with no skips. Assertion gate passed 17 cases
with cutoff 2026-09-17T19:50:00Z. Both products verify ETag/lease denial, exact replay,
changed-payload conflict, persisted bytes/screening, real cross-draft file denial,
expired-rating write denial, historical download and revoked-role denial before
receipt replay/download. Issued JSON remains unchanged; no association is fabricated.

This is a service prerequisite, not HTTP/browser completion. It is not registered
in DI or routed yet. Next: extend the held context with current underwriting grants
and full immutable cumulative risk slices, implement association/review/withdrawal
services and referral/condition graph, then strict DTOs/DI/routes/generated contracts
and actual UI/browser acceptance. Existing Source quote grants/associations are not
servicing approval. No whole-plan summary or completion is claimed. Phase7 stays
5/16 and overall plans 54/65. Shared demo and running 07-05 previews remain unchanged.


## Verified base-risk evidence commands: 434289c

ServicingEvidenceProjection reconstructs full cumulative proposals from the immutable
issued base and saved revision at the retained rating instant. It checks source
hashes, dated slice counts and exact underwriting projections against the immutable
rating input before deriving proof purposes/fingerprints with current capture pins.
It does not use the projected pricing subset as if it were the full risk document.

ServicingEvidenceService now supports current requirements, attach, accepted/rejected
review and withdrawal. Commands hold current source/policy/draft/cycle scope and
require exact fresh draft/evidence ETags and actor-owned editing lease. Review holds
current effective same-product/binder grants before receipt replay; trading-history
proof additionally checks the reviewTradingHistory grant. Proof review is distinct
from risk approval: full referral dimension authority remains with the unfinished
referral service and cannot be inferred from an accepted file.

Requirements readback requires current matching context, accepted screening, latest
accepted review and no withdrawal. Unreviewed attachments do not satisfy proof.
Rejection removes satisfaction; independent rereview restores it, while withdrawal
is irreversible for that association. Expired rating sets Applicable=false and
cannot expose satisfied proof as current. Every event is persisted separately;
source quote approval and issued policy bytes remain untouched.

RED: .local/phase7-06-review-service-red/sql/sql.trx has 2 failing cases before
implementation. The first implementation run reached grant revocation but the test
used a fixture time before seeded grant CreatedAt; it was corrected to the existing
revocation-test convention without changing production constraints. Final reviewed
.local/phase7-06-review-service-reviewed/{unit,sql} passes 15 unit and 2 real-SQL cases
on both Motor Trade products. Assertion gate verified 17, cutoff2026-09-17T20:09:00Z,
no skips. Intermediate .local/phase7-06-review-service-final/sql passed 4 cases,
including prior upload/download service regression; final additional checks cover
stale evidence ETag, rejection/rereview and capture-pin use. Do not double-count
repeated runs. git diff --check passed; Debug build has no new warnings/errors.

SQL service cases verify cumulative two-date purposes, source-quote proof not reused,
actual cross-draft file denial, cross-draft cycle denial, wrong target/fingerprint,
unauthorized review, lease takeover, exact accepted-review replay, independent
missing requirements, expiry, accepted/rejected/rereview/withdrawal readback,
withdrawn reactivation denial, revoked grant before replay, exact event count and
unchanged issued snapshot. The existing service fixture is extended with two
'evidence-review' cases, with detailed assertions in ServicingEvidenceReviewTests.cs.

Remaining07-06: referral/current-decision and condition storage/services, full
current dimension authority and selected all-or-none decisions; condition evidence
purposes and resolution; terms/acceptance applicability integration with downstream
owners; bounded evidence/history metadata read models; strict DTOs/DI/routes and
contract generation; actual evidence/referral UI and both-product browser checks.
New services are not yet registered/routed; no endpoint runtime status or source
coverage is marked complete. No shared demo migration or preview restart occurred.
Phase7 remains5/16, milestone54/65 plans. Continue inline without agents.


## Verified cumulative referral-rule prerequisite: 0f0a88a

ServicingReferralRules.Assess groups stable rule/dimension/item needs while retaining
each effective date, originating binder/authority/source rule, requested amount and
specific limit. Transient earlier risks remain visible even when the final slice
removes the driver or reduces the exposure. Annual risk inputs are explicit; callers
must join immutable rated annual prices to exact dated input slices, never use net
movement/return premium as annual authority exposure.

AuthorityAllows evaluates every dated risk against one candidate grant and binder.
It does not combine grants across dates or dimensions, accept a grant exceeding the
binder, infer unnamed-driver licence experience from ages, remove independent source
referrals or supply evidence/approval. Ordered UTC dates inside the term, bounded
schedule/identities/triggers and same-product typed configuration are mandatory.
Current identity, effective grants, SQL ownership and decisions remain service duties.

RED .local/phase7-06-referral-rules-red/unit.trx has6 failing cases before behavior.
Reviewed .local/phase7-06-referral-rules-reviewed/unit.trx passes17 cases (7new,
10shared authority regressions), no skips. Both-product monetary triggers, earlier
removed drivers, separate source review, disjoint grants, binder ceilings, unknown
unnamed experience and malformed schedule/configuration are covered. git diff
--check passes. This change is pure; no SQL migration/service/API/UI behavior was
added and no SQL execution or browser acceptance is claimed for it.

Next: persistent servicing referrals and append-only decisions, with compound
DraftId/CycleId/RevisionId/RatingId ownership and same-referral current pointers.
Populate requirements through the cumulative rule output, storing dated triggers
rather than flattening to the final risk. Add condition/resolution ownership before
allowing conditional approval. Test fresh/current grants and selected atomic writes,
then complete bounded read models, HTTP contracts/DI/UI/browser work. Keep07-06
in progress, with Phase7 still5/16 and54/65 total plans. Demo/preview untouched.


## Verified referral/decision storage checkpoint: fdc39a2

ServicingReferral and ServicingReferralDecision now use full draft/cycle/revision/
rating ownership, unique rule/dimension/stable target and sequence, immutable dated
trigger provenance and same-referral current-decision pointers. Decisions retain
explicit GrantId, ActorId and AuthorityVersionId through a compound foreign key to
an additive UserAuthorityGrant alternate key. A decision cannot borrow another
actor's grant. Source guards require effective unrevoked same-product/binder grants;
current role/dimension checks and command replay remain service responsibilities.

Additive20260917205247_ServicingReferrals adds two tables and the grant alternate key.
Four SQL triggers enforce current rated source on creation, exact trigger dates/
rule/dimension/driver target, ordered append-only decisions, matching latest outcome
and pointer, immutable provenance, and no reactivation of superseded referrals.
Conditional/query decision storage requires nonempty condition JSON; query also
requires a question. Actual typed condition rows/resolution and conditional approval
service are still pending, so this storage does not enable any approval endpoint.

SQL RED .local/phase7-06-referral-storage-red/sql/sql.trx has2 absent-table failures.
The first implementation run exposed a test assumption equating the reviewer's
specific grant with the cycle's baseline authority version. The test now selects
the actual same-product/binder actor grant, preserving that necessary distinction.
Reviewed .local/phase7-06-referral-storage-reviewed/{unit,sql} passes17 unit and4 real
SQL cases. Assertion gate verified21 with cutoff2026-09-17T20:48:00Z, no skips.
Both products cover compound owner mismatches, duplicate referral identity, wrong
trigger rule/foreign driver, unused-row deletion protection, cross-referral pointer,
existing other-user grant denial, ordered decisions, empty conditional/query denial,
old acceptance rollback, immutable history and supersession. The two file-storage
cases also exercise downgrade/upgrade of all new migrations over an issued graph;
issued JSON remains unchanged. Debug build and git diff --check pass.

Next: populate referrals from cumulative pure requirements (save current cycle/rating
pointers before inserting referrals to meet the SQL guard); add condition/resolution
entities with full ownership, then individual/selected decision services with current
per-dimension authority, atomic rollback and grant-revocation-before-replay tests.
Do not equate a baseline AuthorityVersionId with an actor's eligible grant version.
Retain grant coverage across the remaining dated term. Add evidence condition
purposes only after their owning condition graph exists. Finish bounded read models,
strict HTTP/DI/contracts, UI and both-product browser acceptance before closing07-06.
Shared demo and running previews remain unchanged; no browser/UAT result claimed.
Phase7 remains5/16; total plans54/65. Continue inline with no agents.


## Verified automatic referral generation checkpoint: 3bea283

The servicing worker now creates referrals atomically with an applicable successful
rating. It matches all immutable cumulative input dates/change IDs to their annual
rated risks and assesses binder, baseline authority and independent source triggers.
Temporary cover removed by a later change still creates the required dated referral.
Current cycle/rating pointers are saved before referral insertion inside the same
transaction, satisfying the SQL provenance guards. Rejected, revoked, superseded and
duplicate worker deliveries cannot create applicable referrals. Draft edits, rerating
and abandonment supersede old-cycle referrals while retaining decision pointers.

RED .local/phase7-06-referral-generation-red/sql/sql.trx contains the two expected
missing-referral failures after fixing a test-local variable name. The first broad
run revealed an incorrect new coverage-end assertion and was interrupted; it is not
passing evidence. Each existing price movement earns through term end, not merely
until the next change. The validation now follows that retained pricing contract.

Fresh .local/phase7-06-referral-generation-reviewed/{unit,sql} passes15 pure/shared
unit and19 real-SQL scenarios, with no skips. assert-test-results.ps1 verified34
cases with cutoff2026-09-17T21:18:00Z. Both products cover temporary above-authority
cover, exact dated binder/authority amounts, superseded worker exclusion, duplicate
application and edit supersession. The broader SQL run also passes rating retries,
revoked access, rejection, configuration change, evidence storage/upload/review and
referral storage. Existing raw storage tests allocate after generated sequences.
git diff --check passes. No shared demo migration or browser acceptance was run.

Next: add owned condition/resolution storage and individual/selected decision
services. Use current actor grants across every cumulative dated risk, retain actual
grant identity and enforce all-or-none writes and revocation before replay. Reuse
closed ReferralRules condition semantics carefully: stable targets may be present
only in earlier slices, and terms-specific evidence depends on the later terms
slice. Complete bounded read models, HTTP/DI/contracts, UI and persisted browser
acceptance before closing07-06. No new decision endpoint is enabled by this change.
Phase7 remains5/16 complete and54/65 total plans. Continue inline with no agents.


## Verified dated condition authority prerequisite: 4128171

Added a pure AuthorityAllows overload consuming ServicingConditionSlice values.
It checks every cumulative annual risk with the existing closed ReferralRules
warranty semantics, applying each condition only on its exact retained risk date.
An any-driver licence warranty must cover every affected date and both authority
and binder minima; it cannot waive stock, premium, age or other dimensions. A
single eligible grant must still stay within the binder. Documentary proof alone
never substitutes for a warranty, and this predicate neither accepts evidence nor
resolves independent source referrals. It does not fabricate unnamed-driver facts.

The entire input risk/configuration schedule is validated before evaluating results.
Condition dates must be unique, UTC and present in that schedule, with nonnull rows
and typed conditions. Bounds permit100 active conditions at each of100 dates: a
condition applicable throughout the term is not counted as100 separate decisions.
Callers MUST parse each definition with ReferralRules.Condition against its dated
proposal and verify current ownership/active decision status before passing it here.
This pure overload is a prerequisite, not an exposed approval service.

RED .local/phase7-06-condition-authority-red/unit.trx has5 expected stub failures
(after correcting a test fixture's use of init-only risk properties). Reviewed
.local/phase7-06-condition-authority-reviewed/unit.trx passes27 cases:7 new dated
condition cases plus20 existing referral/shared authority/condition cases, no skips.
Tests cover both products, incomplete date coverage, independent risk dimensions,
higher licence minima, documentary proof, malformed later risk, foreign/duplicate/
non-UTC dates, null/oversized conditions, over-binder grants and a100-date schedule.
This change touches pure code only; no SQL migration, service, API or browser result
is claimed. git diff --check passes. Prior runtime SQL evidence remains recorded
above; it was not copied into this pure test run.

Next remains owned condition/resolution storage and current-grant decision services,
then HTTP/UI integration and browser verification. Integrate this overload only with
validated per-date condition projections; do not flatten final risk or expand a
condition to dates where its target is absent. Phase7 remains5/16, total54/65;
07-06 remains incomplete. Continue inline with no agents.


## Verified condition storage checkpoint: f2e7be8

Additive20260917214347_ServicingConditions retains immutable condition definitions
under exact DecisionId/ReferralId/CycleId/DraftId/RevisionId/RatingId ownership.
Each row records its1..20 decision-array ordinal, closed code/kind, original JSON
and bounded applicable UTC dates. A compound decision FK prevents borrowing another
referral/rating owner. Future resolution pointers can use the full condition key.
The table has rowversion for future command preconditions, but all updates/deletes
are currently rejected; no resolution pointer or resolution service exists yet.

Source trigger51361 requires the latest decision to be conditional/query, current
owned unexpired rating, exact actor and original array definition, correct kind,
and documentary-only query conditions. Date guard51362 requires1..100 distinct UTC
instants present in retained cumulative input. Immutable guard51360 preserves rows.
Closed definition schema, stable-target applicability per date, current grants and
active-condition selection remain mandatory service checks before commands are
exposed. A code check and exact decision match alone do not establish approval.

RED .local/phase7-06-condition-storage-red/sql/sql.trx has2 absent-table failures.
The first implementation run exposed a test that used malformed JSON for a trigger
check: SQL correctly rejected it via check547 first. The corrected test uses a
valid-code altered definition and now verifies immutable trigger51360 directly.
Fresh .local/phase7-06-condition-storage-reviewed/{unit,sql} passes27 pure/shared
unit tests plus4 SQL cases. assert-test-results.ps1 verified31 cases, no skips, with
cutoff2026-09-17T21:40:00Z. Both-product tests cover foreign parents/revisions,
duplicate or absent ordinals, changed definitions, invalid/foreign/non-UTC/duplicate
dates, immutable update/delete and persisted readback. File-storage cases exercise
downgrade/reapply of the new migration over an issued graph. Issued JSON is unchanged.
Debug build and git diff --check pass. Shared demo database/previews unchanged.

Next: add condition-bound evidence ownership and immutable resolution records with
same-condition/evidence/review keys, then actual decision/resolution commands and
current-grant/atomic-selection/replay tests. Extend the condition immutable trigger
via a new migration only when adding a guarded current-resolution pointer. Parse
closed definitions against each cumulative dated proposal before using the dated
condition authority helper. HTTP/DI/read models/UI/browser checks remain pending;
07-06 stays incomplete, Phase7 stays5/16, total plans54/65. Continue inline, no agents.


## Verified condition resolution storage checkpoint: 21a1102

Additive20260917220333_ServicingConditionResolutions stores append-only resolutions
with exact condition/referral/cycle/draft/revision/rating ownership, association plus
input fingerprint, exact review event and actor/grant/version compound foreign keys.
Sequence is unique per condition. Latest resolution is selected by indexed sequence;
no mutable condition pointer is needed and condition definitions remain immutable.
This refines the earlier proposed pointer approach while preserving complete history.
An explicit resolution is the condition-to-evidence binding: merely having base proof
never resolves a condition. Current service checks must validate the full fingerprint,
condition applicability and live proof before using that resolution as satisfaction.

Trigger51370 rejects updates/deletes.51371 requires the active latest decision's
conditional/queried referral, current unexpired rated draft, exact latest nonwithdrawn
review and ordered time/sequence; satisfied requires an accepted review.51372 matches
purpose and stable documentary target.51373 checks an unrevoked effective same-product/
binder actor grant. Compound keys reject borrowed actors, reviews, associations and
condition owners. Risk-change conditions cannot resolve with proof. Warranty/signed
statement evidence remains unavailable under existing association purpose constraints;
extend it with actual owning requirements/contracts, not an unguarded success path.

RED .local/phase7-06-resolution-storage-red/sql/sql.trx has2 missing-table failures.
Initial green exposed an incomplete test revocation (missing actor/reason). The test
now uses a full grant revocation inside a rolled-back test transaction, respecting
production final-revocation semantics; it does not un-revoke a committed grant.
Fresh .local/phase7-06-resolution-storage-reviewed/{unit,sql} passes31 pure/shared
unit tests and4 SQL cases. Gate verified35 with cutoff2026-09-17T21:59:00Z, no skips.
Both products cover successful/rejected immutable resolutions, wrong purpose,
another association's real review, foreign owners/IDs, duplicate/gapped sequences,
other-actor grants, revocation, stale/rejected review and post-withdrawal rejection.
Migration downgrade/reapply over issued policies also passes. Issued snapshots and
shared demo/previews remain unchanged. Debug build and git diff --check pass.

Next: implement real decision and resolution services, closed per-date condition
projection, current-grant checks before replay, selected all-or-none decisions and
live satisfaction that rechecks latest resolution/review/withdrawal/active decision.
Add warranty acknowledgement proof under a guarded association-purpose extension;
signed statement remains tied to the later actual terms graph. Then complete bounded
read models, DI/HTTP/contracts and UI/browser verification. No approval/resolution
endpoint is enabled by this storage work.07-06 remains in progress; Phase7 stays5/16,
total plans54/65. Continue inline without agents.


## Verified closed dated condition projection prerequisite: 901d37f

ServicingConditionRules.Parse now validates a bounded ordered cumulative proposal
schedule and an exact ordered subset of applicable UTC dates, then invokes the
shared closed ReferralRules.Condition parser separately against every selected
risk. Removed drivers/premises can receive an earlier condition but cannot be
borrowed into a later date. Named-driver wording retains each date's actual names;
it does not copy final-risk wording backwards. All stable-ID collections are checked
for valid unique IDs even on unselected dates, with100 slices/100 dates/1000 items
per collection and1MiB per proposal bounds. This closes a prerequisite for safely
calling the previously added dated-condition authority helper from real services.
It does not itself choose applicable dates, validate terms ownership, authorize a
decision or resolve evidence; those remain held-scope service responsibilities.

RED .local/phase7-06-condition-projection-red/unit.trx records5 expected stub failures.
Reviewed .local/phase7-06-condition-projection-reviewed/unit.trx passes28 tests:
5 new projection cases plus23 servicing authority/referral/evidence and shared
condition cases, no skips. Tests cover earlier removed targets, changed captured
names, foreign vehicle/premises, unknown definition fields, malformed/duplicate/
foreign/non-UTC/unordered dates and malformed unselected risk. git diff --check
passes. This is pure application code; no SQL/API/UI/browser change is claimed.

Next remains actual decision/resolution services. Reconstruct trusted full proposals
from the held base/revision/rating instant (ServicingEvidenceProjection already does
this), derive and validate applicable dates server-side, then use this parser for
closed typed definitions and per-date authority. Implement current-grant checks
before replay, selected atomic decisions, live resolution/proof checks and guarded
warranty purposes before wiring APIs/UI. Keep07-06 incomplete; Phase7 stays5/16 and
54/65 total plans. Continue inline without agents.


## Verified initial referral command service checkpoint: b465dfc

ServicingReferralService.DecideAsync executes bounded single/selected decisions
through SqlCommandBoundary with current identity, owned rated context and effective
actor grants checked before receipt replay. It holds all selected referrals, checks
all rowversions and the editing lease before writes, persists decisions/conditions
atomically and returns the new draft ETag. Exact retries reuse the receipt; revoked
actors cannot replay. Approvals assess annual risk across all retained dated slices
using one eligible grant and parsed applicable conditions. Above-binder temporary
cover remains unapprovable even when later removed. Decline/reopen retain history.

ServicingEvidenceProjection now exposes its existing verified cumulative proposal
reconstruction as Slices. Conditions use closed parsing against server-derived
target-presence dates; named-driver wording is parsed per date. JSON whitespace is
normalized before both decision and child storage, preserving exact SQL provenance;
semantic duplicate definitions are rejected. Up to100 active conditions are retained.
Risk-change conditional outcomes remain outstanding and grant no issue eligibility.

Intentional unfinished boundaries: this service is NOT wired to DI/HTTP/UI yet.
Any active condition still blocks plain approve until real resolution commands and
live proof validation are integrated. UW-22 plain approval remains blocked pending
that trading-history proof integration. Signed-statement conditions reject until
the later terms graph exists. Warranty conditions can be retained but their guarded
acknowledgement requirement remains to implement. Query code exists but still needs
its dedicated behavioral integration case before endpoint exposure. Add strict
serialized ConditionsJson byte/character bounds in command validation before HTTP
exposure, matching the database's131072-byte bound; current excess rolls back at DB.

RED .local/phase7-06-referral-service-red/sql/sql.trx has2 expected stub failures.
Fresh .local/phase7-06-referral-service-reviewed/{unit,sql} passes36 pure/shared unit
and6 SQL cases, gate42/no skips with cutoff2026-09-17T22:34:00Z. Both products cover
role/lease denial, foreign selection, stale second member with zero partial writes,
approval, exact retry, conditional rows from pretty JSON, unresolved-condition
blocking and revoked-grant replay. Two additional cases use real generated temporary
above-binder cover referrals to reject approval then persist decline/reopen. Synthetic
owned referrals in the ordinary decision fixture isolate command-boundary tests;
generation is independently verified. Both-product evidence-review regression also
passes after projection extraction. Issued snapshots remain unchanged. Debug build
and git diff --check pass; shared demo/previews and frontend-code untouched.

Next: implement resolution commands/live satisfaction, replace the explicit pending
condition/trading-history blockers with validated proof checks, harden boundary
validation, test query and malformed conditions, then add guarded warranty evidence,
bounded read models, strict DI/HTTP/contracts, UI and persisted browser acceptance.
Keep07-06 in progress; Phase7 stays5/16 and54/65 total plans. Continue inline, no agents.


## Verified decision validation/query checkpoint: 4ba4905

The decision service now rejects null selections/versions/condition lists, undefined
or non-object JSON, more than20 conditions and short trimmed reasons/questions with
422 before normalization/persistence. Both raw per-condition and exact serialized
ConditionsJson lengths are bounded to65536 UTF-16 units, matching SQL's131072-byte
nvarchar limit. Oversized input now reports servicing-condition-payload-too-large
rather than reaching a database exception. This closes the explicitly recorded
payload-boundary gap from the preceding checkpoint.

Actual query decisions are now verified on both products: a meaningful question and
documentary conditions persist with queried state; missing questions and warranty
query conditions reject. The fixture still checks unresolved proof blocking and
revoked-grant replay after a query. Existing approval/conditional/decline/reopen and
above-binder temporary-cover tests remain passing; no HTTP/DI/UI exposure was added.

RED .local/phase7-06-decision-validation-red/sql/sql.trx has2 expected failures from
unhandled null conditions. An intermediate green build overlapped the ending red
host and retried locked test assemblies; it subsequently passed. Final verification
ran after that host exited. Fresh .local/phase7-06-decision-validation-final/{unit,sql}
passes36 pure/shared unit and4 SQL scenarios. Gate verified40/no skips with cutoff
2026-09-17T22:55:00Z. Final build has no lock warnings, git diff --check passes and
issued snapshots remain unchanged. Do not start rebuilding integration binaries
until the previous integration-test process has fully exited.

Next remains resolution commands and live condition/proof satisfaction. Replace the
explicit pending-condition and UW-22 approval blockers only with verified current
proof checks; add guarded warranty acknowledgement requirements, bounded read models,
strict DI/HTTP/contracts and UI/browser verification. Shared demo/previews and sales
funnel remain unchanged.07-06 stays incomplete; Phase7 stays5/16, total54/65. Continue
inline without agents.


## Verified condition-requested trading proof checkpoint: e4a80d1

Active latest conditional/query decisions can now request trading-history evidence
for an established business, even when automatic age-based proof rules would not.
The evidence projection loads owned active conditions, reparses their closed dated
definitions against reconstructed risk, and unions their exact dates into current
requirements. Requirements, attachment and review all use the same asynchronous
projection. Requested dates are bounded, ordered, unique UTC members of the schedule
and contribute to the existing exact proof fingerprint. Other base proof behavior
is unchanged. Superseded decision conditions no longer keep a requirement active.

Both products now exercise a real condition-to-proof flow through UploadAsync,
AttachAsync and ReviewAsync, followed by persisted satisfaction readback. Accepted
proof still creates no condition resolution and cannot bypass the separate unresolved
condition approval gate. Query/reopen removes the current extra proof request while
retaining both historical condition rows and immutable evidence. This supplies the
previously missing prerequisite for documentary resolution commands; those commands
and live resolution evaluation are still next. No HTTP/DI/UI exposure is claimed.

RED .local/phase7-06-requested-proof-red contains1 pure and2 SQL missing-requirement
failures. The broader reviewed run passes37 unit+4 SQL cases, including both-product
base evidence review regression. After adding the superseded-requirement/history
assertions, fresh .local/phase7-06-requested-proof-final/{unit,sql} passes37 unit+2
SQL cases; gate39/no skips with cutoff2026-09-17T23:13:00Z. Final SQL tests cover the
complete amended decision/proof workflow. Debug build and git diff --check pass.
Issued snapshots, shared demo/previews and frontend-code remain unchanged.

Next: ResolveAsync with current grants before replay, editing/version fences, exact
condition/evidence/review ownership and immutable resolution append; implement live
satisfaction that rechecks latest resolution, latest accepted review, withdrawal,
active decision and exact requirement fingerprint. Replace pending-condition/UW-22
approval blockers only after those paths pass. Warranty acknowledgement still needs
its guarded proof-purpose extension; signed statements depend on real later terms.
Then finish bounded read models, DI/HTTP/contracts, UI/browser verification.07-06
remains incomplete; Phase7 stays5/16, total54/65. Continue inline without agents.


## Verified resolution command/live proof checkpoint: 81e50a7

ServicingReferralService is now partial with ServicingConditionService implementing
ResolveAsync and ConditionSatisfiedAsync. Resolution commands require current actor
capability/grants before receipt replay, exact owned condition/evidence, current
lease and draft/condition versions, active latest decision, matching purpose/target/
fingerprint and current reviewed nonwithdrawn proof. Conditional approvals require
one effective grant covering all cumulative annual risks and active dated conditions;
query proof resolution grants no risk approval. Trading-history review permission
is independently required. Writes append immutable resolution history and a new
draft ETag; retries reuse receipts and revoked grants cannot replay.

Live satisfaction checks the latest resolution outcome against the current condition,
current unexpired rating and requirement fingerprint, exact latest evidence review,
accepted file/review and withdrawal. A later rejected resolution or withdrawn proof
removes satisfaction without changing history. Risk-change, unavailable warranty and
signed-statement purposes cannot be satisfied through this command. No schema change,
DI/HTTP/UI exposure or business UAT is claimed for this slice.

RED .local/phase7-06-resolution-service-red/sql/sql.trx has2 expected stub failures.
Fresh .local/phase7-06-resolution-service-reviewed/{unit,sql} passes37 pure/shared
unit tests plus2 SQL workflows, gate39/no skips at cutoff2026-09-17T23:32:00Z. Both
products execute condition request, upload/attach/review, initially false satisfaction,
role/stale-condition/foreign-evidence denial, resolution and exact retry, latest
satisfied/rejected/satisfied history, withdrawal-driven loss of satisfaction, denied
new resolution on withdrawn proof and revoked-grant replay. Three resolution rows
remain immutable. Issued snapshots are unchanged. Debug build/diff checks pass;
shared demo/previews and frontend-code untouched.

Next: integrate live satisfaction into a bounded referral/readiness read model and
retain proof dependencies when deriving conditional approval readiness. Do NOT just
allow plain approve to drop old conditions: the new approve decision has ConditionsJson
empty and would hide the old requirement; withdrawal must still invalidate readiness.
The current plain-approve active-condition guard remains deliberately closed pending
that assurance design/integration. UW-22 proof eligibility also still needs its live
check. Add guarded warranty acknowledgement requirements, then strict DI/HTTP/contracts,
UI and persisted browser acceptance. Resolution helpers already support documentary
proof; consume them rather than adding duplicate state pointers.07-06 remains
incomplete, Phase7 stays5/16, total54/65. Continue inline without agents.


## Verified paginated referral/live readiness checkpoint: 1482fae

ReadReferralsAsync returns current owned referrals in ascending sequence pages of
1..50 with a stable next-after cursor, draft/referral/condition ETags, decision IDs,
condition satisfaction and per-referral DecisionReady. At most100 current conditions
are loaded. Current rated scope is held once; condition satisfaction now has an
internal same-context helper, avoiding nested transactions while reading the page.

A conditional referral becomes DecisionReady only when every current condition is
live-satisfied and its original actor grant remains unrevoked, effective and covers
the remaining term. The retained authority is rechecked against every cumulative
annual risk and active dated conditions. Expiry, withdrawal or grant revocation
removes readiness without deleting decisions or mutating conditional state. This
preserves proof dependencies rather than rewriting a conditional decision into an
unconditional approve with no conditions. DecisionReady is referral-level only:
base proof, capacity, terms and acceptance remain separate issue prerequisites.
The existing plain-approve active-condition guard remains protective; UI should
consume live readiness rather than encouraging users to erase conditional history.

RED .local/phase7-06-referral-read-red/sql/sql.trx has2 expected stub failures.
Fresh .local/phase7-06-referral-read-reviewed/{unit,sql} passes37 unit+2 SQL cases;
gate39/no skips with cutoff2026-09-17T23:51:00Z. Both products verify ready conditional
state after reviewed resolution, page-size/cursor behavior, invalid page denial,
expiry disabling all readiness, withdrawal disabling the same retained conditional
row and revoked grants disabling an existing approved row. Existing resolution and
decision flows still pass. Debug build/diff checks pass; no demo migration, browser
or UAT result claimed. Shared preview and frontend-code remain untouched.

Next: UW-22 live proof eligibility, guarded warranty acknowledgement requirements,
then complete bounded evidence/history views and strict DI/HTTP/contracts, UI and
persisted browser checks. ReadReferralsAsync is not wired to a route yet. Historical
cycles, richer decision/trigger detail and actual role-based UI actions must be
covered before closing07-06. Phase7 stays5/16, total54/65. Continue inline, no agents.


## 2026-09-18 trading-history approval and warranty acknowledgement

Implemented aafa4b2: actual generated UW-22 on a business startedOn correction
in both Motor Trade products now permits plain approval only with exact current
accepted trading-history proof. The check also precedes receipt replay. Withdrawal
removes referral readiness and denies replay/new approval while preserving the
historical approved decision. Conditional dependencies remain retained.
RED .local/phase7-06-trading-proof-red/sql has2 expected approval failures.
.local/phase7-06-trading-proof-green contains29 unit+4 SQL passing cases, gate33,
cutoff2026-09-18T00:10:00Z. Existing conditional flow passes alongside real UW-22.

Implemented fdbbc2b: ServicingWarrantyRules constructs one policy-level
acknowledgement covering ALL active dated warranty records. Its fingerprint binds
current condition identities, validated definitions, per-date captured wording,
stable targets, entire risk schedule and exact servicing ownership. Replacing even
the same wording under a new decision requires new acknowledgement. Multiple named
drivers are supported; old proof cannot transfer to a replacement condition.
Requirements projection includes current warranties; resolution matches this
aggregate purpose without reducing named-driver warranties to a single target.
Additive migration20260918002325_ServicingWarrantyEvidence expands the closed
purpose constraint and guards association insertion with a current owned warranty.
No historical migration rewritten and no shared demo migration applied.

RED .local/phase7-06-warranty-red has1 expected pure stub failure and2 missing
requirement SQL failures. .local/phase7-06-warranty-reviewed passes30 unit+6 SQL
cases (warranty, trading and existing referral flows). Final storage strengthening
.local/phase7-06-warranty-storage passes30 unit+2 SQL, gate32/no skips, cutoff
2026-09-18T00:25:00Z. Both products exercise reviewed resolution, withdrawal,
replacement fingerprint invalidation, wrong target/unreviewed proof denial,
SQL rejection without current warranties, and downgrade/reapply over existing
issued/rated data. Issued snapshot unchanged. Debug build/diff checks pass.

Remaining07-06: bounded evidence/history and richer referral/condition wording
views, strict DI/HTTP/contracts, role-based UI and actual persisted browser checks.
Signed-statement conditions depend on prepared terms in the later terms plan and
remain rejected until their owner/version exists. No API/UI or browser acceptance
claimed for07-06. Phase7 remains5/16; total54/65. Continue sequential inline.


## 2026-09-18 bounded evidence and referral history

Implemented265b8b2. ServicingEvidenceService now has FilesAsync (metadata only,
never content bytes), AssociationsAsync (explicit owned cycle), and ReviewsAsync
(immutable review/withdrawal events). Files/associations use ordered CreatedAt/Id
keysets with owned cursor lookup; event/decision histories use sequence keysets.
All page sizes are1..50, queries fetch at most pageSize+1, current held policy read
permission is checked before data access, and foreign cursors/children return404.
Historical reads use HoldDraft, not current rating eligibility, so expiry does not
hide prior reviews or decisions. ServicingReferralService.DecisionsAsync retains
reason/question/actor/authority/grant/conditions for every decision. Current
ReadReferralsAsync additionally returns trigger detail, latest decision, closed
condition definitions and per-effective-date wording/endorsement/target IDs.
No readiness or command authority is inferred from a historical record.

RED .local/phase7-06-history-red/sql has2 expected empty stub failures.
Intermediate .local/phase7-06-history-green/sql passes2. Final
.local/phase7-06-history-reviewed/{unit,sql} passes30 unit+4 SQL, gate34/no skips,
cutoff2026-09-18T00:35:00Z. Both products verify same-timestamp file cursor paging,
review/withdrawal pages, conditional decision history and retained old/new IDs,
foreign cursor/cycle/child and invalid page denials, reads after rating expiry,
and current named-driver wording across both effective dates. Existing conditional
flow still passes. Debug build/diff checks pass; no shared demo migration or browser
claim. Next is strict DI/HTTP/contracts, role-based UI and persisted browser checks.
Phase7 remains5/16; total54/65. No07-06 summary/completion yet.


## 2026-09-18 scoped HTTP reads and writes with runtime contracts

Read API5937108 wires seven authenticated policy-read routes for requirements,
files/downloads, owned-cycle associations, review events, current referrals and
immutable decision history. PartyPaging supports signed GUID keysets alongside
existing sequence cursors; actor/route/filter/size/version/expiry bindings remain.
HistoryVersionAsync reads only the scoped draft fence; paginated responses recheck
it and are no-store without pretending draft ETag is a response cache validator.
File downloads are scoped attachments with nosniff. Actual DI is registered.

Command APId661f62 wires upload, attachment, review, withdrawal, selected/single
referral decisions and condition resolution. Strict bounded JSON/multipart DTOs,
CSRF, strong draft/child ETags, editing lease and current held grants are enforced.
Single and selected decision receipt scopes are distinct actual HTTP paths. Shared
quote condition parsing is reused without changing its behavior. New servicing
Outcome helper avoids the quote helper's quote-specific201 Location behavior.
HTTP runtime names refine the planning contract: immutable fileId (not a separate
fileVersionId), associationId, /reviews plural, required purpose fingerprints and
child ETags. Closed runtime schemas are generated separately from pending planning
DTOs. Pending capacity/terms/issue routes remain unimplemented and closed.

Read RED .local/phase7-06-proof-reads-api-red/sql:2 missing-route failures.
GUID paging RED .local/phase7-06-guid-paging-red:1 expected stub failure,1 existing
pass; GREEN2 passes. Read final .local/phase7-06-proof-reads-api-reviewed passes
30 domain unit+2 paging+2 real SQL/HTTP, gate34/no skips;36 contract tests pass;
12 fresh HTTP response bodies validate against generated schemas. Read lint valid
with24 unused-component warnings.

Command RED .local/phase7-06-proof-commands-api-red/sql:2 missing-route failures.
Intermediate green had4 passing servicing regressions and2 upload Location
failures; corrected before final. An existing contract assertion assumed the old
planned fileVersionId/reason-only withdrawal and was updated to require fileId,
cycle/fingerprint and child ETag. Final .local/phase7-06-proof-commands-api-reviewed
passes30 domain unit+2 paging+2 SQL/HTTP, gate34/no skips, cutoff2026-09-18T01:00:00Z.
37 API contract tests pass;12 fresh read responses and14 actual persisted command
receipts validate using scripts/verify-servicing-proof-contracts.mjs reads/commands.
OpenAPI lint valid with28 unused-component warnings (includes superseded planning
DTOs). Debug build/diff checks pass. Both products verify CSRF denial on all7 writes,
strict form/body/query/version/lease handling, upload exact replay and download
Location, attachment, reviewed proof, condition resolution, withdrawal, selected
atomic validation/decline and single route matching/reopen. Read tests include
unauthenticated access, foreign children and forged/mismatched cursors.

Remaining07-06: frontend panels/forms and persisted browser checks; safe current
actor authority display and any required resolution-history projection should be
completed as part of that integration. Current referral trigger limits describe
retained rating inputs; do not label those as the viewer's current grants. No UI,
browser, shared demo migration or human UAT result claimed. Running previews still
use07-05 Release binaries; stop only verified owned processes before rebuilding.
Phase7 remains5/16,total54/65; no07-06-SUMMARY until full UI/source coverage passes.


## Frontend command and form checkpoint —734c6e0

Added servicing-evidence.tsx, servicing-referrals.tsx and a version-fenced paged
read hook; integrated with servicing-workspace lease/local-edit/command guards.
The filename refines the proposed servicingevidence.tsx to match existing local
component naming. Files, attach, review, withdrawal, selected/single decisions,
queries, typed dated conditions and explicit satisfied/rejected resolutions
now invoke the real07-06 routes. Conditions use an actual saved effective slice;
ConditionForm's existing quote prop is narrowed to its used proposal property,
without altering existing quote behavior. Full current grant limits are still
outstanding, and browser acceptance has NOT been run. Historical-cycle browsing
needs finishing: current cycle proof/history is wired; closed/superseded cycles
are not yet selectable from this panel.

Immutable proof commands snapshot scope, body, File, key, ETag and lease; receipt
validation checks exact draft/revision/cycle and strong body/header ETag. Lost
responses/readback retain the same command and disable other draft actions.
Cursor pages reset on draft ETag change and do not append stale data.
7 targeted frontend cases pass, covering exact proof matching, upload retry bytes
and identity, receipt scope, atomic selected decisions and reviewed rejected
proof resolution. Referral helper RED was missing module before implementation.
All126 frontend tests pass (.local/phase7-06-proof-ui-tests.log); TypeScript passes
from workspace root with --incremental false; changed-file ESLint passes.
A child-directory sandbox typecheck could not read root contract imports; root
invocation passed. No SQL rerun needed for this frontend-only checkpoint.
Backend latest evidence remains34 backend cases/37 contracts/26 live response
shapes from the previous checkpoint. No shared demo migrations or browser result
claimed. Phase remains5/16,total54/65.


## Current authority and local preview checkpoint —86a9d9a

Added GET /drafts/{draftId}/referrals/{referralId}/authority with held current
actor scope, distinct effective grants, bounded signed grant cursors (max5),
per-date referral dimension limits and full-schedule assessment with retained
conditions. No combined maxima or inferred issue permission. Pinned trading-year
threshold now reaches the shared authority display (default remains5). UI exposes
this live read and an evidence rating-cycle picker for retained historical proof.

Current authority RED .local/phase7-06-current-authority-red/sql:2 expected404s.
First green attempt reached revocation but the fixture's historical clock
violated RevokedAt>=CreatedAt; corrected to actual audit timestamp. Reviewed
.local/phase7-06-current-authority-reviewed passes16 targeted units+2 SQL/HTTP
journeys (gate18,minimumSQL2,cutoff2026-09-18T01:00:00Z). Both products prove
revocation removes current grants without changing draft ETag.4 fresh authority
HTTP bodies validate in authority mode;38 API contract cases pass; OpenAPI valid
with28 existing unused-component warnings. TypeScript and changed-file lint pass.

Local demo migrations and idempotent seeds applied additively, no reset:
.local/phase7-06-demo-upgrade.log. API Release build passed0 warnings/errors; web
production build passed. Old owned API52872/web25844 were verified and stopped.
Current API81768 serves5087. Web has been rebuilt/restarted during browser fixes;
read .local/phase7-06-preview-pids.json and verify actual process before stopping.

Browser harness scripts/verify-servicingevidence-browser.mjs is now implemented
but NOT passed. First attempt created combined draft333156b3-5b7e-4adb-acc8-01782ff360ff,
rated a business start-date change and found UW-22; it timed out finding the
decision selector via getByLabel. Accessible snapshot confirmed the role/name;
new form labels are now explicit and scoped field spacing added after inspecting
.local/browser-evidence/servicing-evidence/failure.png. Harness preflight only
abandons a previous draft recorded by this harness with exact policy/reason
checks, preserving its audit. No test success or completed source coverage yet.

The source audit identifies3 controls assigned07-06: Open referrals, Submit to
underwriting, and the pMta.sections[3].rows[0] navigation. Explicit submission and
policy-to-referral navigation still require implementation/verification; rating
automatically creating referrals is not counted as those controls. Prototype
submitMta() checks rated/no missing capture then shows submission toast. Need a
persisted implementation, not a toast-only alias. Keep07-06 incomplete,5/16,total54/65.


## Browser acceptance checkpoint —0e81d53 (latest)

Both actual Chrome Motor Trade journeys now PASS, completed2026-09-18T01:57:35Z:
.local/phase7-06-servicing-evidence-browser-labels.log and
.local/browser-evidence/servicing-evidence/report.json. First attempt's accessible
label lookup failed; explicit labels and scoped existing-style field layout were
fixed and the complete journey rerun. Both products verify actual conditional
decision, current actor authority, same-key/ETag/lease lost-upload retry with one
stored file, all required driver/business proofs and Combined premises security,
rejected then accepted content, conditional resolution while missing trading
proof independently blocks readiness, withdrawal invalidation, replacement proof,
reload persistence, rerated historical read-only proof, authorized download,390px
containment and unchanged issued snapshot. Each successful journey abandons only
its own fictional draft, retaining all history. The previous failed combined
draft was also abandoned through the UI after exact harness policy/reason checks.

Desktop/mobile full screenshots are retained. Focused retained-proof desktop and
mobile screenshots were captured from the persisted historical cycle and visually
inspected; fields, controls and wrapping fit both sizes. No human UAT claimed.
Final126 frontend cases pass (.local/phase7-06-proof-ui-final-tests.log), changed
TypeScript/React lint passes and root TypeScript --noEmit --incremental false
passes. Production web rebuild .local/phase7-06-preview-web-label-build.log passed.
Backend/contract evidence remains86a9d9a's16unit+2SQL gate18,38 contract cases and
4 actual authority responses; earlier34-case proof API coverage remains recorded.

Running preview now API81768 (5087) and web86300 (3100); read and verify
.local/phase7-06-preview-pids.json before stopping. Demo was upgraded additively
without reset. No outstanding test process. frontend-code untouched.

NEXT: finish the3 source controls before closing07-06. Prototype Open referrals
is a policy link to its MTA (go:'mta'); add persisted-policy/draft navigation to
the correct servicing referral section. The MTA review-row link should reach its
referral details. Submit to underwriting is a distinct explicit action: source
submitMta() requires rated/capture-complete and shows submission, but current
implementation only creates referrals during rating. Add a current-scope, version/
lease fenced, idempotent persisted submission with readback and tests, preserving
older cycle submissions on rerating. Do not present a navigation/toast alias as
a saved submission or count automatic rating as that control. Then verify these
controls, review source fields and record07-06-SUMMARY. Only then advance07-07.
Phase7 remains5/16 complete,total54/65; no plan completion claimed.
