---
phase: 02-application-and-persistence-foundation
reviewed: 2026-09-14
status: passed
method: inline source and test review
---

# Foundation code and security review

Scope: API startup, identity and every operational endpoint; application capability/retry rules; infrastructure persistence, migrations, seed, tickets, command boundary, leases, provider and inbox; browser auth/operations/shell; SQL/API tests and CI/setup scripts. Generated migration snapshots were assessed through the model and real migration tests. This is an agent review of the local development foundation, not a penetration test or production-hosting approval.

## Finding fixed

**P2 — Diagnostic scenario scope did not have to match its JSON behavior.** DiagnosticDemoProvider accepted any `diagnostic-probe/` scope with a supported scenario value. A corrupted setting labelled success could execute rejection behavior even though the integration read endpoint rejected that same mismatch. Require exact scope-to-scenario equality before creating a provider operation. DiagnosticProviderTests now changes a stored success setting to rejection JSON, verifies a new operation fails with InvalidPayload and persists no provider effect, and verifies an already committed result still replays unchanged. Full native backend suite: 32 unit +14 integration tests passed, zero skips, in `.local/foundation-review-results`.

## Reviewed safeguards

| Area | Evidence and conclusion |
|---|---|
| Authentication | PasswordHasher credentials; unknown-user dummy hashing; serialized five-failure lockout; SQL tickets with random keys stored as hashes; persisted protected ticket/key material; active user/stamp/current internal roles checked on each request. MFA/reset-required identities fail closed. |
| Request boundaries | Fallback authentication, server capability checks, mutation CSRF including login/logout, bounded typed JSON, login rate limit and safe Problem responses. Development diagnostics are not mapped in production. |
| Disclosure | Job reads scope before lookup; administrative lists require capability before counts/paging; audit allowlist emits fixed summaries. Ordinary projections exclude payload, raw provider result, credentials and audit before/after/reason. Dispatcher emits a fixed warning for unknown exceptions. |
| Command replay | Actor/route/key scope, typed request hash, transaction-owned application lock, one effect/outbox/audit/receipt commit. Authorization precedes replay; replay precedes mutable preconditions. No auth response caching. SQL triggers reject receipt/audit updates and deletes. |
| Delivery and recovery | Row locks, token/attempt/expiry fences, stable provider operation key, independent provider transaction, atomic inbox/receipt/attempt completion, duplicate hash quarantine. Manual recovery requires reason and rowversion and retains globally bounded attempts. Batch locks use stable order and commit atomically. |
| SQL foundation | Explicit lengths, FKs/no cascade, JSON validity, UTC checks, unique identities, rowversion and published product validity guard. Native tests migrate/seed repeatedly and reject invalid data and stale overwrites. |
| Demo safety | Initializer validates exact database and password before mutations; ordinary initialization has no reset. Tests replace the configured catalog with a generated owned name and clean only that name. Browser recovery fixtures are labelled and append history. |
| Frontend | Current actor fetched server-side without cache; full navigation after login/logout; safe HTTP messages; duplicate command lock and retained key after uncertainty; obsolete list requests aborted; labelled keyboard-scrollable tables/drawer. |
| CI | Read-only repository permission, no deployment, disposable SQL service credentials; real SQL tests fail on unavailable SQL. TRX gate rejects absent/skipped/failed/undersized suites. Hosted provisioning has not been executed. |

## Boundaries carried forward

- Setting/version publishing has no write API yet; phase 11 must enforce immutability and validate publication before enabling edits. Direct database administration can still modify these rows. The corrected provider rejects inconsistent configuration; it is not a substitute for future publication controls.
- Job list cursors freeze creation cutoff, not later state changes; they are operational navigation, not a snapshot export. Future export semantics require their own contract.
- Local production startup guards exist, but public hosting, Entra, encrypted non-Windows keys and full MFA/account administration remain future work. The present deployment is a local Development demo.
- Business-job subject permissions must be implemented by each owning phase. The diagnostic access rules do not grant access to later policy, payment or document jobs.
- No other material foundation finding remains open from this review. This does not imply later business features are implemented or verified.
