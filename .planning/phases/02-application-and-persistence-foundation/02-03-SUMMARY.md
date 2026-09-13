---
phase: 02-application-and-persistence-foundation
plan: '03'
status: complete
requirements: [FND-02, FND-06]
completed: 2026-09-13
---

# Revocable local authentication

Commit 7476caf implements contract /api/v1/auth/csrf, login, logout and /api/v1/account. ASP.NET cookie authentication uses an encrypted SQL ITicketStore; only a random opaque reference is in the protected cookie and only its SHA-256 hash is indexed in SQL. Data Protection keys persist outside tracked files and use Windows DPAPI. User state, security stamp and current internal roles are checked on every authenticated request. Sessions have a fixed eight-hour lifetime. Logout atomically records revocation and a redacted audit event.

Local credential failures are transactionally counted in SQL: five failures lock for 15 minutes. Login also has a per-direct-IP 20/minute limiter. Requests use bounded DTOs, reject unknown JSON fields, enforce antiforgery on API mutations including login/logout, and return safe ProblemDetails. API responses prohibit caching. Capabilities are explicit and default-deny; system-admin does not imply finance or underwriting authority. Actor context uses stable internal user ID and team; external/mixed role sets remain denied until agency scope is implemented.

Development uses a separate HttpOnly cookie. Production cookie settings use __Host-, Secure, Path=/, no Domain, and reject HTTP API access. Non-development startup requires an explicit persistent key directory. Non-Windows production hosting fails closed until encrypted key storage is configured. MFA/must-reset credentials cannot bypass their pending flows; no fake MFA success is exposed.

Verification: build succeeds without warnings; locked restore succeeds; 13 unit cases and seven integration cases pass without skips. The authentication SQL/API scenario verifies anonymous 401, missing/stale CSRF 403, malformed unknown-property JSON 400, wrong password, persisted lockout, safe actor DTO, no-store, permission denial/allow, secure production flags, HTTP rejection, expiry, stamp change, suspension and authentication/revocation audit. The first test exposed malformed requests being reported as 503; corrected to preserve the HTTP request error status with a safe body. Host restart with persistent keys/tickets succeeds; original cookie is rejected after logout.

Additionally, two actual dotnet API processes were started sequentially on a temporary loopback port against CoverMGA_Demo. Node's retained cookie authenticated after process restart; logout revoked it and replay returned 401. Both test processes were stopped. Local smoke runner is ignored .local/auth-process-smoke.mjs, with no embedded password; automated host-restart regression lives in AuthenticationTests.cs. The demo database now has the LocalSessionSecurity migration. No production deployment or real external service was used.

Inline review corrected capability-policy evaluation to check authenticated identity before parsing its user ID. SQL parameters and request bodies are not configured for sensitive-data logging; response/audit tests exclude passwords and credential internals. Keys, test outputs and the generated demo password remain ignored. Browser design/UAT are pending 02-04; MFA/account administration remains Phase 11. FND requirements stay open until full Phase 2 verification.

Next: 02-04 prototype-faithful shell, real sign-in UI and browser verification; then 02-05 durable platform workers and 02-06 foundation acceptance.
