---
phase: 11-configuration-and-account-administration
plan: '04'
status: complete
requirements: [ADM-04, ADM-06]
---

# 11-04 — Internal identity administration

Delivered searchable internal users, team creation/rename, servicing-role invitations, one-use invitation acceptance, exact-version protected email/team/role/MFA-reset requests and independent approval. Sensitive approval rechecks requester and approver, rotates security stamps and revokes sessions. Suspension, forced reset and explicit session revocation act immediately; resumption cannot revive tickets. A serialized last-active-administrator guard prevents administrative lockout. Agency identities cannot enter these routes. Authority grants remain in the separately approved authority editor from 11-02.

Additive `AdministrationIdentitySecurity` migration introduces protected, expiring identity actions and the MFA replay-step column for 11-05. Invitation tokens are hashed for verification; deterministic local delivery is encrypted separately, never written to audit/command receipts. Development-only reveal requires current administrator authorization, CSRF and an explicit action. Public invitation acceptance uses CSRF and rate limits. Newly invited staff start with servicing; other roles require approval.

Focused native SQL `RealSqlUserAdministrationIndependentApprovalInvitationAndRevocation`: 1 passed, zero skips. Browser request/independent approval/reload, suspension causing 401, and resumption leaving the old session rejected all passed (`users-browser.json`). API build, TypeScript, ESLint and OpenAPI pass. Migration inspected: additive table and nullable column only. Browser/migration applied only to owned isolated fixture; retained demo unchanged. No full regression/human UAT claimed.

## Self-Check: PASSED
