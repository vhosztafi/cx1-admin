---
phase: 09-tasks-documents-communication-and-incidents
plan: '08'
status: complete
completed: 2026-09-22
requirements-completed: []
---

# Document/evidence UI and exact-version preview

Record documents now support actual generation, evidence upload, immutable version
history and exact-version preview/download across both Motor Trade products and
Commercial Combined. Task attachments persist links to exact saved versions.
Delivery and effective withdrawal retain their09-10/09-15 owners; no compound
OPS-04/05 or CC-05 requirement is marked complete by this slice.

## Delivered

- RecordDocuments composes policy, quote, agency and client-relationship document
  views. Policy selection pins policyVersionId; quotation actions pin revision and
  selected retained terms; servicing actions pin the selected prepared terms ID.
  Historical downloads never regenerate current cover. Shared UI shows source,
  file/version, size/type, availability and provenance, with safe download fallback.
- GET /records/{recordId}/documents/options validates exact typed source, product,
  kind and published applicable templates, with bounded source-bound cursors.
  Source metadata is composed in batches, including current version metadata.
- Document commands retain body, source/template, original upload bytes and key
  after uncertain responses. Actual upload finalization precedes version readiness.
  New versions preserve earlier hashes and history. Pending files have no preview
  action and their content endpoint rejects retrieval.
- Legacy quote/agency/servicing evidence retains its original typed download,
  evidence identity and review state. New evidence is not automatically accepted
  proof. Client entry selects an actual owned relationship.
- TaskDocumentAttachment stores exact DocumentVersionId, author/time/reason and
  retained removal provenance. Migration20260921215507_TaskDocumentAttachments,
  EF model/snapshot, filtered unique index and SQL guards enforce original parent,
  immutable identity and history retention. Downgrade refuses nonempty history.
- TaskService.AttachDocument, RemoveDocumentAttachment and ListDocumentAttachments
  use existing task scope, ETag, receipt and event machinery. Current original
  document authority precedes replay; new associations require ready content.
  Cross-parent, duplicate, stale, pending and revoked operations are rejected.
  Explicit null reasons now fail domain validation before database access.
- GET/POST /tasks/{taskId}/attachments and POST /tasks/{taskId}/attachments/{id}/remove
  enforce closed input, CSRF and appropriate preconditions. TaskAttachments offers
  an exact-version chooser, persisted list, preview/download and retained removal.
  OpenAPI/generated TypeScript and OPERATIONS-CONTRACTS reflect these signatures.

## Verification

Strict .local/phase9-08-final-strict verifies63 unique passing cases, including
12realSQL, no skips or duplicate identities. Raw reports are unchanged copies of:
phase9-08-final-unit (49), options-second (3), metadata-green (2),
null-reason-green (2), renewal-corrected (1), corrected-final (6).
The older options-api-green report is excluded because metadata-green repeats its
case. Earlier failed fixture/RED attempts remain separate, never counted as passes.

node scripts/verify-document-browser.mjs passed current normalized source hashes:
Motor Trade Road Risks22, Motor Trade Combined22, Commercial Combined22 and
renewal23 checks, each with SQL readback and no page errors. Manifests are under
.local/phase9-08-browser. Actual PDF/file hashes, replacement/original stability,
reload/history, pending409, foreign404, uncertain retry, exact quotation terms,
legacy bytes, task attach/remove and scoped quote/agency/relationship uploads pass.
Renewal generates an actual invitation from selected prepared terms in its workspace.

Frontend186/root417 tests, production build, lint and clean-exit OpenAPI validation
passed (100 existing OpenAPI warnings). Logs use .local/phase9-08-expanded-* and
phase9-08-openapi-recheck.log. Desktop and390px task/document screenshots were
inspected, including focus and preview fallback. Headless mobile PDF iframe does
not render; exact download works. Poppler parsed the11-page schedule and its first
page was visually inspected. Native PDF viewer rendering and human business or
assistive-technology UAT are not claimed.

## Review and continuity

09-08-REVIEW records the fixed null-reason and downgrade defects and corrected
isolated fixture setup. No unresolved HIGH/CRITICAL finding remains in this slice.
Implementation commits:5da42c8,7056c92,3076598,33d5510,46681b7.
No retained demo migration/reset, original key change or frontend-code edit occurred.
Preexisting next-env.d.ts/tsconfig.json changes remain excluded.

Next:09-09 notes and editable message drafts. Existing policy agency links use an
incorrect /agencies/ UI route; this preexisting navigation issue remains explicitly
tracked for Phase9 integration. Source ledger marks mixed Preview/Download/Send
occurrences partial until delivery exists. Final source acceptance remains09-18.
All acceptance processes from this slice have exited; do not repeat these suites
unless subsequent changes affect their behavior.
