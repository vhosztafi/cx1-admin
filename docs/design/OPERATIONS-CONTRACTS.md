# Operational contracts v1

Phase9 implementation contract,2026-09-21. This document describes the approved target; runtime routes/storage remain pending their owning plans. `contracts/schemas/operations.schema.json` and generated `contracts/generated/operations.ts` are produced from `scripts/operations-contracts.mjs`. OpenAPI applies `addOperationalRuntimeContracts` after legacy form modifiers, so its final endpoint schemas are authoritative. New/updated routes are labelled phase-9-contract-only until proven implemented. Existing issued-policy JSON, source envelopes and live underwriting/servicing routes remain unchanged.

## Ownership and transactional model

OperationalSubject has Id and exactly one nullable typed FK: AgencyId, RelationshipId, QuoteId, PolicyId or ServicingDraftId. CHECK enforces exactly one; filtered unique index per FK makes registration idempotent; all deletes NO ACTION. Kind is derived/checked against the nonnull FK. Public registration uses OpsSubjectWrite(kind,parentId), never arbitrary actor/agency grants. Register under current parent authorization before receipt lookup; readable parent plus explicit registration permission is required. SubjectId is a locator, never authority. Subject parent is immutable. Client pages select authorized relationship subjects, never a global-client container for competing agencies' information.

Current quote scope locks agency → current identity → quote → relationship/client; policy scope extends that chain with policy/term/version. Operational commands first resolve parent hints without trusting them, acquire agency locks in sorted UUID order, then current identity and all required parent quote/relationship/policy chains in canonical kind/UUID order before child locks. Do not lock an operational child and then call parent scope. A shared bulk scope helper must preload/lock all agency rows before identity (not call single-parent helpers one-by-one across agencies), then revalidate hints and current relationships. Parent operations must continue their established order. Subject/head rows follow parent locks, task IDs ascending, immutable child reads, then outbox/owned lease. Same-key command application lock remains the outer SqlCommandBoundary lock. Deadlock retries preserve the command identity and never fabricate success.

SqlCommandBoundary.ExecuteAuthorizedAsync owns the transaction for domain changes, immutable trail, audit, outbox and receipt. Authorization precedes receipt read; ETag/prerequisites follow same-key replay. Missing If-Match428, stale412, changed key409; wrong/missing subject404, visible forbidden operation403. Suspended/revoked identity or relationship cannot disclose old receipts. Bulk selection1..100 requires distinct IDs AND per-row ETag, even if duplicate objects have different ETags. Validate all first; failure rolls all back. Server actor/time/authority/derived totals cannot be supplied by callers.

## Persistence boundaries

| Records | Keys, types and integrity |
|---|---|
| OperationalTask | UUID/SubjectId FK, stable reference, closed type/priority/state, nullable dueOn SQLdate, exclusive eligible user/team/unassigned, rowversion, immutable CreatedBy/CreatedAt. Parent cannot change through edit. |
| TaskEvent/Comment/Checklist | (TaskId,sequence) unique; actor/time/reason append-only; checklist definition from pinned type/rule, state changes separately audited; comment bounded Unicode plain text. Manual task content never records a referral decision. |
| WorkflowTaskSource | unique(ruleVersionId,sourceKind,sourceId,eventKey); exact source/rule snapshot and hash; rule version references published SettingVersion. Reconciliation cannot reopen completed task or produce a new key per poll. |
| FileObject | Internal UUID, storage-kind discriminator, server-derived local key OR one typed legacy Agency/Quote/ServicingEvidenceFile FK, byte count≤20MiB, binary-collated lowercase64-character SHA256 matching retained evidence interfaces, detected media, safe display filename, pending/ready/quarantined state. CHECK exclusive sources; legacy FK unique. No public keys. |
| Document/Version | Document SubjectId/visibility/relationship; unique(DocumentId,number), immutable version FileObject and exact source/template/renderer provenance; unique originating request association. New regeneration makes another version. Withdrawal is an effective-dated immutable event. |
| Note/Thread/Message | Note internal-only append-only; Thread subject plus closed audience/relationship; MessageDraft rowversion; MessageVersion immutable queued body/recipients and exact attachment versions. Composite ownership FKs where IDs repeat. |
| Delivery/Attempt | MessageVersion/pack association, WorkId unique, immutable operationKey/requestHash/scenarioVersion; durable provider outcome separate from applied state; ordered attempt records. Retry same op, resend a new op over chosen unchanged versions. |
| Incident/Revision/Resolution | Head rowversion, owned policy; immutable revision JSON plus hash. Closed product-specific source fields and factual occurrence precision. Resolution binds revision, knownAt and owned temporal candidates. Handoff requires same current revision/resolution and complete readiness. |
| ClaimsSummary | Append-only exact handoff/provider-event/hash, asOf and receivedAt; paid/reserved decimal(19,2) nullable, GBP; no local settlement command. Changed event duplicate quarantined. |
| MID/Consequence receipts | Unique existing intent/consequence and WorkId; exact policy/term/transaction/version/hash provenance; request items immutable; apply only live lease/current authority and effective boundary. |

Every new table receives appropriate indexes for scoped paging, source uniqueness and next-attempt scans, plus migration/snapshot and actual SQL tests. JSON columns have ISJSON checks and a format/version discriminator; service validates closed schema before insert. Append-only issued/provenance/file-version rows need SQL guards, not only immutable C# records. Date/time columns retain UTC instants or local dates according to meaning, never silently convert a loss date to midnight.

## Task semantics and source fields

Types: servicing, underwriting-referral, authority-referral, renewal, data-exception, agency-onboarding, complaint, underwriting. Source Low/Medium/High maps low/normal/high; existing urgent contract remains supported. State open/in-progress/awaiting-information/blocked/completed/cancelled; overdue derives from dueOn and nonterminal state. Complete/reopen/other transition is reasoned and audited. Checklist completion does not approve underwriting. Required checklist gates completion under pinned type/rule; manual types may have no checklist. Created by me uses CreatedBy, not owner.

Owner inputs tkOwner/tkOwner2 map assignment union {kind:user,ownerId}, {kind:team,teamId} or {kind:unassigned}; backend validates eligibility under current scope and out-of-office rules when Phase11 provides them. tkDue/tkDue2 maps dueOn; date display DD/MM/YYYY, transport ISOdate. Type/priority/title retain their source controls. Four review checklist controls call updateTaskChecklist, task comment calls addTaskComment, and explicit reopen calls transitionTask with a nonterminal state/reason. Bulk actions cannot approve referrals or issue transactions.

## File, generation and visibility

Upload is bounded multipart bytes plus typed kind/visibility/relationship, using existing uploadDocument operation. Server filenames, content signature allowlist(PDF/PNG/JPEG), hash/length, nonexecuting private volume and persistent finalization follow09-RESEARCH. Pending/quarantined is unavailable. Actual storage failure returns safe Problem and never a ready version. Legacy SQL bytes remain under their original evidence scope and are bridged additively. Download/preview reauthorizes every request, returns safe Content-Disposition, private no-store and nosniff; no caller URL/path is accepted.

Generated kinds include quotation, statement-of-fact, policy-schedule, policy-certificate, endorsement, renewal-invitation and cancellation-notice. Generate command selects exact typed source plus published template and reason; server validates source belongs to subject and template applies to product/kind/date. It may reference an existing request but cannot rewrite it. RendererPDFsharp-MigraDoc6.2.4, pinned licensed fonts and closed projections follow research. Quotation/renewal may use exact terms versions; historical documents use their original source and original bytes. Unsupported product/kind is422. CC EL certificate requires selected EL; no motor certificate on CC. CC property subject cover labels map to retained property/BI amounts; liability cover codes already match existing CommercialIncidentPayload sections. Occupation IDs resolve to retained wage category labels for the old payload adapter, never caller text authority.

Internal notes do not become agency thread messages. Internal thread rejects relationshipId; agency thread requires current owned relationship. Recipient and attachment selection is always server-authorized, including ready state, visibility and subject relationship intersection. Default deny cross-subject attachments. A permitted internal author cannot send internal evidence to an agency. MessageDraft may be blank; queue/send requires nonblank body or valid applicable content, eligible recipients and exact ready attachments. Draft and queued state/read models must both validate. Pack send requires1..20 exact versions and1..50 distinct recipients. Server limits duplicate IDs independent of JSON uniqueItems.

## Incident source mapping and time

Draft requires policyId/productCode only. Server confirms policy product and cannot reparent existing incident. Additional data is structurally checked while incomplete. Source lcDate/lcTime map occurrence with date/approximate/exact precision; approximate preserves the recorded local time as a hint while temporal resolution considers the full local day unless a factual exact time is later clarified. No fabricated tolerance interval or asserted midnight. An exact UTC instant must agree with local occurredOn in Europe/London; DST invalid/ambiguous local conversion is rejected or explicitly disambiguated.

Source lcInvolved maps motorSubject.kind; registered-vehicle, unregistered-vehicle(Not on register), stock-or-customer-vehicle, premises or third-party-only. lcVeh/lcDriver resolve owned historical IDs; explicit not-named/unknown declarations retained. lcDrivable maps yes/no-recovered/unknown. Property lcProp/lcOwner/lcVal map subject itemDescription/owner/estimatedValueAtRisk. Remaining fields map locationDescription, policeReference, thirdPartyInvolvement/name/insurerOrRegistration, description, reportedBy, reportingRoute and bestContactDescription. CC uses a separate commercialSubject, owned property location/cover or liability cover/occupation. Motor-only kind/details are rejected on CC.

Resolve-occurrence command persists one resolution for current immutable revision at a server knowledge cutoff. Date-only or approximate periods intersecting multiple material versions/cancellation boundaries remain ambiguous/partly uncovered; saving facts is allowed but handoff is blocked pending clarification. Clarification records reason and new revision. Handoff revalidates resolution against the same revision and current authority; a new source/history change affecting applicability requires a new resolution, never silently retargets an existing handoff. Log and hand off is one atomic command with durable outbox. Failed/uncertain handoff retries same immutable operation. Definitively rejected corrected revision becomes a distinct operation. Unknown provider paid/reserve remains null, not zero.

## Recovery, compatibility and proof

Provider workers persist exact operation/scenario/hash and durable response before applying under owned lease. Timeout-after-success recovers saved response, duplicate changed response quarantines; one terminal exception task. Recheck current sender/recipient/source before execute and apply. Revocation cannot erase an already performed demo effect; show safe historical outcome without authorizing a new send. Certificate withdrawal and eligible task closure wait until exact cancellation effectiveAt; old notice receipts stay unchanged and are not silently resent. MID reads exact MT intent actions/version/effective dates, never latest risk or CC.

Phase1 legacy TaskWrite/IncidentDraftWrite schemas remain as historical contract components, but actual operational routes now reference Ops-prefixed schemas after all generator modifiers. Existing example/binding fixtures are updated explicitly. This is an intentional pre-runtime refinement, not a migration of issued policy JSON or already working sales/underwriting transport. Runtime endpoints, SQL migrations, PDF rendering and browser journeys are owned by09-02..18 and remain unverified by this contract-only work.

Contract acceptance includes strict AJV negatives, all generated request/response schema compilation, real source-ID/mapping preservation and generated TS typecheck. Duplicate IDs, current ownership, temporal semantics and all DB/filesystem side effects require later unit/SQL/browser tests; schema validation cannot establish those guarantees.

## Operational endpoint matrix

Generated from the final OpenAPI contract. Task/subject entries are API-verified by09-02; remaining families are runtime pending. Write commands require session and CSRF; operation scope is rechecked before receipt replay. Bounded Problem responses use the common API conventions (400 malformed input,403 forbidden,404 unavailable subject,409 conflict,412 stale,422 invalid transition/readiness,428 missing version). Bulk uses each selected head ETag. A queued202 response never means the adapter completed.

| Operation | Route | Permission | If-Match | Idempotency |
|---|---|---|---|---|
| listTasks | GET /tasks | task-read | none / per-selection for bulk | not-cached |
| createTask | POST /tasks | task-write | none / per-selection for bulk | required |
| getTask | GET /tasks/{taskId} | task-read | none / per-selection for bulk | not-cached |
| updateTask | PUT /tasks/{taskId} | task-write | required | required |
| transitionTask | POST /tasks/{taskId}/transition | task-write | required | required |
| assignTasks | POST /tasks/bulk-assignment | task-assign | none / per-selection for bulk | required |
| createThread | POST /records/{recordId}/threads | message-write | none / per-selection for bulk | required |
| createMessageDraft | POST /threads/{threadId}/messages | message-write | none / per-selection for bulk | required |
| updateMessageDraft | PUT /messages/{messageId} | message-write | required | required |
| listDocumentVersions | GET /documents/{documentId}/versions | document-read | none / per-selection for bulk | not-cached |
| generateDocument | POST /records/{recordId}/documents/generate | document-generate | none / per-selection for bulk | required |
| uploadDocument | POST /records/{recordId}/documents/upload | document-upload | none / per-selection for bulk | required |
| listIncidents | GET /incidents | incident-read | none / per-selection for bulk | not-cached |
| createIncidentDraft | POST /incidents | incident-write | none / per-selection for bulk | required |
| getIncident | GET /incidents/{incidentId} | incident-read | none / per-selection for bulk | not-cached |
| updateIncidentDraft | PUT /incidents/{incidentId} | incident-write | required | required |
| logIncident | POST /incidents/{incidentId}/log | incident-write | required | required |
| handoffIncident | POST /incidents/{incidentId}/handoff | incident-handoff | required | required |
| listClaimSummaries | GET /incidents/{incidentId}/summaries | incident-read | none / per-selection for bulk | not-cached |
| listMidSubmissions | GET /versions/{versionId}/mid-submissions | mid-read | none / per-selection for bulk | not-cached |
| sendDocumentPack | POST /records/{recordId}/document-deliveries | document-send | none / per-selection for bulk | required |
| listDocumentDeliveries | GET /records/{recordId}/document-deliveries | document-read | none / per-selection for bulk | not-cached |
| validateIncidentProposal | POST /incidents/validate | incident-write | none / per-selection for bulk | not-cached |
| registerOperationalSubject | POST /operational-subjects | subject-read | none / per-selection for bulk | required |
| changeTaskDueDates | POST /tasks/bulk-due-date | task-assign | none / per-selection for bulk | required |
| completeTasks | POST /tasks/bulk-completion | task-write | none / per-selection for bulk | required |
| updateTaskChecklist | PUT /tasks/{taskId}/checklist | task-write | required | required |
| addTaskComment | POST /tasks/{taskId}/comments | task-write | required | required |
| listTaskComments | GET /tasks/{taskId}/comments | task-read | none / per-selection for bulk | not-cached |
| listTaskEvents | GET /tasks/{taskId}/events | task-read | none / per-selection for bulk | not-cached |
| clarifyIncidentOccurrence | POST /incidents/{incidentId}/occurrence | incident-write | required | required |
| resolveIncidentOccurrence | POST /incidents/{incidentId}/occurrence-resolution | incident-write | required | required |
| logAndHandoffIncident | POST /incidents/{incidentId}/log-and-handoff | incident-handoff | required | required |
| contactClaimsAdministrator | POST /incidents/{incidentId}/contact | incident-handoff | required | required |
| listMessageDeliveries | GET /messages/{messageId}/deliveries | message-read | none / per-selection for bulk | not-cached |
| getDocumentDelivery | GET /document-deliveries/{deliveryId} | document-read | none / per-selection for bulk | not-cached |
| listDocumentDeliveryAttempts | GET /document-deliveries/{deliveryId}/attempts | document-read | none / per-selection for bulk | not-cached |
| retryDocumentDelivery | POST /document-deliveries/{deliveryId}/retry | document-send | required | required |
| resendDocumentPack | POST /document-deliveries/{deliveryId}/resend | document-send | required | required |
| retryMessageDelivery | POST /message-deliveries/{deliveryId}/retry | message-send | required | required |
| previewDocumentVersion | GET /document-versions/{versionId}/preview | document-download | none / per-selection for bulk | not-cached |

##09-02 implementation refinement

Task list/detail responses include a strong etag per row, required by OpsTask, for preserving displayed selections in bulk commands. Task commands and history are implemented/SQL-verified; UI remains09-03. Pure discovery uses the existing QuoteDiscovery serializable read pattern with current identity and scoped parent predicates; writes retain the held agency→identity→parent→task lock order. Cursor versions include visible task and relevant owner rowversions because owner team changes affect the team view. Task references are currently stable UUID-based strings; the UI plan may add readable reference allocation.

## 09-03 presentation and discovery refinement

Task read rows now require `subject` (typed id/kind/parentId/label/href), `assignmentLabel` and `createdByLabel`; labels are batch-projected only from already authorised subjects in the held transaction. New task references use `TaskReferenceSequence` and `TSK-` plus at least7digits. Migration20260921140631_TaskReferences preserves all existing references and restarts above existing numeric task references if reapplied.

`GET /api/v1/tasks/summary` (`getTaskSummary`, task-read, no query, no-store) returns open/dueToday/overdue/awaitingOthers/completedSevenDays and asOf. Due measures exclude terminal tasks and use London dates. Completed7days counts currently completed visible tasks with an immutable completion transition during the preceding7days, not recent comments or updates.

`GET /api/v1/task-assignees` (`listTaskAssignees`, task-assign, no-store) accepts1..100distinct repeated subjectRecordId UUIDs, required kind=user|team and optional q<=100chars. It holds current scope for every subject and reuses command assignment eligibility for active internal users or teams with an eligible member across all selected subjects. Returns at most50eligible `{id,label}` items and hasMore; search refines larger result sets. The write still rechecks current assignment authority.

Frontend pending commands freeze the exact body, key, actor and task ETag(s). Unconfirmed outcomes retry the original request; stale failures retain forms/selection. Detail review loads the current saved values separately and requires explicit adoption before rebuilding a command. Creation registers a typed saved parent with its own fixed-key recovery before creating the task. Task completion remains distinct from any underwriting, issue or servicing decision.

## 09-04 workflow provenance

Task rows may include `workflow` (`OpsWorkflowProvenance`): the first applied rule code/id/version/title, source kind/event id, creation time, current source condition and sourceChanged. Conditions are outstanding, resolved, not-due or unavailable. Current source state is projected after task-parent authorization; it never updates the human task state, history or ETag. Immutable first-applied source/rule JSON and SHA-256 hashes remain in WorkflowTaskBinding.

## 09-07 generation command refinement

`OpsDocumentGenerate.source` is discriminated by source kind and requested document kind. A quotation from `quote-revision` requires both `quoteRevisionId` and `quoteTermsVersionId`; a statement may omit terms. No implicit latest terms lookup is allowed. Policy sources cannot create proposed quotations/invitations; quote and servicing sources cannot issue certificates or schedules. Servicing sources retain `termsVersionId`.

Optional `documentId` requests an explicit new version of an existing owned document. Omission on the explicit generate command creates a new logical document. Automatic import of an original retained request instead reuses its unique association and original work; it never calls the explicit command to enqueue duplicate business intent. Same-key retry returns the original version. SQL validates the supplied document's subject, kind and audience before adding a version. Audience shape remains internal/insurer without relationship, or agency with exact relationship. The public generation endpoint is verified for CSRF, closed input, replay, current access and actual PDF generation; it returns202 `OpsDocumentVersion` and a status `Location`. Selected policy, quotation and servicing terms sources are SQL verified. List/history, staged upload attachment and original cancellation consequence PDF generation are verified. Original cancellation delivery work and receipts are preserved even when already delivered; separate technical generation work pins the notice/version/template. Legacy evidence continues through the existing typed bridges and original authorized download APIs;09-08 composes their document/evidence UI.

The first implemented09-07 read routes are `GET /document-versions/{versionId}` (`getDocumentVersion`, document-read), `/content` (`downloadDocumentVersion`, document-download) and `/preview` (`previewDocumentVersion`, document-download). All reauthorize current original-parent scope; status is pending until both the bound file and original generation work are ready/succeeded. Content and preview return the exact verified saved bytes with attachment/inline filename, private/no-store and nosniff. Pending/quarantined returns409. Damaged ready bytes persist quarantine/audit and return503 without paths; later requests cannot download them. The status DTO omits storage keys and file IDs. Explicit generation, staged upload attachment and paged list/history routes are now verified.

Workflow identity is `(RuleCode, SourceKind, SourceEventId)`, independent of wording revisions. Reconciliation validates the latest effective published SettingVersion, reauthorizes the originating internal actor and real parent, acquires a transaction-owned source/rule lock and atomically inserts the task, initial checklist/event, binding and audit. A later version can affect genuinely new source events but cannot recreate an existing obligation. Each binding has typed source FKs and a composite task/subject FK; update/delete and destructive downgrade are rejected.

Source adapters resolve quote/servicing referrals and query decisions, matched information requests with an actual quote or client-agency relationship, issued policy terms, agency follow-ups and owned job exceptions. Unknown/diagnostic jobs cannot use a payload GUID to invent an insurance parent. Older match decisions with equal ordering timestamps are reported as ambiguous instead of guessing a current request. Missing/revoked originators fail current authorization; reconciliation does not add roles or grants.

`WorkflowTaskScanner.Scan` processes at most64events per rule/source kind per pass and revisits the remaining records through offsets. Invalid latest definitions produce issues without fallback to older rules. `WorkflowTaskSeed.SeedAsync` adds only missing fictional rule scopes inside the serialized seed transaction. Later operational producers can reuse this mechanism; document and incident source families remain with their owning plans.

###09-05 file runtime refinement

FileService stages bounded raw bytes outside the SQL command and inserts immutable FileObject metadata and file-finalization OutboxWork atomically after fresh authority checks. POST /records/{recordId}/file-uploads?name=... returns202 OpsFileUpload; GET /file-uploads/{uploadId} returns status and GET /file-uploads/{uploadId}/content returns verified attachment bytes. All are internal operational capabilities with the original typed parent checked again on every operation. Upload IDs are separate WorkId values; they do not expose FileObject.Id or storage paths. This additive staging contract avoids pretending a file is a DocumentVersion. The09-07 uploadDocument contract is refined to a closed JSON attachment command referencing this staged upload ID, with UI09-08. New file bytes acceptPDF/PNG/JPEG; existing screened text/plain evidence is available through the typed legacy bridge and original APIs.

FileFinalizationWorker.Process holds current originator authority, parent and SQL file/work locks through bounded local disk verification/rename and ready-state commit. This local two-resource boundary uses transaction locks instead of a remote provider lease. A process crash rolls back SQL publication, and the next pending attempt verifies any existing renamed bytes. IO availability failures are bounded retries; content/path failures quarantine. Download-time integrity failures also persist quarantine and an audit event. Ready bytes are never overwritten. Filesystem path checks assume the configured private-volume ACL prevents untrusted concurrent path manipulation.

BridgeLegacy links original AgencyEvidenceFile/QuoteEvidenceFile/ServicingEvidenceFile records with unique typed FKs and original-parent checks. SQL insert guards independently verify screening, identity, metadata and actual byte hash. No original bytes, evidence reviews or attestations are copied or edited. CleanupExpiredTemporary serializes with metadata insertion and removes only expired unreferenced temporary files; it cannot delete ready or any referenced object. File finalization exceptions resolve workflow ownership through FileObject→OperationalSubject, never an arbitrary outbox subject GUID.

###09-07 upload composition refinement

POST /records/{recordId}/documents/upload now accepts OpsDocumentUpload JSON (uploadId, kind, visibility, reason, optional relationshipId/documentId). This replaces the provisional multipart shape: bounded byte streaming/finalization already belongs to file-uploads, and document attachment must reuse its identity rather than upload the bytes twice. The original uploader must retain current parent access. Same upload and identical metadata resolve to one document version even with another command key; conflicting metadata returns409. A replacement uses a new upload and creates another immutable version. FileObject IDs, paths and caller-provided bytes are not accepted by this metadata command. The original file/work, SHA256 and creator are retained; no extra document-generation work is created. Generation, delivery and file availability remain distinct. Legacy evidence retains its original IDs, reviews and APIs/typed bridge;09-08 integrates those authorized links in the document/evidence UI.

## 09-08 exact selection and task attachment contracts

`GET /api/v1/records/{recordId}/documents/options` requires document-generate and an explicit sourceKind/sourceId, with quoteTermsVersionId when selecting a quotation. It returns applicable published template/kind combinations for that exact owned source, readable source labels/dates and a bounded opaque nextCursor. An empty page may still have a next cursor. There is no implicit latest source or template selection. Document list heads include currentVersion metadata; history/read responses retain exact source, template, terms, renderer and file hashes. Metadata is projected from the retained source graph in bounded batches.

Task attachments are associations to saved DocumentVersion IDs, not copied files or all documents on the parent. `GET /api/v1/tasks/{taskId}/attachments` returns up to20 active links with exact version metadata, author, creation time and reason. `POST` on that route accepts only documentVersionId/reason; `POST /api/v1/tasks/{taskId}/attachments/{attachmentId}/remove` accepts only reason. Writes require task-write, CSRF, task If-Match and Idempotency-Key and return the updated normal task receipt/ETag. Reads require task-read. Both recheck original document authority; writes do so before replay. A different original subject is denied even when the actor can read both parents. New links require a ready version and an editable task. Removed links retain original and removal provenance; reattachment creates a separate association.

Migration `20260921215507_TaskDocumentAttachments` adds TaskDocumentAttachment with task/version/user FKs, one active task/version pair, removal checks and SQL guards against cross-parent insertion or original identity changes/deletion. Task association, task event/ETag and audit commit atomically. The UI preserves the selected version, actor, command body/key and ETag through uncertain retries. Regeneration never repoints a task attachment. Download and preview still authorize the original exact-version endpoint independently.

Record documents are composed on quote, policy, agency, client-relationship and servicing-terms views. Client identity itself is not an operational subject: users select a saved agency relationship. Existing evidence is displayed through its original typed API and download IDs; adding a document does not record underwriting assessment or signing proof. Browser completion is tracked in09-08-SUMMARY only after the current source acceptance gate passes.

## 09-09 internal notes and editable communication drafts

`GET/POST /records/{recordId}/notes` require internal-note-read/write and resolve the original typed operational subject. Notes contain bounded plain text (8000 UTF-16 code units), author label and creation time; they have no edit/delete route. SQL guards retain note identity and contents. Task comments retain the existing task event contract.

`GET/POST /records/{recordId}/threads` require message-read/write. A thread has a subject (maximum300), immutable author/time and either internal visibility with no relationshipId property, or agency visibility with an owned active relationshipId. Optional GET visibility filters both items and counts. These are internal staff APIs; an agency audience label does not grant external access. Existing agency sharing DTO projections never include notes or unsent drafts.

`GET/POST /threads/{threadId}/messages` list/create drafts. `GET/PUT /messages/{messageId}` read/update one draft and return its ETag in both header and DTO. PUT requires If-Match. Bodies may be empty while drafting, up to8000 characters; recipientContactIds and attachmentVersionIds are required unique arrays, bounded50/20. Agency recipients must be active owned contacts with valid plain email addresses. Internal threads accept no external recipients. Attachments must be exact ready document versions on the original subject; agency threads additionally require the same document audience relationship. Reads reauthorize original file scope. Ended saved recipients remain removable, but cannot be saved or replayed as current selections.

`GET /records/{recordId}/thread-relationships`, `/threads/{threadId}/recipient-options` and `/threads/{threadId}/attachment-options` return bounded current choices and an opaque nextCursor. Lists resolve current authority before counts/materialization. Commands require CSRF and Idempotency-Key and revalidate current subject/audience/selection authority before receipt replay. Selection-only changes advance the draft ETag. Conflict review loads the current saved draft separately; adopting its ETag is explicit and retains local text.

Migration `20260922083328_OperationalCommunication` adds InternalNote, OperationalThread, OperationalMessageDraft, MessageDraftRecipient and MessageDraftAttachment with relational ownership, creator and exact-version FKs. SQL guards enforce immutable note/thread history, bounded bodies, original audience, ready attachments, and immutable queued content/selections. Nonempty history prevents downgrade. Draft save does not queue or send anything; immutable delivery versions, outbox work and deterministic delivery belong to09-10.

### Operational delivery (09-10 implementation, acceptance in progress)

`POST /messages/{messageId}/send` accepts an empty object, the saved message ETag,
an idempotency key and CSRF token. It atomically freezes the saved agency message,
recipient names/addresses and exact ready document versions, then queues an
`operational-delivery` job. `POST /records/{recordId}/document-deliveries` accepts
`subject`, `body`, `recipientContactIds` and `documentVersionIds`; a pack requires
at least one file. Both return 202 with the job identity and Location. Acceptance
means queued, never delivered.

`GET /records/{recordId}/document-delivery-recipients/{relationshipId}` offers
paged active, valid-email contacts only after checking the relationship against
the original subject. No conversation must be created merely to send a pack.

History is available at `/messages/{messageId}/deliveries` and
`/records/{recordId}/document-deliveries`. Individual receipts and bounded attempt
histories are under `/message-deliveries/{deliveryId}` or
`/document-deliveries/{deliveryId}`, with `/attempts` for attempts. Private reads
use no-store. A delivery carries its own ETag. `/retry` and `/resend` require that
ETag and a reason. Retry expands the bounded budget on the same job and provider
operation. Resend creates a new receipt/job using the original exact snapshot;
new document versions never replace historical attachments. Current authorization
is rechecked before either action, including replay.

The development worker uses persistent deterministic scenarios (`success`,
`reject`, `transient-once`, `timeout-after-success`, `retry-required`). Its provider
transaction is independent of local application, so timeout/restart cannot create
a duplicate provider effect. It checks current sender, recipient and file access
before execution and application, verifies stored file bytes, and applies only an
owned lease and matching provider operation/scenario/hash/result. Changed duplicate
results are quarantined. If access changes after a provider effect, local state
becomes superseded while `providerOutcome` records what already happened.

Delivery status reads retain explicit parent/identity/audience authorization locks,
but do not retain shared mutable delivery locks while waiting for the work head.
Polling responses can become stale; recovery writes enforce the delivery ETag.
A terminal failure retains one job exception. Published workflow rules materialize
its task; a suspended sender's task can use an existing active authorized internal
owner, with the original sender retained in the source snapshot.

Storage migration: `20260922094628_OperationalDelivery`, with retained-source
triggers in `OperationalDelivery.Guards.cs`. The runtime flag is
`Cover:OperationalDeliveryWorkerEnabled` (development default true). Acceptance
hosts disable it when controlling leases explicitly. There is no real mail or
external transport.

### Historical occurrence producer (09-11)

`IncidentOccurrenceRules.Window` accepts a local date, `Europe/London`, and one of
`date`, `approximate` or `exact`. Approximate time serializes as `HH:mm` and remains
a hint; it does not narrow the full local day. London days can be23 or25hours.
Exact UTC instants must agree with the stated local date. `ExactLocal` rejects
nonexistent local times and requires a valid explicit offset for repeated times.
Applicability intervals are half-open; exact points do not gain cover at expiry.

`IncidentOccurrenceResolver.Resolve/ResolveHeld` authorizes the typed original
policy and uses `PolicyTemporalSelector` at the bounded knowledge cutoff. It splits
the claimed interval at retained version/term/cancellation boundaries and validates
the actual stored source hash. Its value contains policy, knownAt, factual window,
state and source-version/hash intervals. Date/approximate windows crossing versions
are ambiguous; gaps are partly-uncovered or uncovered. Missing facts and potentially
future windows are incomplete. These are readiness outcomes, never claim decisions.

The owning incident command in09-12 must bind and persist this value with its
immutable revision identity, and pass server time for a new knowledge cutoff.
Supplying a subject validates its retained IDs when one temporal source is resolved;
ambiguous source selection cannot authorize handoff. Final handoff also requires
complete factual incident fields and current original scope.

`IncidentSubjectRules.Ready` validates closed product branches against the retained
source, including MT vehicles/drivers, commercial property location and selected
subcover, and liability section/EL wage identity. Missing selections remain draft
facts. Supplied foreign identities or incompatible product fields fail.
`CommercialIncidentPayload.CreateResolved` emits `commercial-incident-2` with the
observed occurrence and applicability window. It never replaces approximate facts
with an invented observed instant. The prior exact-only `Create/Valid` contract
remains compatible with retained `commercial-incident-1` payloads.

## Saved incident reports (09-12)

`IncidentService` owns create/update, description-only saves, explicit occurrence
clarifications, historical resolution and logged-unsent state. Each content change
inserts an `IncidentRevision`; the prior facts, author, reason and content hash remain
unchanged. Resolving inserts `IncidentOccurrenceResolution` bound to that revision,
with normalized exact `IncidentResolutionSource` intervals. `IncidentEvidence`
retains each selected ready document version from the original policy. No operation
in this slice queues provider handoff or asserts that a claims administrator received it.

`GET /incidents` requires `policyId`. Detail and revision history check current internal
identity and original policy access. Signed cursors retain list position and knowledge
cutoff. Writes require CSRF and idempotency; mutations require the current incident
head ETag. Authorization and original evidence ownership precede receipt replay.
Drafts may be incomplete. Log requires occurrence, type, involvement (including
unknown), factual description, reporter/route/contact, valid owned subject and an
unambiguous historical source. Editing a logged-unsent report returns it to draft.

`PUT /incidents/{incidentId}/description` writes only description; other submitted
fields are rejected. `POST .../occurrence` accepts occurrence plus a reason.
`POST .../occurrence-resolution` and `POST .../log` accept an empty object. The former
returns the immutable resolution with the updated incident ETag; the latter returns
the incident as logged. Every response is private/no-store.

`GET .../subject-options?versionId=...` only reads a source present in the current
revision's stored resolution and verifies the original snapshot hash. Choosing a
candidate to view its vehicles, drivers, locations or occupations does not resolve
an ambiguous date. An explicit factual occurrence clarification is required.

The MT/CC policy Claims tabs provide draft and description saving, original revision
history, conditional product fields, readiness and exact evidence selection. Pending
transport commands retain their original body, key and ETag. Stale responses retain
local inputs and require reviewing the latest saved version before another write.
Browser acceptance passed for both products with SQL readback; see09-12-SUMMARY.
Provider handoff remains the09-13 consumer.

## Claims handoff and administrator summaries (09-13)

ClaimsHandoffService.Handoff(actor,incidentId,etag,revisionId,resolutionId,providerId,
log,key,token) owns both handoff and atomic log-and-handoff. It pins the complete saved
revision, resolved historical source, exact source hashes and administrator identity.
A new request is never derived from the latest policy pointer. ClaimsAdministrator is
seeded as a persistent demo identity, separate from capacity providers. No real transport.

ClaimsHandoff stores immutable request JSON/hash with mutable acknowledgement state.
ClaimsRequest binds each handoff, refresh or contact to one OutboxWork, stable operation
key, immutable payload/hash and scenario version. ClaimsSummary appends exact provider
operation/event/hash, reported AsOf and server ReceivedAt. Migration
20260922125221_OperationalClaims retains histories and blocks destructive downgrade.
Only definite provider rejection permits a corrected incident revision/new handoff;
unknown failures retain their exact revision and operation. Manual retry expands the
existing job budget. A terminal exception uses the shared unique workflow task binding.

ClaimsSnapshots captures facts without internal notes, policy-wide private JSON or
storage locators. Only selected ready insurer-visible document versions are disclosed
in the administrator projection. Agency-only/internal selections remain local with a
visible withheld count. Provider execution rechecks current original sender authority,
exact snapshot identity and actual ready file bytes. No automatic visibility promotion.
Commercial projections use commercial-incident-2; legacy adapter envelopes stay valid.

ClaimsHandoffWorker commits the deterministic provider effect separately from local
application. Apply requires exact retained result, operation/scenario/request hashes,
current authority and a live owned lease. Changed duplicates are quarantined. A revoked
actor after the provider effect blocks local summary application while preserving that
effect. No local settlement endpoint exists. Paid/reserve remain explicit null until
advised; null differs from zero. Summaries sort by AsOf, then ReceivedAt and stable ID.

POST /incidents/{id}/handoff and /log-and-handoff take revisionId,resolutionId,providerId.
POST /refresh takes an empty object; POST /contact takes body. Both require acknowledged
handoff. These mutations use incident ETag and idempotency/CSRF. POST
/incidents/{id}/requests/{requestId}/retry takes reason and the job ETag, retaining its
original sender and operation. GET /administrators provides available demo destinations;
GET /handoffs, /summaries and /requests expose signed, scoped history pagination. Generic
GET /jobs/{id} delegates claims authority to ClaimsSummaryService. All responses are
private/no-store. Incident detail/list includes the current revision's latest retained
administrator summary, with null-preserving money and its source chronology.

ClaimsSummaryPanel exposes submitted facts, administrator summary, recorded contact,
requests/attempts and retry. Unconfirmed UI writes retain actor/body/key/ETag, block
navigation and cannot be retried under another actor. The policy Claims table displays
stored reference/status/paid/reserve. Final acceptance evidence is recorded in09-13-SUMMARY
when complete; implementation alone does not establish phase completion.


##09-14 Motor Trade MID runtime

MidSubmissionRegistration.Register(workId) associates one exact retained PolicyMidIntent or
cancellation mid-removal consequence with its original OutboxWork. Initial issue now adds a
new-business PolicyMidIntent using the same mid-update envelope/key family. The explicit
RegisterMissingInitial(actor,versionId,reason,key) service supports missing-only retained demo
initial sources; no GET enqueues work and no historical source/receipt is rewritten.

MidSnapshots.Capture uses immutable source hashes and the prior slice or recorded issue
BaseVersionId. Vehicles require the explicit prototype.addveh.report-mid=true declaration;
trade plates come from covered tradePlates, never the merely held inventory. Registration
replacement removes old/adds new; reporting removal removes the prior entry; changed rows or
cover produce change; unchanged adjustment items produce no reportable changes. Renewal
updates surviving registrations for the new term. No CC reporting exists.

MidSubmission stores its own provider scenario, typed mid-submission-2 snapshot, base/source
version hashes and immutable items. Cancellation retains its original work scenario, key and
payload; its execution waits until effectiveAt. Normal servicing work receives its provider
scenario before registration. MidResult appends the exact provider event with nullable
reference for not-required. This state does not claim an accepted provider submission.

MidSubmissionWorker.ExecuteProvider/Apply persists a deterministic demo operation separately,
then applies only a matching retained result under current original actor authority and a
live owned lease. Expired leases cannot apply, identical results deduplicate, changed events
quarantine. Post-effect revocation retains the external demo effect but no authorized local
result. Original source versions and later issued versions remain untouched. One terminal
JobException maps to one actual policy workflow task. Retry requires current caller and
original sender authority, strong job ETag and the same immutable command/operation; only
transient exhaustion has an expanded retry budget.

GET /versions/{versionId}/mid-submissions uses current typed policy scope and signed paging;
POST /mid-submissions/{submissionId}/retry accepts only reason, CSRF, Idempotency-Key and job
If-Match. Both are private/no-store. Generic job reads delegate to the same source scope.
MidEndpoints, MidDispatcher and MidSubmissionService are wired in Program; development worker
flag Cover:OperationalMidWorkerEnabled defaults true. Read DTOs expose exact actions/effective
endsAt, state, safe reasons, attempts, retry ETag and exceptionTaskId. UI policy Vehicles and
vehicle version history use those persisted reads and freeze actor/body/key/ETag on uncertainty.

Persistence:20260922135419_OperationalMid plus OperationalMid.Guards.cs and EF snapshot.
Unique work/version/intent associations, original owned version FK, immutable requests/results,
provider provenance and registered work identity guards apply. Retained MID rows or initial
intents prevent destructive downgrade. External calls remain deterministic local demo effects.


## 09-15 cancellation operations (implemented)

`CancellationOperationsWorker.Apply(JobLease, CancellationToken)` owns the original
`cancellation-certificate-withdrawal` or `cancellation-task-close` work. Claiming and
application both check the pinned cancellation effective instant. Application holds current
original staff identity and typed policy scope before the child work lease, validates the
original notice, issued version, decision and sibling consequence envelope, then appends
`CancellationOperationalReceipt` with withdrawal or task events in one transaction.
MID remains the original09-14 `cancellation-mid-removal` operation and is never requeued here.
Commercial Combined has no MID and creates withdrawal only for covered Employers’ Liability.

`TaskService.CloseCancelledTerm` closes only open/in-progress/awaiting-information/blocked
renewal tasks whose immutable policy-term binding owns this cancelled term, whose subject
matches the policy and whose stored source-changed flag is false. It records cancelled,
not completed, and retains unsatisfied checklists in the canonical task event. Manual,
complaint, unrelated term and already terminal tasks remain untouched.
`CertificateWithdrawal` is term-level, with the original effectiveAt. Read metadata projects
that date onto every policy-certificate for the term, including a later historical render.
File content and download authorization remain unchanged.

`PrepareNotice(workId, MessageDeliveryService, token)` rechecks current original policy,
recipient and document source and registers one `CancellationNoticeDispatch` before the
existing delivery adapter's attachment rows. `MessageDeliveryService.Queue` has an optional
internal association callback, executed inside its caller's transaction after delivery
insertion and before attachments. This is the only extra visibility exception: the exact
internal cancellation-notice PDF, matching the cancellation version and original recipients,
can be attached to its specifically associated delivery. Generic sharing still requires
agency visibility and matching relationship. `DeliveryAuthority.HoldSender` rebuilds this
snapshot on each provider/application attempt. Original contact names/emails must still match.
The existing adapter handles persistent provider effects, expired leases and explicit retry.
Cancellation deliveries expose resendAllowed=false; a resend route returns409 before any effect.

`ApplyNotice` records the adapter-backed outcome against the original notice work; provider
failure remains visible on the child delivery and its existing exception task. An existing
`CancellationNoticeReceipt` remains immutable legacy evidence, and never registers a new
delivery. The old dispatcher is disabled when the new development dispatcher is enabled;
work fencing and symmetric SQL guards also prevent mixed workers creating both receipt types.
No-recipient notices fail visibly. Current source/actor failures become one original work
exception. Historical completed notice work is not reopened or rewritten.

GET `/api/v1/versions/{versionId}/cancellation-consequences` requires document-read under
current typed policy scope and returns at most four exact-version consequences, no arbitrary
query filters. It is private/no-store and returns404 for an unavailable version. Each result
contains original job identity, kind, effectiveAt, appliedAt, state, safe error code, exact
notice document/delivery, legacy receipt, MID submission, closed task IDs and exception task.
`CancellationConsequences` appears in Motor Trade and Commercial policy transaction/document
views; it reuses exact PDF preview and delivery recovery, and distinguishes a posted credit
from cash refund. Delivery details expose resendAllowed for all callers.

Program registers/maps `CancellationOperationsDispatcher`, `CancellationOperationsWorker`,
`CancellationOperationsService` and `CancellationOperationsEndpoints`. Development flag
`Cover:OperationalCancellationWorkerEnabled` defaults true. The document worker still generates
one source-bound PDF; the cancellation worker waits for it without inventing a delivery.
Migration `20260922144152_CancellationOperations` plus `CancellationOperations.Guards.cs`
adds the four immutable evidence tables, original ownership/time guards, the narrowly bound
attachment exception and mixed-worker legacy receipt exclusion. Retained operations block
migration downgrade. No retained demo database migration is performed by this slice.
##09-16 matching correspondence bridge (first sub-slice accepted)

LegacyOperationalBridge.MatchRequest(requestId, token) revalidates original current staff and submitting agency, registers its typed operational subject, then locks the source association after parent authority. ThreadService.ImportRecordedRequest creates an internal editable draft and MatchCorrespondence in one transaction. Existing associations return the original message without replacing edited content. Unresolved candidates never become recipients; the source MatchInformationRequest stays recorded. Migration20260922154416_LegacyOperationalLinks and Guards add immutable typed source/message FKs, uniqueness, audience/creator ownership and protected downgrade. No existing receipt is rewritten as sent.

OperationalDemoSeed.Initialize and the Development-only --seed-operational-demo command perform missing-only association. Optional Cover:LegacyOperationalWorkerEnabled processes newly recorded requests in bounded batches. GET matching information requests includes an optional current-scope correspondence association; the UI links to the actual /agents/{id}?tab=Messages workspace. Source state and draft state remain separate. No automatic send occurs.

This sub-slice passed .local/phase9-16-match-accepted:1 real-SQL/API/browser preservation scenario, five browser cases and SQL readback, revoked-actor denial, and the actual immutable trigger52040. Root420/frontend200, production build, lint and OpenAPI0errors104warnings passed. Failed port/link diagnostics are excluded. Remaining09-16 obligations are in its checkpoint; this is not full-plan acceptance.
##09-16 retained renewal lapse correspondence (second sub-slice accepted)

LegacyOperationalBridge.LapseNotice(actor,lapseId,token) creates one owned internal policy thread/draft using ThreadService.ImportInternal and an immutable RenewalLapseCorrespondence. The supplied current staff operator is recorded on the new association, separate from the historical manual/automatic event. Parent scope precedes the per-source lock and existing-link lookup. Repeated setup preserves staff edits; no recipient/delivery is created and no legacy receipt/work/term is altered. Migration20260922160618_LegacyRenewalCorrespondence and guards protect typed ownership, immutable association and downgrade.

InitializeRenewals(actor) is called by the explicit Development setup command with Cover:OperationalDemoOperator (default existing active senior-underwriter). It does not grant privileges. RenewalLifecycleView now includes nullable correspondenceMessageId under current policy/message authority. Its UI labels legacy notification records, links the real policy Messages view and states that the internal draft resends nothing. Policy initialTab now accepts Notes/Messages as well as the earlier transaction/document links; this fixes both direct navigation and reload. Original issued data remains unchanged.

Current .local/phase9-16-correspondence-current passed3unique real-SQL/API/browser cases with15browser checks/readbacks: matching plus manual/automatic lapse. Unit-level source semantics remain covered by normal existing services; focused RED lapse tests failed before implementation. Immutable SQL52045, original receipt hashes/attempts/expiry and staff edits are asserted. Root420/frontend200, current production build/typecheck, OpenAPI0errors104warnings passed; desktop/mobile screenshots inspected. This is sub-slice evidence, not full09-16 completion or retained demo migration.

##09-16 retained operational demonstration (implemented; final aggregate pending)

OperationalDemoQuoteSeed.PrepareCommercialIncident(actor, relationshipId, productVersionId, startsOn, token) uses current capture scope and published eligibility before discovering its immutable first-revision marker. Missing creation uses QuoteService; repeats return the same quote and preserve edits. The explicit Development CLI prepares only a quote. Normal API orchestration separately rates, reviews evidence/referrals, prepares/delivers terms, records acceptance and issues the additional historically applicable commercial policy. No authority grant is implied.

The retained incident orchestration uses IncidentService and ClaimsHandoff/Summary normal APIs and persistent local command journals. Original revision/resolution hashes, one handoff and append-only summaries are independently checked in SQL. Actual browser reads demonstrate unknown provider money without inventing zero. Original future commercial policy and original keys remain unchanged.

The Development-only --register-operational-mid-demo CLI resolves a current internal senior-underwriter, validates CoverMGA_Demo and calls existing MidSubmissionRegistration.RegisterMissingInitial followed by Register. Its stable initial version identity prevents duplicate intents and preserves existing work. The command does not alter privileges or synthesize a provider result.

The failure/retry setup appends immutable retry-required adapter setting versions during a bounded explicit local setup window and immediately restores the previous defaults by a newer version. Work pins its selected scenario. Only the recorded pack/MID NextAttemptAt values are compressed for demonstration, matching prior accepted retry fixtures; all actual lease, attempt, exception, workflow task, provider and application operations use normal hosted services. This is not real-duration backoff evidence. Both operations produced six failures, one exception task, then the real browser retry and seventh successful attempt with one provider identity.

Pack generation explicitly creates agency-visible copies from the original immutable policy source/template. Existing internal documents retain their audience and bytes. Delivery pins the exact three selected document/file hashes. Retry creates neither a resend nor replacement files. The independent retained SQL reporter asserts original work IDs, provider count, task count, seven attempts and three original file hashes. All journals remain local and contain fictional data only; do not delete them as a reset mechanism.

### Record task discovery

`GET /api/v1/tasks` additionally accepts optional `policyId` (UUID). Current policy scope is held before discovery in agency/identity/parent lock order. The result contains currently accessible tasks bound directly to that policy or to its servicing drafts; source-quote tasks remain in their own quote list. Existing filters intersect this set, paging binds the selected policy, and inaccessible policies return404. Invalid or duplicate policy IDs return400. No schema migration is required.

`RecordTasks` renders persistent policy/quote lists for both product families using the same task read models, state filters and paging. Policy tasks include servicing follow-up and retain each actual linked record. The existing scoped create command remains the only task creation path. Commercial policy Notes/Messages deep links retain the requested supported tab through the shared policy wrapper.

An optional `riskItemId` UUID requires `policyId`. This narrows discovery to workflow bindings whose original quote/servicing referral (or query decision's referral) has that stable risk-item identity. This specific mode also considers the policy's original quote; ordinary policy task discovery retains its existing direct-policy/servicing-only scope. Authorization still precedes filtering, and cursors bind both IDs. Driver history links use this query and navigate the actual persisted task, never a fabricated task identifier.

Client Claims selects from currently accessible policies scoped to the client, then reads the existing policy incident list and administrator summaries. Policy `tab=Tasks` and `tab=Claims` links retain their requested tab for both product families. `tab=Claims&incident=new` opens the normal incident editor; opening or cancelling this editor does not persist a draft. All saves retain the existing incident command, validation and occurrence-time source resolution.

## Source-audit read models (implementation under verification)

Task responses now include `relatedRecords`: at most three `{kind,id,label,href}` entries for the owned agency, insured account and issued policy. Primary subject identity remains unchanged. TaskPresentation loads typed parents and related labels in batches only after held parent authorization. A source quote's issued-policy link requires matching relationship/client/agency identities. Navigation reauthorizes at the destination; tasks do not grant underwriting authority.

Incident subject options include `policyContext` with the saved policy reference, historical insured name (company or individual), exact term/version href and declared section code/cover level/limit/excess. The existing owned occurrence-resolution candidate and source hash are checked first. Missing section limits/excesses remain unstated, not zero. Selecting an ambiguous candidate does not resolve cover or authorize handoff.

Administrator summaries add optional `liability`, `incurred`, `recoveryExpected`, `excessApplied` and `movementNote`. Money uses the existing exact nonnegative GBP format; narratives are bounded to300/1000/2000 characters. These are provider facts, not locally editable decisions. New nullable properties omit null when serializing the provider record, preserving older serialized outcomes and inbox hashes exactly. Existing SummaryJson and hashes are never rewritten. API projections and generated schemas expose the fields read-only; absent older values remain Not advised.

The explicit immutable `summary-details` demo setting yields fictional known summary values; the default success scenario retains unknown amounts. Saved incident `administratorName` comes from its immutable submitted handoff, so directory renames do not relabel history. No DB migration is required for these additive read models and optional JSON fields. Targeted tests cover legacy bytes, invalid values, SQL duplicate application, saved administrator identity and access revocation. Final source acceptance remains pending.
