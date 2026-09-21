# Phase 9: Tasks, documents, communication and incidents — Context

**Gathered:** 2026-09-21
**Status:** Ready for research and planning
**Mode:** Single-pass automatic discussion under the user's standing approval. Choices below are agent-selected defaults; no new user answers are claimed.

<domain>
## Phase boundary

Implement OPS-01..08: personal/team task queues and workflow tasks; internal notes and scoped agency communications; actual versioned documents and evidence downloads; document packs and delivery recovery; Motor Trade/Commercial Combined incident logging and deterministic claims summaries; Motor Trade MID exceptions and retries. Complete the Phase 9 portion of POL-01 and all of CC-05. Finance reconciliation remains Phase 10, configuration administration Phase 11, reporting/search breadth Phase 12 and final milestone UAT Phase 13.

The initial global inventory assigns 62 controls directly to Phase 9 (33 task, 23 incident/claim and six task-modal controls). This is a starting subset, not a complete denominator: inherited policy/client/agency/quote tabs, modal variants, display items, earlier explicit Phase 9 owners and integration consequences must be reconciled during research. Preserve original IDs, source hash and occurrence identities. No sales-funnel changes or real external communication.
</domain>

<decisions>
## Decisions

### Operational record ownership
- **D-01:** Deliver all eight OPS requirements as real saved workflows, with actual navigable task/document/message/incident/MID records. Include the explicit operational obligations from Phases 3–8, especially match information requests, agency follow-ups, exact policy document requests, cancellation consequences and CC incident/document contracts. A queued request is never labelled completed, generated or sent.
- **D-02:** Reuse current identity, agency/client relationship, policy/term/version and quote/draft ownership. Shared operational links must resolve a real typed parent under current scope; arbitrary entity-kind/ID pairs cannot authorize reads or writes. Research the planned WorkRecord supertype against the implemented model before choosing additive registration/backfill constraints. Never introduce a second policy graph or rewrite issued JSON.

### Tasks and notes
- **D-03:** Implement the source personal/team/created/completed views, filters, linked-record navigation, task creation, type/priority/owner/team/due/status changes, comments, checklist and reasoned completion. Task history is durable. Assignment must use currently eligible users/teams. Bounded bulk actions validate all selected records and concurrency before atomic application; failures preserve the selection and explain the conflict.
- **D-04:** Published workflow rules create tasks once per owned source event and rule identity; store provenance and the rule snapshot. Include referrals, missing information, renewals, agency evidence and integration exceptions. Retries/polls must not duplicate tasks or reopen completed work. New source events can create distinct work; resolved underlying conditions cannot be disguised as completed human review. Research completion/checklist semantics per source task type.
- **D-05:** Internal notes and task comments are append-only records, or append-only revisions if source editing exists. Preserve author/time and current visibility. Never expose internal underwriting notes, support flags or foreign-agency records through thread, document, task or incident projections.

### Files, documents and communication
- **D-06:** Provide actual uploaded/downloadable bytes with immutable versions, hashes, safe filenames and current authorization on every read. Follow the accepted durable local-file-storage boundary with SQL metadata and explicit temporary/ready/quarantined states; reconcile existing evidence storage rather than replacing retained evidence. Enforce bounded allowed content, never trust MIME/extension/path input, and label deterministic demo screening honestly. Do not expose raw storage keys or delete referenced issued files.
- **D-07:** Generate real previewable/downloadable policy, quote, renewal and cancellation documents from exact retained source and template versions. Preserve original output bytes and hashes; reconstruction is a distinct version/request. Use closed product-specific projections, including CC property/BI/liability schedules and EL applicability, without motor fields on CC. Existing pre-08-15 requests are read from their exact retained sources without rewriting historical envelopes. Research a suitable PDF renderer and font/template strategy behind a replaceable .NET boundary.
- **D-08:** Distinguish internal notes from agency-visible threads. Draft messages may be edited; queued message content, recipient snapshot and attachment versions are immutable. The server validates each recipient and attachment against the audience and current relationship. Delivery uses persistent deterministic adapters, exact operation identity and attempt history. Retry the same uncertain/failed operation; an explicit resend is a new delivery of the same chosen document versions. Never silently regenerate historical documents or broaden their audience.

### Incidents and integrations
- **D-09:** Implement save-without-send, log-and-handoff, drafts, owned policy/risk selection, loss date/optional approximate time, incident type, location, involvement/ownership/value, third-party/police information, description, reporter/contact and route from the prototype. Resolve historical cover at occurrence using effective/known-at policy selection, not current risk. Preserve date/time precision and unknown values. If an imprecise date spans multiple materially different policy versions, require explicit disambiguation rather than inventing an exact occurrence time. Motor Trade subjects and CC property/liability/location/occupation subjects use distinct closed contracts.
- **D-10:** Claims handoff records a durable exact request, attempt/result and append-only administrator summaries with provenance. Contact-administrator actions use the same recorded demo communication boundary. Paid/reserved/status fields are provider summaries, not local settlement powers. Correcting a definitively rejected handoff is an explicit new revision/operation; replay cannot retarget a later policy version.
- **D-11:** Consume exact owned Motor Trade MID intents for applicable vehicles/trade plates, including issue/servicing/cancellation sources after source audit. Keep add/change/remove, effective dates, selected version, request/response history, reasons, exceptions and authorized retry visible. No MID for CC. Never apply an old response to a newer issued version or treat a queued intent as an accepted submission.
- **D-12:** Implement existing cancellation notice/certificate-withdrawal/task-close consequences at their correct effective boundaries. Notices may be delivered earlier when permitted; certificate withdrawal and task closure must not complete early. Deduplicate exception tasks and worker effects across crashes, lease loss and repeats. Workers recheck current applicable authority and exact owned source before applying an effect; revoked access cannot be bypassed by recovery.

### UI, demo and acceptance
- **D-13:** Preserve prototype typography, colors, density, shell and tab layouts, including working task list/detail, incident entry/detail and shared notes/documents/messages/claims views. Use explicit empty/loading/conflict/retry states; retain uncertain input and exact request identity. Desktop and 390px layouts, keyboard controls, focus return and readable previews must work. No invented customer portal or implementation jargon in normal business flows.
- **D-14:** Add realistic fictional tasks, files, messages, incident/provider summaries and failure/retry scenarios through normal services or missing-only seeds. Preserve existing edits, immutable versions, original keys and existing demo policy references. No real email, claims/MID connection, payment, destructive reset or regulatory certification.
- **D-15:** Split implementation into manageable sequential vertical plans with concrete data/API/UI ownership. Require meaningful unit and real-SQL/API negatives for scope, audience, files, concurrency, rollback and retries; actual browser readbacks, rendered PDF inspection and worker crash/restart tests; source-ID coverage; final full current backend, retained Motor Trade/CC regression and additive initialization/actual restart preservation. Do not inflate totals with repeated cases, skip SQL or call schema validation runtime proof.

### Agent discretion
The user delegated research, planning, engineering choices and progression. Select exact schemas, migrations, renderer package, safe template representation, endpoint names and plan count from source/code/official documentation. Keep existing module boundaries and sequential inline execution; no routine confirmation or subagents. Human business/assistive-technology UAT remains explicitly unperformed until actually undertaken.
</decisions>

<canonical_refs>
## Canonical references

- `.planning/PROJECT.md`, `.planning/REQUIREMENTS.md`, `.planning/ROADMAP.md`, `.planning/ACCEPTANCE-BACKLOG.md` — approved scope, autonomy and compound obligations.
- `.planning/phases/08-commercial-combined-back-office/08-PHASE09-HANDOFF.md`, `.planning/phases/08-commercial-combined-back-office/08-VERIFICATION.md`, `.planning/phases/08-commercial-combined-back-office/08-SOURCE-INVENTORY.json` — completed CC contracts and retained future owners.
- `.planning/phases/07-policy-lifecycle-and-history/07-SOURCE-INVENTORY.json`, `.planning/phases/07-policy-lifecycle-and-history/07-VERIFICATION.md` — shared policy operations still owned by Phase 9.
- `docs/prototype/Cover MGA Back Office-4.html`, `docs/design/source/prototype-template.txt`, `docs/design/source/prototype-render-data.json`, `docs/design/source/prototype-evidence.json`, `docs/design/control-inventory.json`, `docs/design/api-control-map.json`, `docs/design/reviewed-api-controls.json` — original controls, handlers, modal fields and rendered occurrences. The exported HTML contains very long encoded lines; use bounded decoded source extraction.
- `docs/design/DATA-MODEL.md`, `docs/design/LIFECYCLE.md`, `docs/design/API-CONVENTIONS.md`, `docs/design/PERMISSIONS.md`, `docs/design/ADAPTERS.md` — ownership, commands, local storage and durable effects.
- `docs/design/UNDERWRITING-CONTRACTS.md`, `docs/design/SERVICING-CONTRACTS.md`, `docs/design/COMMERCIAL-COMBINED-CONTRACTS.md`, `docs/design/COMMERCIAL-EXPOSURE.md` — immutable provenance and temporal/product boundaries.
- `contracts/openapi.json`, `contracts/schemas/`, `docs/SETUP.md`, `docs/DEMO.md` — closed generated contracts and retained demo/runtime practices.
- `frontend-code/` — read-only Motor Trade reference if field semantics require it.
</canonical_refs>

<code_context>
## Existing code insights

- `backend/src/BackOffice.Infrastructure/Persistence/PolicyRecords.cs`: PolicyDocumentRequest already pins policy/term/transaction/version/template, payload hash and OutboxWork. It is not a generated DocumentVersion.
- `backend/src/BackOffice.Infrastructure/Persistence/ServicingIssueRecords.cs`: PolicyMidIntent already stores immutable version-linked work/payload/hash. Phase 9 must apply rather than duplicate those intents.
- `backend/src/BackOffice.Infrastructure/Persistence/FoundationRecords.cs`: OutboxWork, AdapterAttempt, DemoProviderOperation, audit and JobException provide durable operations, lease and outcome primitives. JobException is not a business task.
- `backend/src/BackOffice.Infrastructure/Policies/CommercialDocumentRequestPayload.cs`, `backend/src/BackOffice.Application/Policies/CommercialDocumentPayload.cs`, `backend/src/BackOffice.Application/Policies/CommercialIncidentPayload.cs`: use exact CC projections and selected-cover/subject validation. Occurrence precision needs explicit API treatment; the existing builder expects an exact instant.
- `backend/src/BackOffice.Infrastructure/Policies/ServicingDocuments.cs`: terms history is already scoped and preserves delivery attempts; do not replace it with fabricated generated files.
- Existing agency/quote/servicing evidence records retain files and immutable proof ownership. General-purpose file/document handling needs an additive bridge and independently authorized projections.
- `apps/backoffice/lib/operations.ts` and the existing operations console are infrastructure job/audit recovery, not a task/incident workspace. Reuse error handling and exact retry patterns while implementing business-specific pages.
</code_context>

<specifics>
Use a complete fictional operational story: issued policy schedules become actual files; a failed pack delivery creates one exception task and recovers without a new policy/document version; a historical Motor Trade incident and a CC property/liability incident hand off and return saved administrator summaries; a failed vehicle MID operation retries without changing later policy history. Cancellation consequences wait for their effective time.
</specifics>

<deferred>
Finance allocation/refund payment/bordereau administration remains Phase 10; general workflow/template/role administration remains Phase 11; aggregate reporting remains Phase 12. Published rules/templates needed by consumers are seeded here. Full claims settlement, real providers, Entra integration and new customer funnels remain outside this MVP.
</deferred>
