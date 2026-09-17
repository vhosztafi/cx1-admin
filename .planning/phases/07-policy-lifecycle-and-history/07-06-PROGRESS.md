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
