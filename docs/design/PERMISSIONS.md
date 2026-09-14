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

## Phase 3 party capability mapping

The initial internal mapping grants client-read/client-write and contact-write to servicing, underwriter and senior-underwriter. Internal agency-admin can read identity/relationships and maintain contacts, but does not edit shared business identity or read internal flags. support-internal-read/support-write are separate explicit capabilities granted to servicing/UW/senior in the demo. match-read and match-review are distinct capabilities, both granted only to UW/senior in this demo. List/detail/trails require match-read; decisions require match-review. Matching activity is filtered before paging/counts unless match-read is present. Decision receipts contain only the review ID and original ETag; current permissions and review scope are checked before replay. System-admin has no implicit party mutation or sensitive support grant; combine an appropriate internal role explicitly. Finance has no broad party access until its scoped financial projections are implemented.

SupportFlag and its history are internal-only. SafeSupportInstruction has only id/personId/instruction/reviewOn and is selected by explicit FlagVisibility plus active relationship/person membership. The relationship-read internal preview records an inspection audit and returns that same safe projection; it neither changes the actor nor enables broker login. Actual broker sessions remain Phase 4. Tests must verify trusted agency-context SQL projections both with and without explicit grants, not infer isolation from a denied login alone.

Party cursors bind current role/agency scope as well as user, route, filters and page size. Current authorization and object scope precede replay. Contact person reuse is limited to an authorized same-client relationship; editing a contact changes its declaration only. General client activity uses fixed safe summaries; sensitive flag history, reason and snapshots are separately permissioned. Match decline viewed from the submission side is generic and contains no candidate reference, agency identity or evidence.

## Identity actions

Local sign-in uses framework password hashing, secure HttpOnly cookie sessions and CSRF protection for cookie-authenticated mutations. Password reset/invitation tokens are random, hashed, time-limited and single-use; delivery is via demo outbox. Self profile fields exclude email/role/authority. Email change needs a second administrator, recorded old/new identity, safe notifications and session revocation on application. Role/authority changes revoke or revalidate sessions immediately.

MFA enrolment requires recent authentication and successful code verification before enabling. Recovery codes are only displayed at creation, stored hashed and atomically consumed. Replacement/disable/reset records an event, revokes affected sessions where appropriate and follows role-required MFA policy. Prototype dates like 30/09/2026 become demo settings, not permanently hardcoded deadlines.

Self-profile fields include full/display name, contact number, job title, out-of-office and digest preference. Out-of-office redirects newly assigned workflow tasks, leaving existing tasks intact. A manager approves role/team changes and deactivation; deactivation reassigns applicable open tasks and revokes sessions in the same transaction. A user administrator can propose these changes but cannot bypass their approval by calling a generic update endpoint.

Password policy reproduces the prototype's stated requirements: at least 12 characters, upper/lower case, number and symbol, and no reuse of the last five passwords. Verify history using the framework hasher, not the mock substring test. UI confirmation must match before submission; the server validates the actual new password independently. Password change preserves a rotated current session after recent credential verification and revokes other sessions. Recovery-code views show a remaining count; lost codes require replacement, since plaintext recovery codes are not retrievable from hashes.

## Required denial cases

For each relevant API: anonymous request, wrong agency, read-only broker mutation, suspended user, missing role, expired authority, exceeding one dimension, self-approval, stale session after identity change and attachment visibility mismatch. Denial must be tested on API calls directly, including download/export/search endpoints, independently of UI controls.


## Phase 4 agency access and invitation protocol

Implementation contract for 04-02 through 04-07, not a claim of broker login availability in 04-01. Internal agency-admin and system-admin may administer onboarding, evidence, users, notifications, permission grants and proposals. Underwriter/senior-underwriter may read agency data. Approvers use agency-admin/system-admin but must differ from the saved requester; client-supplied actor/role/approval IDs are rejected. Seed a separate fictional reviewer account. System administration grants no underwriting or finance authority.

Broker identities have one stored AgencyId and exactly one broker-admin, broker-user or broker-readonly role with agency scope. No internal role may coexist. UserRole/user SQL constraints plus transactional service checks enforce scope, including initial role assignment and last active broker-admin protection. Email remains globally unique; existing internal/foreign-agency email cannot be taken over by invitation. Agency user edit allows display name and one permitted role; email and agency reassignment are excluded. Deactivation, role/grant changes and suspension rotate affected security stamps/revoke sessions. Invitations activate credentials only after successful one-time acceptance; delivery never activates identity.

Authenticate and retrieve tickets against current User, Agency, roles, stamp and effective grants. An inactive agency/user is denied even with a previously issued cookie. ActorContext.AgencyId comes only from stored identity. /agency-context has no caller-selected agency parameter; relationship IDs are constrained through the current active relationship. If an own-user route contains an agency ID, compare it to trusted scope before any read, count or receipt. Broker-admin can manage own permitted users and request own allowlisted permission; broker-user/read-only cannot administer. Internal UI routes reject broker identity and the minimal account access screen shows the limited available surface.

The internal sharing preview is audited and uses the same allowlisted projection service as external agency-context. Return only own client identity, relationship contact declarations, explicit safe flag instructions, effective distribution products and granted capabilities. Never internal category/reason, match evidence, competing agency identity, hidden totals, raw activity payload or ledger internals. Filter before paging/counts; bind cursors to user/current scope/grants/filters. Statements/quotes/policies/tasks explicitly report unavailable until their owning phases. Bordereau permission can be approved now, but actual export remains unavailable until Phase 10 enforces it.

Evidence-file upload requires agency administration, CSRF, agency ETag and idempotency key; limit before buffering to 10MiB, sanitize name and restrict type. Download checks agency ownership and read permission each time, uses attachment and nosniff, and exposes no storage path. A submitted file ID must match the target agency via composite FK. File bytes are not copied to command receipts. Evidence state/actor/time/fingerprint are server-owned. Six digits or a declaration of confirmation is not FCA verification.

Invitation tokens contain 32 random bytes, use a SHA-256 stored hash and a purpose-separated Data Protection delivery envelope with persistent keys. Expiry is exactly 14 real UTC days from issuance, independent of business/demo clock. Staged invitations contain no token/expiry/notification; resend revokes the old token and issues a distinct invitation under Agency -> User -> Invitation locks. Never store raw token/password in ordinary outcomes, audit, receipt, log, provider receipt or browser URL query. A queued old invitation is checked again before demo delivery and marked superseded when no longer current.

Development-only POST /invitations/{id}/demo-link requires current internal agency user administrator and antiforgery, records inspection, returns no-store and has no replay receipt. Production never registers it. The local acceptance UI carries the secret in a fragment, immediately removes it with replaceState and submits token/password only in POST body with antiforgery and rate limiting. Do not screenshot or log the fragment. Acceptance locates the hashed token, locks agency then user then invitation, rechecks active agency/invited user/current role/nonexpired/unconsumed token, creates hashed credential, consumes invitation, rotates stamp and audits atomically. Invalid/expired/used/revoked tokens receive the same bounded error. There is no automatic sign-in and no token replay that resets a password.

Source role dialogs aguser/invite both map Broker administrator -> broker-admin, Broker user -> broker-user, Broker user (read only) -> broker-readonly. The order differs between dialogs; no fourth broker role is implied. Every state decision, terms decision and permission grant rejects self-approval in both domain and SQL constraints. SQL/API acceptance must cover current-session denial, cross-agency ID substitution, mixed-role attempts, last administrator removal, stale decisions, concurrent invitation acceptance/resend and suspension/link races.
