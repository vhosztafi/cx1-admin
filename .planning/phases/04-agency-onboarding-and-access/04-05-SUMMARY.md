---
phase: 04-agency-onboarding-and-access
plan: '05'
subsystem: agency-users-and-invitations
requires: [04-04]
provides: [scoped-broker-identities, secure-invitation-lifecycle, one-time-password-acceptance, agency-users-workspace, staged-administrator-readiness]
affects: [04-06, 04-07, 04-08]
requirements-completed: []
completed: 2026-09-14
---

# 04-05 — Agency users and secure invitations

Completed across staged storage699fa5f, delivery ownership6883e81, issuance/resend/revokef86d9d3, user lifecycleee5db25, acceptance09fa7dd, internal APIs6cb63d3, Users UI dfa87b4, wizard39daf09, readiness17cc438, Last active0a350ae and counts91bf437. See04-05-PROGRESS for detailed evidence and04-05-REVIEW for the final scoped review.

## Delivered

- Separate agency identities with one of three broker roles, globally unique normalized email, immutable email/agency and SQL enforcement. Staged draft invitations have no token, expiry or delivery. Abandonment retains records and revokes staged users/invitations/sessions.
- Active invitations issue32 random bytes, hashed token and purpose-protected delivery material with14 real-day expiry. Resend revokes its predecessor and creates one fresh owned delivery. Exact replay never rotates twice; user edits/deactivation/reactivation preserve history and enforce last-active-administrator safety.
- One-time token acceptance uses current state/time/role checks under aggregate locks, hashes the password and creates one credential. It never caches a password/token receipt or signs the broker in. Audited Development reveal is internal-only; browser password setup removes fragment/history secrets and clears sensitive fields after requests.
- Scoped paged internal APIs, actual row versions, bounded strict bodies, CSRF and current authorization. Users tab and Contacts-stage dialogs preserve exact uncertain commands and retained stale inputs. Parent onboarding edits cannot be silently overwritten after user changes.
- Actual staged/active administrator readiness. Last active comes from stored session history; missing history is explicit. Directory/header/KPI counts derive from agency identities, with inactive users retained and staged/invited users identified separately.

## Verification

Final full suite:239 backend tests, including34 real-SQL scenarios,198 unit/41 integration; no failures or skips. Report gate passed .local/phase4-user-counts-full. Frontend22 tests, TypeScript/production build, ESLint,77 Node contract tests and OpenAPI lint pass.327 operations/949 control mappings retained.

Real Chrome users/wizard browser passes staging, persisted identities, lost-response exact replay, stale edit retention/reload, deactivation/reactivation, active resend/revoke, safe demo-link opening, concurrent onboarding-edit protection, live header counts, directory/KPI values and390px containment. Password-acceptance browser passes URL cleanup, confirmation, one-time use, no automatic session and uncertain response handling. Desktop/mobile screenshots inspected. All owned previews stopped; fictional demo history preserved. No hosted CI or human UAT claimed.

## Next

Execute04-06 independent activation approval, suspension and immutable agreed terms, integrating staged invitation issuance and demo notices atomically. Broker login/trusted scope remains closed until04-07. Full AGY requirements remain partial until remaining owning plans and04-08 verification. Sales funnel remains unchanged.
