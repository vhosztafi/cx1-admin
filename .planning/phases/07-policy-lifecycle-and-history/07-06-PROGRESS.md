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
