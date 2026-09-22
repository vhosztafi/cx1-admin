---
phase: 09-tasks-documents-communication-and-incidents
plan: '09'
status: complete
completed: 2026-09-22
requirements-completed: []
---

# Internal notes and editable message drafts

Internal notes and scoped conversations now persist on policy, quote, agency and
client-relationship records for Motor Trade and Commercial Combined. Saved drafts
retain exact recipients and file versions; sending remains09-10. OPS-03 is compound
and is not marked complete before delivery.

## Delivered

- Separate append-only InternalNote storage, author/time, scoped list and count;
  existing task comments retain their existing immutable task event store.
- Immutable OperationalThread subject/audience/relationship identity, editable
  OperationalMessageDraft with rowversion, normalized MessageDraftRecipient and
  MessageDraftAttachment FKs. Migration20260922083328_OperationalCommunication and
  OperationalCommunication.Guards enforce original audience, bounded bodies,
  retained history, ready attachments and queued-content/selection immutability.
  Downgrade refuses nonempty history; model/snapshot have no pending difference.
- NoteService.Add/List; ThreadService.Create/CreateDraft/UpdateDraft/ReadDraft/
  List/Messages/Relationships/RecipientOptions/AttachmentOptions. Closed routes
  and final signatures are documented in OPERATIONS-CONTRACTS. Commands validate
  current original parent/actor/audience/selection before receipt replay; recipient
  ending or lost sender scope prevents replay disclosure. Selection-only updates
  advance ETag. Foreign/internal/unready attachment writes are rejected.
- Internal-only capabilities do not broaden parent access. Actual agency sharing
  client/contact/instruction projections and counts remain identical before/after
  internal note/draft creation. Thread counts cannot enumerate internal notes.
- Notes/Messages tabs and typed client-relationship selection use saved scope.
  Exact document history/head actions open agency composers with pinned versions.
  Native confirmation preserves exact bytes/key on lost responses. Stale edits
  preserve local text and require explicit current-version review/adoption.
- Shared field/checkbox styles now work in both product layouts at390px. Fixed
  existing policy/history agency links to the actual /agents UI route.

## Evidence

.local/phase9-09-final-strict verifies16unique passing cases/4realSQL/no skips from
unmodified unit-final12, browser-motor-conflict1 and final-sql-commercial3 reports.
This includes API scope/replay/CSRF/ETag/projections/options, actual oversized SQL
body rejection, immutable note/thread/queued associations and protected downgrade.
The later presentation-only correction is independently verified by2passing cases
in .local/phase9-09-styled-browser; these repeat browser identities and are not added
to the unique16count. No unchanged SQL suite was rerun for CSS.

node scripts/verify-communication-browser.mjs validates current source fingerprints,
13checks each for Motor Trade and Commercial Combined plus independent SQL readback.
Manifests: .local/phase9-09-browser/{motor-trade,commercial-combined}.json. Checks
cover note lost-response retry, persisted author/time, separate counts, exact file
composer, recipient choices, mobile overflow, draft reload, stale412retained text,
explicit fresh-version save and quote/agency/relationship notes. SQL independently
checks4notes, exact draft body/contact/file and unchanged policy hash. No page errors.

12focused backend units,417root and190frontend cases passed. Production build and
lint passed after the shared styling correction; build includes TypeScript checking.
Earlier standalone typecheck passed. OpenAPI exits0 with101warnings. Desktop notes
and final390px composers for both products were visually inspected. No human
business/assistive-technology UAT or live transport is claimed.

Permission RED4 preceded GREEN4; corrected API RED reached404instead of expected201
before endpoints existed. Earlier fixture/harness failures are retained separately,
never counted as acceptance. Browser waits were corrected for asynchronously loaded
options, populated implicit labels and Chrome's unused error-body retrieval.

## Review, commits and next boundary

09-09-REVIEW records addressed findings; no unresolved HIGH/CRITICAL issue found.
Commits5c4b2ec backend/storage/API and fafd789 UI/contracts/browser. No retained demo
migration, original-key changes or frontend-code edits. Existing next-env.d.ts and
tsconfig.json working changes remain excluded. All acceptance sessions completed.

09-10 must create immutable MessageVersion/content/recipient/address/exact-file
snapshots and durable work atomically. Current sender/recipient/file authority must
be checked before provider effects and apply; retries preserve operation identity,
resends create new operations. Read09-10-INTEGRATION-NOTES and canonical plan/docs.
Final source placement/seeded-history reconciliation remains09-17/18.
