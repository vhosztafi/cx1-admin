# Document UI integration discovery

Prepared while09-07's final SQL gate runs. This is implementation context, not
09-08 completion or browser evidence. Preserve09-UI-SPEC and existing primitives.

- Existing quote routes render `quotes/quote-receipt.tsx`, with a dynamic
  `quotes/commercial-receipt.tsx` branch. Add a shared Documents surface there;
  keep quote editing/evidence assessment in the existing wizard. The historical
  quote revision selection must remain explicit when generating a document.
- Motor Trade `policies/policy-record.tsx` and Commercial Combined
  `policies/commercial-policy-record.tsx` contain the retained request tables.
  Replace their generation placeholders with actual version availability. Pass
  the selected `policy.versionId`, including historical navigation, rather than
  selecting the current/latest policy version. Existing request identity remains
  visible when a request has not yet been imported by the worker.
- `agencies/agency-detail.tsx` is mounted under `/agents/[agencyId]`; API paths
  remain `/agencies`. `clients/client-detail.tsx` owns paged agency relationships.
  Client documents must select an actual relationship; a free client ID is not
  an operational subject. There is no separate relationship detail route.
- `operations/task-detail.tsx` has a literal Attachments placeholder. Its
  `task.subject` is the typed parent; task visibility alone must not authorize
  an attachment from another parent. Discover the persisted task evidence link
  contract before implementing attachment commands; do not substitute unrelated
  record-wide files for saved task attachments.
  Inspection confirms `OperationalTaskRecords` and `TaskEndpoints` do not yet
  persist or expose attachments. Add the bounded association, migration and
  contract with current task plus original document/evidence scope. Use immutable
  version links; attaching must not copy file bytes or change an evidence review.
- Existing typed evidence downloads are
  `/quotes/{quoteId}/evidence-files/{fileId}/content`,
  `/drafts/{draftId}/evidence-files/{fileId}/content`, and
  `/agencies/{agencyId}/evidence-files/{fileId}/content` under `/api/v1`.
  Keep original IDs, review/withdrawal states and current source authorization.
  Do not fabricate DocumentVersion rows or technical import work for these files.
-09-07 generation requires exact source/template IDs. The UI needs an authorized
  server selection of applicable published templates and source combinations;
  do not ask users to type UUIDs or hard-code demo template IDs. Existing quote
  terms and servicing terms reads expose some selected template identities, but
  there is no general document-options endpoint yet. Any necessary read contract
  belongs to this UI composition with OpenAPI/generated types and SQL/API tests.
  Prefer bounded valid combinations derived through the existing exact-source
  rules, including quotation terms/template pins and CC EL applicability. New
  generation requires a currently published/date-applicable template; reading
  an old ready file continues to use its retained bytes. Agency/relationship
  surfaces support uploads without inventing a policy source.
- Current document list heads expose `currentVersionId`; individual metadata
  exposes source/template IDs but no readable source label/date. Compose the
  existing scoped list with bounded current-version metadata for the table,
  rather than fetching each row separately. Derive source labels/dates from the
  exact retained FK and keep IDs/hashes in expandable provenance. Extend the
  generated read contract and tests together; do not silently reinterpret a
  historical parent's selected version as the latest source.
- Ready previews use only `/document-versions/{id}/preview`, with metadata and
  download fallback. Pending/failed/quarantined files cannot open preview.
  Use current file-version metadata and opaque list/history cursors; never use a
  mutable "latest" URL in an open preview. Close restores focus.
- Upload composition is two commands: stream to file-uploads, then attach the
  returned public upload ID through `/records/{id}/documents/upload`. Preserve
  uncertain command identity and the selected File until confirmed. A replacement
  uploads new bytes and appends an immutable document version.

Read the frontend-design and React performance skills before implementation.
Actual API and browser acceptance must include new version/old-byte stability,
scope denial, reload, real file bytes, 390px and keyboard/focus recovery. The final
phase owner handles withdrawal/delivery states as their endpoints are implemented;
this slice must not claim a message was sent merely because a PDF exists.

## Task attachment refinement

The existing task model has no persisted attachments. Add an explicit
TaskDocumentAttachment association to an exact DocumentVersion; do not display
the parent record's entire document list as task attachments. Default deny a
different original subject, even when the actor can read both records. This
matches the existing cross-subject attachment boundary. Require current
task-write and original document-read before command receipt replay, a ready
version before adding, the task ETag, and a reason. Limit active links to20.
Remove only the association with retained removal provenance; never delete or
rewrite document bytes. A later reattachment creates a new association while
retaining the removed row. A filtered unique index prevents duplicate active
task/version pairs. SQL guards reject cross-parent inserts and identity changes.
Task events/audit and updated task ETag commit with the association. Reads and
downloads recheck original scope. Use the existing TaskService mutation path
with an authorization callback before child locks; no new task service or
identity provider is needed.
