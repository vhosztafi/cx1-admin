---
phase: 06-underwriting-and-first-policy-issue
plan: '08'
status: complete
completed: 2026-09-17
implementation_commit: 0ba6132
requirements_completed: []
---

# 06-08 — Immutable quotation, delivery and acceptance

Implemented in `0ba6132`. Both Motor Trade products now prepare immutable
structured terms, require exact-version signed proof, queue scoped demo delivery
and record actual named acceptance against delivered terms and reviewed evidence.
Current identity, agency, grant, rating, template, recipient and proof checks
protect progression and receipt replay. This is the backend slice; browser UI
belongs to06-09, first policy storage/issue to06-10..12.

## Persistence and contractual boundaries

- Migration `20260917013041_QuoteTermsDeliveryStorage` adds template, terms,
  delivery and acceptance records and three nullable same-owner cycle pointers.
  Composite keys, JSON/state/time constraints and SQL triggers enforce exact
  ownership, append-only contractual history and immutable delivery requests.
- Forward SQL inspected in `.local/phase6-08-migration-forward.sql`. Fresh and
  retained-upgrade SQL tests pass. Applied to preserved CoverMGA_Demo and repeated
  initialization: all23retained counts/SHA256 hashes match, including credentials,
  quote/revision/files/cycles/rating/decision/condition/capacity history. Evidence:
  `.local/phase6-08-preservation-{before,after,repeat}.txt`, preservation.sql and
  `.local/phase6-08-demo-{migration,repeat}.log`. No reset or historical rewrite.
- Required documentary review affects assurance without recursively changing
  contractual terms. Re-preparation after signature returns the same terms.
  New warranties require new terms/signature/delivery/acceptance; earlier facts
  remain immutable. Actual acceptance proof exists before hashing assurance.
- Scoped active contact IDs resolve to retained name/email snapshots. Server-
  selected versioned scenarios support success, rejection, transient failure and
  timeout after durable success. Provider execution and application are separate;
  lost leases/retries reuse one original operation and retain every attempt.
- Application rechecks current recipients, actor, template, signing proof and
  assurance. Superseded context cannot produce a sent quote. Bounded original-job
  recovery requires quote-terms plus integration-retry; revocation denies replay.
- Four strict terms/acceptance endpoints and scoped job/read/retry integration
  are active. Protected independent history cursors bind to this quote/delivery
  generation, avoiding unrelated database rowversion churn. Read projections
  expose structured cover, rating, settlement, actual proof and safe options.

## Measured verification

- Full backend `.local/phase6-08-backend-full`: **799 passing cases**,628unit and
  171integration, including **144realSQL**, zero skips. Unit duration7s;
  integration30m20s. `assert-test-results.ps1` passed MinimumTests799/
  MinimumSqlTests144. Full log `.local/phase6-08-backend-full.log`.
- New14pure acceptance cases and18SQL scenarios cover both products, same terms
  after signature, durable provider scenarios/lost lease, failed-job recovery,
  exact/current receipt replay, withdrawn/unreviewed/foreign proof, expiry,
  runtime/template/recipient/actor changes, warranty invalidation, fresh/retained
  migration, append-only constraints and strict HTTP/CSRF/ETag/paging behavior.
- Targeted evidence `.local/phase6-08-targeted-1`, targeted-2, final-additions,
  api-final; early fixture/schema/projection failures were corrected and the full
  final suite passes. Fixes include array JSON constraints, operation-key case,
  tracked quote transitions, safe cloned JSON and quote-specific history versions.
-317contract/source tests pass `.local/phase6-08-all-contracts.log`; generated
  OpenAPI355operations validates with10existingwarnings in phase6-08-openapi.log.
  Contract generation and diff checks pass. frontend-code remains unchanged.

## Handoff and limits

06-09 consumes current terms/hash/acceptance capabilities and GETterms options.
Upload and review acceptance proof after applied delivery to avoid invalidating
queued delivery assurance. Keep frozen key/body/ETag on uncertain requests.
The API exposes structured payloads, not fabricated PDF download links. Generic
document generation remainsPhase9. No external messages, real payment, policy
issue, deployment, hostedCI, Docker or humanUAT is claimed. Compound requirements
remain open until06-14; the conditional escalation source supplement also remains
an explicit06-14completion gate.
