# 09-10 execution checkpoint

In progress.09-09 complete in5c4b2ec/fafd789/9aa8384;90/99plans and9/18Phase9plans.
Read09-09-SUMMARY for valid evidence; don't rerun unchanged09-09 suites.

## Current work (uncommitted)

- OperationalDeliveryTests unit:2capability RED failures→GREEN2, then2readiness
  RED failures alongside2passes→GREEN4. Reports.local/phase9-10-capability-* and
  phase9-10-rules-*. Send denies internal threads/blank subject/body/no recipients;
  packs require1..20distinct files. Existing draft rules bound50recipient IDs.
- Added message-send/document-send to internal capabilities and scoped allowlists/
  auth policies. Original parent permissions unchanged; external/finance denied.
- OperationalDeliveryRecords/Model: immutable queued MessageVersion; Delivery with
  originalsubject/relationship, optionalMessageVersion/ResendOf, Work/Scenario,
  ContentJson/Hash and state; normalized recipient and exact file snapshot rows.
- Migration20260922094628_OperationalDelivery +Designer/snapshot and Guards partial.
  Guards retain frozen versions/selections/delivery identity, validate source and
  original audience/contact/file snapshot, refuse downgrade when history exists.
  Found f.Sha256 binary-collation vs newContentHash defaultcollation conflict; fixed
  explicitCOLLATE. JSONimmutable comparisons now varbinary, not case-insensitive.
  SQLsourcebody uses OPENJSON nvarchar(max), not JSON_VALUE's4000character limit.
- MessageDeliveryService.Send/Queue, DocumentPackService.Send, DeliverySnapshots
  and OperationalDeliverySeed created. Not yet API/DI-wired or verified. Seed supports
  success/reject/transient-once/timeout-after-success/retry-required. Last scenario
  intended to exhaust initial6attempts then succeed after explicit manual retry,
  retaining same operation identity. No actual worker yet.
- New OperationalDeliveryTests integration builds actual issuedpolicy/thread/draft,
  posts /messages/{id}/send then expects202/frozenqueue/history/replay. First run25534
  failed migration collation (not valid APIRED). Second45550failedCS9113unusedfactory;
  removed unused constructor parameter until read methods need it.

## Active verification

CURRENT39928: corrected APIRED boundary run, log.local/phase9-10-api-red-boundary.log,
TRXfolder.local/phase9-10-api-red-boundary. Uses new CoverMGA_Test DB; expected404 since
API not implemented. Inspect completion; do not duplicate nativeSQL suites.

## Next

Review correctedRED, implement DeliveryEndpoints +DI sendmessage/pack +seed wiring,
prove queue API GREEN. Need sender/recipient/file authority before provider effect
and application, immutableproviderhash/scenario/op and leased fencing, persistent
attempt/outcome/quarantine, same-opretry vs explicitresend, exception→tasktypedowner,
scoped job/read/history APIs, contracts+UI+realSQL/browser acceptance. Queue response
is canonicalJob (jobId resource and Location), delivery lists locate exact delivery.
RetryETag design must be settled consistently with canonical requiredpreconditions.

Read09-10-PLAN,09-DATA-CONTRACTS,UI-SPEC,ADAPTERS,09-10-INTEGRATION-NOTES. Existing
SqlJobLeases requires newkind allowlist+terminal update integration; WorkflowTaskSources
.Jobs needs typed newdelivery→subjectowner mapping. Do not copy underwriting-specific
cycle constraints. Preserve existingworkers, retaineddemo/keys/frontend-code. NoPhase9
retaineddemo migration. Existingnext-env.d.ts/tsconfig.json changesremainexcluded.
User requests continuous autonomous execution; no pendingapprovalorblocker.

## Continuation update 2026-09-22 11:00 local

- Corrected APIRED39928 completed expectedAccepted vs404. API/DI now wired:
  DeliveryEndpoints.MapDeliveries has POST/messages/{id}/send (emptyclosedJSON,
  ETag/key/CSRF) and POST/records/{id}/document-deliveries (closedPackInput).
  Program registers services/routes; operational initializer seeds defaultscenario.
-10351 APIGREEN failed503 because WithDatabase includesquote seed but deliberately
  excludesoperationalworkflows. Fixture now explicitly calls OperationalDeliverySeed
  underheldtransaction. CURRENT11649 run.local/phase9-10-api-seeded.log/results.
  Do not duplicate SQL. No providerworker/UI/contracts/sendjobsread implementedyet.
- Newservices compiled: MessageDeliveryService(SqlCommandBoundary,TimeProvider),
  DocumentPackService(SqlCommandBoundary,MessageDeliveryService), DeliverySnapshots.
  Queue snapshots beforedraftheadlock, checksETag+samehintrowversion+selections under
  lock, insertsimmutableversion,delivery+work+recipient/file snapshots thenmarksqueued
  atomically. Authorization (includingselectedcurrentcontacts/files) precedesreplay.
  JobOutcome usesjobid resource202/ETag; existing generic jobread still needsnewscope.
- Migration collationfixexplicitf.Sha256 comparison COLLATEBIN2. FrozenJsoncomparisons
  varbinary to avoidcaseinsensitive mutation. Downnonemptyguardadded. Currenttests
  validateactualsnapshot/version+associationimmutability oncequeuegreen.

Workerdesignnext: usecurrent senderfromstoredCreatedBy, thenOperationalScope locks
(parentbeforeidentity), compare freshCapture to frozenJSON and verifyactual filebytes
throughFileService.Open beforeprovider. FileService is scoped, so dispatcheruses
IServiceScopeFactory likeFileFinalizationDispatcher. Providerdurableeffect in separate
context/transaction (cannotrollbackremoteeffect); persistScenario/op/hash/result.
Apply reauthorizes and fenceslease thencomparesproviderresult exactly. DiagnosticInbox
has duplicateevent/hash quarantinepattern usablewithnewprovidername; do not reuseits
businesspermissions. SqlJobLeases.FailAsync Superseded currently saysinvitation-
superseded; newkindneedsnarrowappropriatefailurecode. Existingkindallowlist and
MarkTerminal/WorkflowTaskSources.Jobs ownershipneedintegration. Readretrybudget:
initial6,max18, manualretryonlytransient/exhausted atlimit;retry-required scenario
intendedfailfirst6thenpassafterexplicitretry. SnapshotJSON fields:format,subjectId,
relationshipId,subject,body,recipients(contactId/name/email),attachments(versionId/
fileId/hash/name/mediaType/length). No worker mayreadmutabledraftcontent.

## Provider core update 2026-09-22 11:06 local

11649 queueAPI passed1 (.local/phase9-10-api-seeded).26990 expandedprovider passed1
(.local/phase9-10-provider-core): immutablequeue/replay/SQLguards, persistentoneeffect,
repeatedExecute sameoutcome, forcedleaseexpiry refusesapply, newlease recoverssameop,
appliedresult duplicate ignored, changedduplicate quarantined, deliverydelivered and
messagesent. These are same testidentity, not2unique cases. NoSQLcurrentlyactive.

New uncommitted: DeliveryAuthority.HoldSender (currentuser/roles/originalscope,
currentrecipientandfile metadataexactcomparison); MessageDeliveryWorker ExecuteProvider
and Applypartial; providertransactionseparatefromlocalauthoritytransaction, files.Open
verifiesactualreadybytes; MessageDeliveryDispatcher scoped/defaultDevelopmenttrue,
flagCover:OperationalDeliveryWorkerEnabled=false in acceptancehost. Addedkindallowlist
and newkindterminalstate propagation toSqlJobLeases, newkindSupersededcode. Applyuses
AdapterInbox/AdapterQuarantine and exactprovideridentity/scenario/hash/result/time,
leasefencing and safeoutcomeaudit. DeliverySnapshots.UpdateMessageState aggregates
resendstates so anolderfailed deliverycannot overwriteanexistingdelivered status.
WorkflowTaskSources.Jobs has typednewdelivery→OperationalSubjectparent mapping.
Dispatcher/typedworkflowmappingaddedAFTER lastprovider-corebuild; needcompileandnew
focusedacceptanceoncefurtherbehavioursready. No09-10commit/SUMMARY yet.

Remaining: read/history/job APIs+ETags, retry/resend (samejobvsnewdelivery), contracts,
UIpackreview/deliveryhistory/messageSend, fullworker/scenario/currentauthority tests,
actualreadyfiles/historyresend/storageguards/downgrade/hostedrestart/terminaltask,
browserfailure→retry→delivered/currentfingerprints. Existing genericjobsreaddoesnot
recognizenewkindyet. Needcurrentauthority beforeanyjob/disclosure. Readhistorycan
retainendedrecipientlabels underoriginalparent, butsend/retry/resend mustrevalidate.
Potentialreviewedge: workflowmaterializationrequiresoriginalsourceactoractive. New
revokedsender keepsdurableJobException butmayneedauthorizedfallbacktaskowner; do not
silentlyelevatepermissionsorclaima taskexistswithouttesting. Normalterminalfailure
mustproduceexactlyoneOperationalTaskthroughpublishedworkflowrule.

## History/recovery update 2026-09-22 11:16 local

4118 history-resend passed1 in.local/phase9-10-history-resend (sameexpandedAPItestID).
Scopejobread/no-store, deliveryread/body/ETag, newoperationresend andunchangedoldreceipt,
messageaggregatestatestayssentifanydeliverysucceeded, recoveryreasonAuditEvent pass.

Newuncommitted DeliveryReadService lists/read/attempts/job, MessageDeliveryService
.Recovery, extendedDeliveryEndpoints read/retry/resend routes (bothmessage/document)
andgenericOperationalJobEndpoints owningnewkindbranch. Retry requiresdeliveryETag,
notjobETag; unchangedHTTPcontracts stillneedregeneration/documentation. Resendalso
usesdeliveryETag andforbidsstillqueued. Currentrequestactorandfrozenrecipient/file
snapshotvalidatebeforereplay; provider independentlyvalidatesoriginalsenderbefore
anyeffect. Retrylocksworkheadbeforedeliveryhead to matchworker/FailAsync. Readservice
usesSerializable; reviewcount/headlockinteractionduringconcurrentworkerbeforefinal.

CURRENT2938: RealSqlOperationalDeliveryTimeoutAndExplicitRetryRetainOneEffectAndExceptionTask,
.local/phase9-10-recovery-sql.log/results. Newfixturetests timeoutaftersuccessrecovery,
retry-required6failures/oneJobException/oneworkflowtask, authorizedsamejobattempt7,
providerrejectnotretryable. Do not runanotherSQLacceptanceuntilcomplete.
No09-10commit/SUMMARYyet. Manycases/UI/contractsremain; followearlierremaininglist.

## Delivery verification and UI update 2026-09-22 11:35 local

Recovery SQL passed1 (.local/phase9-10-recovery-sql). Revocation initially 1pass/1fail: contact fixture violated CK_Contact_Ending (primary flag/timestamp). Corrected fixture clears IsPrimary and bounds EndedAt by CreatedAt; .local/phase9-10-revocation-fixed passed2, no skips. Suspended-sender fallback uses an existing active internal actor with parent capability; original actor retained in source snapshot. No grants changed.

New OperationalDeliveryPackTests.cs real-SQL case passed1 in .local/phase9-10-pack-sql: pending file rejected without delivery, ready file queued/replayed, changed same-key body rejected, replacement version does not alter selected original file, resend creates new operation/receipt preserving original JSON/hash/version/name, both actual provider applications succeed. Session76288 completed and closed. No SQL suite active.

DeliveryReadService now uses ReadCommitted with existing explicit parent/identity/audience/file authority locks: avoids holding a delivery shared lock while waiting on worker-owned work head. This change still needs current-source read/concurrency verification. ProviderOutcome added to history DTO/contracts. Contracts regenerated; broader tests pending.

Frontend communication command transport supports send-message/send-pack/retry/resend with frozen bytes and message/delivery ETags, validates queued job receipt without claiming delivery. Communications tests passed6. New delivery-history.tsx and Send to agency in thread.tsx wired; frontend typecheck passed. UI NOT browser-verified yet. Document-pack picker, recipient discovery, browser scenarios, broader negatives, guard/downgrade/concurrency/hosted recovery and final review remain. No09-10SUMMARY or commit; phase remains9/18 plans complete. Preserve unrelated next-env.d.ts/tsconfig.json edits.

## UI acceptance checkpoint 2026-09-22 11:42 local

Document pack picker now selects ready exact versions from current rows and version history; prevents mixed relationship packs, allows up to20 selected files, previews original versions. New GET /records/{recordId}/document-delivery-recipients/{relationshipId} via ThreadService.PackRecipients validates original subject and current audience without creating a thread. Contract regenerated. UI typecheck/build/lint pass. Root417/frontend192 passed; targeted .NET unit4 passed in .local/phase9-10-unit-current.

.local/phase9-10-current-sql passed5 (2m46s). Subsequent worker review maps malformed provider result JSON to bounded ProviderConflict and validates result state matches provider state. Pack tests subsequently add missing-recipient/version/internal-file denial and SQL immutability/protected downgrade checks; these require a fresh SQL run after the active browser suite.

Motor browser passed1 in .local/phase9-10-browser-motor, report/output .local/browser-evidence/communications/CoverMGA_Test_d1fc31de4418436782c52eae4ea2c06b. Current script exercises failed6→UIretry→delivered7, samejob/delivery IDs, historical document pack, no overflow390px, all prior note/draft/conflict flows, and SQL readback (sent message,2delivered/2provideroperations/1exception). Mobile delivery screenshot inspected. Bundle .local/next-phase9-10-browser. Manifest .local/phase9-10-browser. Commercial browser ACTIVE session84114, .local/phase9-10-browser-commercial.log; do not duplicate SQL until it completes. Browser harness accelerates scheduling only in its owned fixture and drives actual lease/provider/application services with hosted deliveryworker disabled. HumanUAT/retained demo migration not claimed.

Remaining: inspect commercial browser outcome/screenshot; current-source SQL rerun for latest guard/worker changes; strengthen foreign-existing-parent/file and actual-byte corruption negatives if missing; review final diff, record honest acceptance limitations, strict unique TRX evidence, source ledger/SUMMARY/state/commits then09-11. No09-10completion yet. All changes remain uncommitted; preserve unrelated next-env/tsconfig.

Final:09-10 complete. Strict11unique/7realSQL/no skips. All processes finished; backend29632db, UI/contracts46c6b9b. Resume09-11. Earlier pending entries are historical.
