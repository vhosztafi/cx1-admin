---
phase: 11-configuration-and-account-administration
plan: '05'
status: complete
requirements: [ADM-05, ADM-06]
---

# 11-05 — Account security

Delivered saved profile edits, owner requests for approved email/team/role changes, current-password/factor verified password change, generic forgotten reset with protected local delivery and single-use completion, own-session list/current marker/revocation, and persisted private security reports. Security changes rotate stamps and revoke sessions; password changes/resets invalidate old recovery codes and require fresh sign-in. Protected owner requests use the same independent current-admin approval mechanism as administrative changes.

Implemented encrypted ten-minute authenticator enrolment, verification before activation, cancellation, five-minute one-use login challenges, six-digit TOTP with replayed-step rejection, per-account/challenge attempt limits, hashed single-use recovery codes, replacement and verified disable. Ticket storage independently refuses MFA accounts without a verified factor; actor reads reflect the real MFA setting. No credentials or factor/reset/invitation secrets enter command receipts/audit. Setup/recovery secrets are shown only in the authenticated one-time flow. Uses BCL HMAC-SHA1 and the algorithms/vectors in [RFC 6238](https://www.rfc-editor.org/rfc/rfc6238) and [RFC 4226](https://www.rfc-editor.org/rfc/rfc4226).

Focused tests: six RFC vector cases plus one native SQL security case passed, zero skips (`phase11-account-security.trx`). The expanded SQL case also passed (`phase11-account-extra.trx`): expiry, bad/replayed factors, concurrent recovery reuse, reset/MFA retention, current session ownership, five-attempt lockout, owner-request approval and live revocation. A test exposed a tracked recovery row conflicting with bulk invalidation; fixed by detaching already-invalidated actions before saving. No broad suite run.

Browser passed profile save, enrolment/confirm, no account access before factor, recovery login/reuse denial, TOTP login, revoking another session, report reload and verified disable (`account-browser.json`). Separate independently approved admin MFA reset passed (`admin-mfa-reset-browser.json`). Initial final navigation timed out; explicit saved-response/page waits and the final complete journey passed. Secret-free screenshots inspected. API build, TypeScript, lint and OpenAPI pass. Only isolated SQL/browser fixture changed; retained demo untouched. Human UAT remains Phase 13.

## Self-Check: PASSED
