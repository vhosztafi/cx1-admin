# Permission contract v1

Default deny. Local authentication maps a stable User ID to roles, agency scope, team and authority. Never trust client-supplied role/agency/actor IDs. Future Entra subject mapping changes authentication, not domain policy. Server applies authorisation before returning records or calculating exports; hiding a button is not a security control.

Foundation operational mapping: `integration-admin` is granted only to internal `system-admin`, with external agency identities denied. Development diagnostic creation uses that capability. A diagnostic job can be read by its creator; another creator's job requires integration administration and records an inspection audit. This does not grant access to business job subjects, finance posting or underwriting. Existing command receipts do not bypass current role checks.

| Capability | Servicing | Underwriter | Senior UW | Agency admin (internal) | Finance | System admin | Broker identities |
|---|---|---|---|---|---|---|---|
| Scoped business read/search | Yes | Yes | Yes | Agency/client admin data | Finance-related data | Support access audited | Own agency, safe fields |
| Account/contact/quote capture | Yes | Yes | Yes | Contacts/onboarding only | No | No implicit underwriting | Own agency only when granted |
| Support flag internal category | Yes with flag permission | Yes with flag permission | Yes with flag permission | No by default | Functional instruction only | Audited support permission | Never |
| Safe support instruction | Yes | Yes | Yes | Granted relationship | Granted relationship | Granted support access | Only explicit FlagVisibility grant |
| Rate/submit draft | Yes | Yes | Yes | No | No | No implicit underwriting | Own agency if product permission |
| Decide underwriting referral | No | Within dimensions | Within dimensions | No | No | No implicit underwriting | No |
| Record capacity response | No | Assigned case with evidence | Yes with evidence | No | No | No implicit underwriting | No |
| Bind/issue policy/MTA | Prepared command if delegated issue permission; all underwriting checks remain | Within authority | Within authority | No | No | No implicit underwriting | Explicit product permission only; no authority bypass |
| Take over another editor's draft | No by default | Assigned-team grant | Yes | No | No | Audited support grant | No |
| Prepare cancellation/renewal | Yes | Yes | Yes | No | Finance review only | No implicit underwriting | Request only by own agency |
| Tasks/notes/messages | Assigned/accessible records | Assigned/accessible records | Assigned/accessible records | Agency tasks | Finance tasks | Audited support | Own visible tasks/messages; no internal notes |
| Incident handoff/documents | Yes | Yes | Yes | Onboarding documents | Finance documents | Audited support | Own permitted records/documents |
| Ledger/receipt/allocation/journal | View summary only | View summary only | View summary only | Agency summary | Yes | No automatic posting | Own statement only |
| Refund approval/payment | No | No | No | No | Within refund limit; no self-approval | No automatic finance | No |
| Agency onboarding/access | No | Read | Read | Yes | Financial check | Yes | Own users only if broker-admin |
| Products/authority/templates | Read applicable | Read applicable | Propose underwriting config | Agency products | Finance config | Versioned edit with required approval | No |
| User identity/security changes | Own profile/password/MFA/sessions | Same | Same | Agency users | Same | Internal users, approval workflow | Own security; broker-admin agency users |
| Audit/integration admin | Own accessible activity | Case activity | Case activity | Agency activity | Finance activity | Full permitted audit, secrets redacted | Own agency events only |

System administrators are not automatically underwriters or finance approvers. Roles may be combined within internal scope; do not combine internal and agency roles. Authority is a separate effective-dated matrix scoped to product/binder. Delegation cannot exceed binder limits even when role assignment exists.

## Field and record scopes

- Client identity is shared internally, but contacts/proposal answers/documents belong to ClientAgencyRelationship. A match link never grants access to a competing agency's data.
- Support flags are excluded from risk/rating/referral inputs and insurer bordereaux, even for an internal user who can otherwise read them.
- Internal notes, commercial referral rules/limits and internal correspondence are never present in agency DTOs. Use explicit allowlisted DTOs, not object serialisation followed by best-effort deletion.
- A document's accessible parent does not automatically make every attachment public; its visibility and relationship must match the requested audience.
- Global search, counts, reports, drill-down, exports and notifications share the same scope evaluator. Unauthorised IDs return non-disclosing 404; missing sign-in returns 401; visible-resource forbidden command returns 403.
- Audit data uses event-specific redaction and read permissions. Password hashes, MFA seeds, recovery codes, raw session/invitation tokens and adapter secrets never appear in business audit or reports.

## Identity actions

Local sign-in uses framework password hashing, secure HttpOnly cookie sessions and CSRF protection for cookie-authenticated mutations. Password reset/invitation tokens are random, hashed, time-limited and single-use; delivery is via demo outbox. Self profile fields exclude email/role/authority. Email change needs a second administrator, recorded old/new identity, safe notifications and session revocation on application. Role/authority changes revoke or revalidate sessions immediately.

MFA enrolment requires recent authentication and successful code verification before enabling. Recovery codes are only displayed at creation, stored hashed and atomically consumed. Replacement/disable/reset records an event, revokes affected sessions where appropriate and follows role-required MFA policy. Prototype dates like 30/09/2026 become demo settings, not permanently hardcoded deadlines.

Self-profile fields include full/display name, contact number, job title, out-of-office and digest preference. Out-of-office redirects newly assigned workflow tasks, leaving existing tasks intact. A manager approves role/team changes and deactivation; deactivation reassigns applicable open tasks and revokes sessions in the same transaction. A user administrator can propose these changes but cannot bypass their approval by calling a generic update endpoint.

Password policy reproduces the prototype's stated requirements: at least 12 characters, upper/lower case, number and symbol, and no reuse of the last five passwords. Verify history using the framework hasher, not the mock substring test. UI confirmation must match before submission; the server validates the actual new password independently. Password change preserves a rotated current session after recent credential verification and revokes other sessions. Recovery-code views show a remaining count; lost codes require replacement, since plaintext recovery codes are not retrievable from hashes.

## Required denial cases

For each relevant API: anonymous request, wrong agency, read-only broker mutation, suspended user, missing role, expired authority, exceeding one dimension, self-approval, stale session after identity change and attachment visibility mismatch. Denial must be tested on API calls directly, including download/export/search endpoints, independently of UI controls.
