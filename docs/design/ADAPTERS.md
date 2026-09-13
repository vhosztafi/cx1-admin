# Durable adapter contracts v1

All adapters are persistent deterministic demo implementations. No real email, payment, regulator, MID or insurer connection is activated. Display the demo mode and delivery outcome in the appropriate business UI. Persist scenario choice, request identity, rule/template version, attempts and final result so restarting the application does not change an outcome.

## Common envelope and storage

An internal command envelope contains `operationKey`, `kind`, `subjectRecordId`, `sourceVersionId`, `scenarioVersionId`, `correlationId`, `requestedAt` and a typed payload. No bearer tokens, passwords or support flags. Business services create `OutboxWork` in their transaction. A worker claims a due job with an atomic lease/update; `AdapterAttempt` records an attempt number, start/end and redacted typed request/result. A stable operation identity survives retries and worker restarts. The worker receives only the minimum projection required by its port.

The public job response is `{id, kind, state, attempts, nextAttemptAt?, completedAt?, resultResourceId?, errorCode?}`. Raw internal payload is never returned through this status endpoint. Job authorization follows the subject plus capability; administrative inspection is separately audited. Result-resource type is determined by kind and checked by the endpoint.

| Port | Request payload | Successful result | Persisted business link |
|---|---|---|---|
| Rating | Quote/draft revision ID and hash; product/binder/rating-rule versions; risk-only projection; requested effective/term dates | Premium/tax/fee/commission decimal components, factor breakdown, expiry, required referrals/evidence with rule IDs and input hash | RatingResult and individual Referral rows |
| Document generation | Document kind, template version, exact policy/quote revision, safe merge DTO, audience | Storage object identity, byte count, MIME type and hash | DocumentVersion; generation never replaces an issued file |
| Document storage | Temporary object identity, expected hash/length/type, destination document-version ID | Durable key and verified hash; ready status | DocumentVersion plus finalisation job result |
| Email | Template version, persisted message ID, recipient snapshot, safe rendered subject/body, approved attachment version IDs | Deterministic delivery reference, delivered/rejected time and recipient results | Message delivery and AdapterAttempt; retries use same message delivery key |
| Lookup | Allowlisted lookup kind (address/company/FCA/vehicle/licence), normalised typed query, reference-data version | Typed candidates/evidence, result status, source/as-of and confidence | AgencyEvidence or proposal lookup evidence; not an automatic verified user declaration |
| MID | Add/change/remove, registration/trade-plate identity, exact policy version, effective dates and safe cover fields | Accepted/rejected, reference, reason codes and effective acknowledgement | Version-linked MID result and exception task when rejected |
| Claims | Incident ID, product, policy version, occurrence details, safe risk/contact/evidence links | Handoff reference and provider-state summary; later paid/reserved/status snapshots | Incident and append-only ClaimsSummary; no local claims settlement |
| Payment | Obligation kind (refund/broker-remuneration), obligation ID, approved eligible positive amount/currency, payee reference (demo identity, no bank secret), stable payment key | Confirmed payment reference, amount, completed time or definite rejection/unknown | Refund or remuneration residual, payment state and exactly one payment journal |
| Bordereau submission | Batch ID, provider, period, exact export document/hash, validation version | Submission reference/time/hash or row rejection codes | Immutable submitted batch or failed attempt |

Product-specific payload schemas must not reuse an unrestricted JSON object. These internal ports are distinct from public HTTP commands; business endpoints create work and return the relevant resource/job IDs. Claim snapshots, payment confirmations and MID results apply only to their exact original source and cannot silently update a later issued version.

## Determinism and retries

Seed scenarios `success`, `reject`, `fail-once` and `timeout-after-success` with explicit adapter-kind applicability. Lookup scenarios additionally support `no-match` and `multiple-matches`. A scenario version and seed are pinned on first request; changing integration settings affects future operations only. Success references derive from kind and operation key, not the current time or process randomness. Business timestamps use a persisted demo clock/control, and normal operational timestamps use UTC.

Default retry delays are 5 seconds, 30 seconds, 2 minutes, 10 minutes and 30 minutes; maximum six attempts, with bounded jitter derived from operation identity. An application shutdown/lease expiry permits recovery. Definite validation rejection is terminal until authorised correction creates a new business intent. A transient transport failure retries the same key. An uncertain payment result first queries the persisted provider outcome for the same key, then applies that outcome; it never initiates a fresh payment key.

Persist the simulated provider side separately from local delivery attempts using `DemoProviderOperation` (unique adapter kind/operation key, request hash, scenario version, state, result and completed timestamp). This is necessary to model a provider completing a payment before the local worker crashes. Reusing a provider key with another request hash returns conflict. Do not implement idempotency using an in-memory dictionary. `AdapterInbox` deduplicates delivered result events by provider/event identity and stores their content hash; changed content with the same event identity is quarantined for review.

Exhaustion creates one linked exception task, not a new task on every poll. Authorised manual retry retains operation identity and records reason; it does not regenerate an issued document or republish the business transaction. Correction after a definite business rejection uses a new version/new explicit operation, preserving the rejected attempt.

## Failure walkthroughs

| Case | Required durable outcome |
|---|---|
| Issue fails a journal constraint before commit | Policy/version/transaction/journal/audit/outbox/result all roll back. Retrying the same command is safe because no successful effect exists. |
| Issue commits, email worker fails | Policy remains issued; job shows retry/failure, its exception task is linked. Worker retries the same delivery key. No second policy or journal. |
| Document worker writes bytes, metadata commit fails | Bytes remain temporary/unavailable. Retry verifies hash and finalises the same document version. Grace-period cleanup removes only unreferenced abandoned bytes. |
| Provider pays, worker times out before Refund update | DemoProviderOperation retains confirmed payment. Worker queries that key, then atomically marks paid, posts one journal, stores inbox/result and completes the job. |
| Duplicate provider event | Matching provider/event hash replays acknowledgement; unique refund/payment journal operation key prevents duplicate cash movement. Different hash is quarantined. |
| Rating completes after draft was edited | Store the original revision-bound result; do not make it current. UI sees a stale result and requests rating of the new revision. |
| User loses scope while export runs | Generation may complete internally, but download reauthorisation denies access. Job status also follows current scope. |

## Implementation verification

Use separate worker/provider-result transaction boundaries in fault-injection tests; running all simulated steps in one transaction cannot exercise timeout-after-success. Kill/restart the worker after each boundary. Assert unique operation keys, balanced journals, unchanged issued hashes, recovered lease, no duplicate exception task and current download scope. Schema and design walkthroughs alone do not prove these runtime properties.
