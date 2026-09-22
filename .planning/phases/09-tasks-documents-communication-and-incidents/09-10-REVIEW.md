# 09-10 review

Status: complete; final SQL guard acceptance and current browser evidence passed.

Reviewed immutable message/pack snapshots, normalized recipient/file references,
original subject authorization before replay, exact provider operation/scenario/hash,
lease fencing, retry versus resend, terminal exception ownership, API contracts,
queue receipt validation, UI callers and current browser/SQL evidence.

Corrections made during review:

- Explicit SQL collation for the file hash avoids incompatible source-column
  collations. Frozen JSON identity comparisons use binary bytes.
- Message source guards use OPENJSON nvarchar(max) for bodies longer than4000.
- Retry acquires the work head before the mutable delivery head. Read polling uses
  ReadCommitted while retaining explicit authorization locks, avoiding the opposite
  delivery-to-work shared-lock ordering.
- Revoked senders cannot execute delivery. Automatic exception ownership uses an
  existing authorized active actor without granting permissions, retaining original
  sender provenance. Both sender and recipient revocation cases pass.
- Provider result parsing fails with a bounded provider-conflict outcome for invalid
  JSON, and provider state must agree with its retained result.
- Delivery history distinguishes superseded local application from a provider
  effect that already happened. Retry uses delivery ETag; Send uses message ETag.
- Frontend commands retain exact request bytes/key and validate queued receipts.
  A queued request is never presented as successful external delivery.

Motor Trade and Commercial Combined each pass17 browser checks and SQL readback,
including failed6→manual retry→delivered7, unchanged delivery/job identity, historical
file pack selection and mobile390px receipt rendering. Both screenshots inspected.
Root417/frontend192/unit4 pass; build/lint/typecheck pass. OpenAPI exits0 with101
warnings. Human business/assistive UAT and retained demo migration were not performed.

Remaining final evidence: current final SQL suite adds immutable attachment/content
mutation rejection, retained-history downgrade rejection, internal-file denial and
actual file-byte corruption before provider effect. Earlier SQL5 passed but is not
substituted for these later assertions. Source placement and seeded history remain
09-17/18. No real transport exists; deterministic provider state is persisted.

Final: phase9-10-final-sql passed5 including byte corruption, immutable SQL snapshots
and downgrade guard. Strict11unique/7realSQL/no skips passed. All recorded processes
finished. No unresolved HIGH/CRITICAL finding identified in this review.
