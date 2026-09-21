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
| FileObject | UUID, storage-kind discriminator, one local key OR one typed legacy Agency/Quote/ServicingEvidenceFile FK, byte count int≤20MiB, binary32 SHA256, detected media, safe display filename, pending/ready/quarantined state. CHECK exclusive sources; legacy FK unique. No public keys. |
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

Generated from the final OpenAPI contract. All entries remain runtime pending. Write commands require session and CSRF; operation scope is rechecked before receipt replay. Bounded Problem responses use the common API conventions (400 malformed input,403 forbidden,404 unavailable subject,409 conflict,412 stale,422 invalid transition/readiness,428 missing version). Bulk uses each selected head ETag. A queued202 response never means the adapter completed.

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
| listMidSubmissions | GET /versions/{versionId}/mid-submissions | policy-read | none / per-selection for bulk | not-cached |
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
