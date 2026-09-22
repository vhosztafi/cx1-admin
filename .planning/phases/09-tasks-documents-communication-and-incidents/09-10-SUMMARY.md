---
phase: 09-tasks-documents-communication-and-incidents
plan: '10'
status: complete
completed: 2026-09-22
requirements-completed: []
---

# Durable messages and exact-version document packs

Agency messages and document packs now queue persistent deterministic delivery,
retain immutable content/recipient/file snapshots, and expose delivery history,
attempts, retry and explicit resend through the back office.

## Delivered

- OperationalMessageVersion, OperationalDelivery, recipient and attachment records,
  migration20260922094628_OperationalDelivery with original-subject/source guards,
  append-only snapshots and protected downgrade. Policy JSON is unchanged.
- MessageDeliveryService.Send/Recover and DocumentPackService.Send use current
  original scope before replay. Send freezes the saved message ETag. Recovery uses
  the delivery ETag and audited reason; retry keeps the job/provider operation,
  resend creates a new delivery with original content and exact file versions.
- MessageDeliveryWorker.ExecuteProvider/Apply persists independent provider effects,
  validates current sender/contact/file authority and actual stored bytes, fences
  stale leases and quarantines changed duplicate results. Timeout-after-success
  recovers the same provider result. Definite rejection cannot be blindly retried.
- Development MessageDeliveryDispatcher, durable scenario seed and terminal failure
  propagation into existing job exception/workflow task machinery. Suspended senders
  do not execute; an existing authorized active task owner can receive the exception
  with original sender provenance retained. No grants or real transport are added.
- DeliveryReadService.Read/List/Attempts/Job, closed read/send/recovery APIs and
  original-scope pack recipient options. Parent authorization uses explicit held
  locks; polling reads avoid delivery-to-work shared-lock inversion.
- Real Send to agency, exact-version pack selection from current/history rows,
  recipient picker, preview, durable receipt/attempt history and separate retry /
  resend controls. Frozen commands retain bytes/key across uncertain responses.
  A queued receipt is not claimed delivered. Superseded application can still show
  an already completed provider outcome honestly.
- OPERATIONS-CONTRACTS documents final paths, ETags and provider boundaries;
  OpenAPI/schema/generated TypeScript are synchronized.

## Evidence

`.local/phase9-10-final-strict` verifies11 unique cases/7realSQL/no skips, copied
unchanged from unit-current4, final-sql5 and motor/commercial browser1each.
SQL proves queue/replay, immutable source, stale lease, changed duplicate quarantine,
timeout-after-success, exhausted retry and one exception task, sender/recipient
revocation before and after provider effect, ready-file pack, replacement/resend
retention, wrong/missing selection denial, internal-file exclusion, corrupt bytes
preventing provider effect, and SQL mutation/downgrade rejection.

Both products pass17 current-fingerprint browser checks plus SQL readback through
`scripts/verify-communication-browser.mjs`. The fixture accelerates only retry due
scheduling, uses actual lease/provider/application services, and records sent message,
two delivered receipts/two provider operations/one exception. Dynamic local API/web
ports; no5000 fallback. Both390px delivery screenshots were inspected.

- Motor output: `.local/browser-evidence/communications/CoverMGA_Test_d1fc31de4418436782c52eae4ea2c06b`.
- Commercial output: `.local/browser-evidence/communications/CoverMGA_Test_b180a9bf80db4767b20dad1c59b9708c`.
- Root417 and frontend192 pass; build/typecheck/lint pass. OpenAPI exits0 with101
  warnings. Unit readiness/permission and API boundary RED→GREEN evidence is retained
  in09-10-CHECKPOINT; earlier failed migration/fixture runs are excluded from totals.

## Boundaries and next work

No human business/assistive UAT, retained demo migration, real external delivery or
production deployment is claimed. Original keys and frontend-code are untouched.
Provider crash recovery is exercised by retained effect plus abandoned/stale lease;
the browser fixture directly drives the worker rather than asserting a separate
host-process restart. Final source placement, seeded histories and aggregate phase
acceptance remain09-17/18, so compound requirements remain open. Next:09-11 historical
incident occurrence resolution, preserving observed date/time precision.
