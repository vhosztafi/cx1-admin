---
phase: 09-tasks-documents-communication-and-incidents
plan: '07'
status: complete
completed: 2026-09-21
requirements-completed: []
---

# Durable document generation and immutable versions

Issued policy, adjustment, renewal and cancellation sources now produce durable
PDFs. Explicit quotation/statement and servicing-terms generation, regeneration,
uploaded evidence composition, scoped history and exact-version downloads are
implemented.09-08 owns the document/evidence UI;09-10 owns delivery and09-15
effective withdrawal. OPS-04/05 and CC-05 remain compound, incomplete requirements.

## Delivered

- Migration `20260921183129_OperationalDocuments`, `OperationalDocumentModel`
  and storage guards persist Document, DocumentVersion and DocumentVersionContent.
  Exclusive typed sources, exact hashes/template/source IDs, unique request/work
  associations, version numbering and single content bindings are enforced.
  Versions/content/audience history are immutable; unsafe downgrade refuses.
- `DocumentService.RegisterRetainedRequest` preserves the original
  PolicyDocumentRequest/work/creator/envelope. `DocumentGenerationWorker.Process`
  stages actual rendered bytes, commits their binding, finalizes storage and only
  then marks generation ready. Current parent/identity checks precede replay,
  rendering, publication and download. Concurrent recovery after render, content
  commit or finalization keeps one version/content binding. Retry limits and safe
  failure/exception records remain separate from delivery.
- `DocumentService.Generate` accepts a closed `DocumentGenerateInput` with exact
  policy version, quotation revision/optional saved terms, or servicing terms;
  selected product/kind/template/source graphs are validated. New generation
  requires a published/date-applicable template. Existing receipts and ready bytes
  retain historical templates. Optional documentId appends a serialized version;
  earlier PDFs are never regenerated for reads or replaced.
- `PolicyDocumentRenderService.LoadSelected` and `RenderSelectedVersion` reuse
  held exact-source validation, including stored rating/terms hashes, quote cycle
  ownership, servicing effective slices, commercial projections and template pins.
  Actual issued requests from both Motor Trade products and Commercial Combined
  first issue, adjustments and renewals are generated with unchanged envelopes.
- `RegisterCancellationNotice` validates the original consequence, issue decision,
  transaction, version, notice hash and original delivery work. A unique consequence
  association creates separate technical PDF work; even an already delivered
  notice's original work, payload and receipt remain unchanged. The selected
  cancellation template is pinned once. Existing non-cancellation generation
  envelopes remain byte-compatible. Six missing-only baseline endorsement/notice
  templates precede the15 future templates without changing policy selectors.
- Development-only `DocumentGenerationDispatcher` is registered through
  `Cover:DocumentWorkerEnabled` (default false). Bounded sweeps discover original
  policy requests and cancellation notices and process pending explicit work.
  It uses the current original actor and never sends documents. The normal hosted
  policy path and cancellation path are tested, including application host restart.
- `DocumentService.AttachUpload` attaches the existing public file-upload work ID,
  original bytes/hash/creator and finalization work. No second byte upload or PDF
  generation work is invented. Reusing identical upload metadata deduplicates;
  conflicting metadata, private file IDs, foreign parents and revoked replay are
  rejected. A replacement upload appends a version. This refines the provisional
  multipart document command into JSON composition of the existing stream API.
- `DocumentService.ListDocuments`, `ListVersions`, `ReadVersion` and
  `DownloadVersion` expose current scope/audience and actual file/work availability.
  Opaque cursors use immutable created-at/ID or version-sequence positions, including
  a new version between pages. Damaged/missing bytes are quarantined and audited.
- `DocumentEndpoints` wires list/history/metadata/content/preview/generate/upload
  under `/api/v1`. Commands enforce CSRF, idempotency and closed bounded input;
 202 version receipts include Location. Reads use private/no-store and nosniff,
  safe filenames and exact ready streams. OpenAPI, operational JSON schemas and
  generated TypeScript match the public contracts; SETUP documents worker/storage.

Legacy agency/quote/servicing evidence keeps its original typed storage bridge,
download API, IDs, reviews and attestations from09-05.09-08 composes those links;
there is no fabricated legacy import work or claim that UI history is finished.

## Verification

Final current-source `.local/phase9-07-final-strict` passes40 unique cases:
14unit and26realSQL integration cases, zero skips. `scripts/assert-test-results.ps1`
verified both reports. SQL finished in23m48s; session43341 is complete and must not
be restarted. Root417 pass in `.local/phase9-07-cancellation-root.log`.

The SQL gate covers immutable storage/migration downgrade, three crash boundaries
with concurrent recovery, hosted dispatch/restart, exact API bytes and quarantine,
CSRF/closed input/authorization/replay, stable paged history, replacement uploads,
explicit policy regeneration, both MT and CC quotations, proposed servicing terms,
all six issued adjustment/renewal product combinations, all three cancellations,
extended file API restart and additive templates. Original delivery receipts and
policy request envelopes are compared unchanged. No retained demo migration or
seed was run, and no full Phase9/browser acceptance is inferred.

Independent pypdf parsing of45 current-run issued-family PDFs (3–13pages) checks
A4 geometry, extractable fictional/source/template labels and every-page footer:
`.local/phase9-07-issued-pdf-parser.json`. Poppler first/last images of a13-page
commercial schedule are `.local/phase9-07-issued-first.png` and
`.local/phase9-07-issued-last.png`; inspected with no observed clipping/overlap.
pdfinfo reports no JavaScript/forms/encryption for the sample. Earlier09-06
renderer-specific parsed/visual proofs remain applicable. These are not human UAT,
legal approval or PDF-UA certification.

RED evidence includes missing generation/upload/cancellation symbols, missing API
routes and outdated response contracts before implementation. The issued-family
first run had4passes/2failures because new adjustment callbacks forwarded the
pre-takeover fixture lease; the current gate verifies the corrected underwriter
lease. A sandbox cancellation run could not negotiate SQL encryption; its native
rerun passed all3 cases. No unresolved implementation failure remains.

## Review and next work

Reviewed current access before receipt disclosure, typed graph ownership,
immutable bytes/envelopes, audience checks, bounded reads/retries, lock ordering,
and separation of cancellation PDF generation from delivery. Test callbacks are
optional and preserve existing helper paths when omitted. No HIGH/CRITICAL finding
remains. Implementation increments run from4f75d15 through8766ba9; final issued
family checks and this record accompany the completion commit.

Continue09-08 with its plan and `09-08-INTEGRATION-NOTES.md`. Discovery identified
server-backed template/source selection, table metadata composition and persisted
task attachments as UI integration work, not already implemented features. Keep
frontend-code, CoverMGA_Demo, preview processes/data-protection keys and the two
pre-existing generated Next files unchanged. Remaining phases and the full MVP
are still in progress.
