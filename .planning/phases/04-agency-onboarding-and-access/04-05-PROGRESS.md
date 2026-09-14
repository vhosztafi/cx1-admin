# 04-05 implementation progress

Plan remains **in progress**. Do not create a completion summary or mark AGY-01/02/05 complete yet.

Production commit: **699fa5f**. No active test or preview processes remain.

## Staged identity/storage slice

- Added bounded `AgencyUserRules` email/display-name validation and exactly three allowed broker roles. Normalized email uses the existing invariant uppercase convention; no name splitting, mixed role sets, privileged role input or caller-owned agency is accepted.
- StaffUser now has nullable AgencyId FK, no Team for agency users, and an unfiltered unique SQL(Id,AgencyId) index. Invitation uses a migration-managed composite FK to that index: EF alternate keys would incorrectly require AgencyId on existing internal staff. EF still models its ordinary UserId FK; no pending model drift is inferred from the manually added SQL composite constraint.
- Broker role seed is additive, with no broker accounts/credentials created automatically. SQL triggers prohibit mixed internal/broker grants, multiple broker roles, agency/email reassignment, roleless active or open-invitation users, and changes to assigned/broker role identity. The invited user insertion gap is allowed only to create the user before its role; the service adds user, role and invitation atomically.
- AgencyInvitation stores lifecycle, optional token hash/issue/expiry/delivery, acceptance and revocation. SQL bundle checks enforce all-or-none issuance and exact14-day expiry, staged no issuance and valid terminal fields. Invitations must start staged; terminal history cannot change/delete; pending token identity cannot rotate in place. Composite notification/agency FK is already present for later issuance.
- `AgencyUserService.Stage` currently implements draft staging only, with current internal authorization before replay, parent lock/ETag, normalized-email gap lock, safe identity-only receipt, and audit/activity. It creates invited user + one broker role + staged invitation, with no token, expiry, credential or delivery. No public user endpoint is enabled yet.
- Draft abandonment now revokes staged/pending invitation history, suspends owned users, rotates stamps and revokes their sessions in the same agency transaction. Lock order is agency -> ordered users -> invitations. No destructive removal of staged history.
- Local authenticate, actor read and SQL ticket store explicitly reject AgencyId identities pending trusted retrieval in04-07. Even a test-created active broker with a valid credential cannot sign in through the internal-only foundation.

## Verification

Targeted10 unit +1 real-SQL test passed in `.local/phase4-invitation-storage-reviewed`. Covers normalization/invalid roles; migration/model drift; preserved seeds; no staged secrets/effects; replay; internal/other-agency email reuse; concurrent same-email staging with exactly one winner; SQL mixed scope/duplicate role/cross-agency invitation/reassignment/terminal denial; closed broker authentication; abandonment revocation. Full suite passed **222 tests including28 real-SQL scenarios**, no failures/skips; report gate verified `.local/phase4-invitation-storage-full` (187 unit/35 integration). Final10 unit/1 SQL rerun in `.local/phase4-invitation-ownership-final` additionally proves rejection specifically by FK_AgencyInvitation_User_Agency, without a duplicate-invitation constraint masking the ownership check. CI minima updated222/28 Windows,220/26 Linux; hosted CI unperformed. Initial SQL test exposed unassigned broker role mutation; the trigger now protects broker definitions as well as assigned roles.

## Required next work

1. Implement active issuance and activation-participant issuance of staged records:32-byte random token, hash only in invitation,14 real-time days, protected notification envelope bound to current invitation/agency. Update notification immutable ownership model/migration, worker and retry with actual invitation validity and superseded stale jobs. Do not dispatch pending invitations via the activation-only seam.
2. Implement resend/revoke, scoped user edits/deactivate/reactivate, last-active-admin guard, exact replay and current authority before replay. Keep email/agency immutable. Resend revokes the predecessor before creating a successor; never rotate pending token identity in place. Acceptance uses a dedicated transaction, not a secret response receipt.
3. Add bounded CSRF/rate-limited acceptance, password hashing, one-time consumption and all expired/revoked/used/concurrent cases. Add Development-only audited no-cache demo-link reveal; no token in ordinary list/body/audit/log/screenshot. Keep broker authentication closed until04-07.
4. Add scoped user/invitation APIs and update actual API contracts/ID-only command responses, source dialogs/stage2 users, minimal fragment-based local acceptance page, frontend/browser gates. Current UI still correctly says user management unavailable; replace that copy only with functioning controls. Add staged-user readiness once actual user lifecycle exists. Map agency.user-staged to an accurate activity summary when exposing the service; the generic activity fallback has not been extended for this not-yet-public action.
5. Run full relevant regressions, code/UI review and record04-05-SUMMARY only when the complete plan is verified. No human UAT inferred.

Do not reset Demo or edit frontend-code. The verified migration and three broker role seeds were additively applied to Demo; existing records were preserved, no broker account/credential was seeded. No active preview, real notification or deployment has been introduced by this slice.
