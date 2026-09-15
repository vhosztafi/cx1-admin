---
phase: 04
slug: agency-onboarding-and-access
status: planned
nyquist_compliant: false
wave_0_complete: true
created: 2026-09-14
---

# Phase 4 validation strategy

Existing runner infrastructure is ready: xUnit real SQL tests, Node contract/frontend tests, ESLint/TypeScript/build and local Chrome scripts. Baseline Phase3 passes123backend/19SQL,63contracts,15frontend. No new framework or in-memory substitute. Individual new test files named in plans are implementation outputs, not existing tests claimed passed.

| Plan/threat | Requirements | Meaningful boundary evidence to add |
|---|---|---|
| 04-01 / T-04-01 | All AGY | Source control/option inventory; strict partial/full DTO cases; secret/capability/invalid-state rejection; mapping drift mutations |
| 04-02 / T-04-02 | AGY-01/03 | AgencyDraftRules/Tests: migration IDs/reseed, step/save/ETag/replay/rollback; browser draft resume and URL filters |
| 04-03 / T-04-03 | AGY-01 | AgencyEvidenceRules/Tests: attachment limits/ownership, immutable attempts, changed-input invalidation, every activation checklist branch and format-only denial |
| 04-04 / T-04-04 | AGY-05 | AgencyNotificationTests: real SQL leases/receipt deduplication, protected payload, stale worker, provider-success-before-crash/process restart, bounded retry/denial |
| 04-05 / T-04-05 | AGY-01/02/05 | AgencyInvitationTests: staged no-token/delivery; current authority, scoped identities, old/expired/revoked token, concurrent accept/resend, no password receipts and SQL role-mixing guards |
| 04-06 / T-04-06 | AGY-01/02/03/05 | AgencyApprovalTests: independent actors, current base/evidence, atomic activation/delivery/terms rollback, suspension/link race, immutable intervals and stale scheduled proposals |
| 04-07 / T-04-07 | AGY-02/03/04 | AgencyScopeTests: real broker cookies, own-only fields/counts/cursors, current session/grant revocation, self-elevation/approval denial and preview staff identity unchanged |
| 04-08 / T-04-08 | All AGY | Full no-skip suites, CI minima/YAML, browser all source states/options, source/keyboard/mobile review, owned cleanup and truthful staged acceptance |

After contract edits run node scripts/validate-contracts.mjs and agency-contracts tests. Backend tasks run relevant Agency filters; each completed slice runs full backend in a fresh .local directory with report gate, never counting stale TRX files. UI tasks run web:test/lint/typecheck/build, then local browser checks. Rebuild after stopping only identified task preview DLL holders. Retain API5087/web3100, no other server manipulation.

Browser harness must preserve fictional history and use unique identities. Tokens/passwords stay out of screenshots/output. Use actual two-user approval sessions, not role headers; count forbidden responses and unchanged SQL records directly. All existing shell/operations/client/contact/support/match scripts remain relevant regressions. Compare source/app at1560x1000 and390px, verify314px rail, table containment, dialog focus and live status.

Nyquist compliance and phase sign-off remain pending implementation. Human UAT, hosted CI and optional Docker runtime cannot be inferred from these plans. AGY-03/04 retain downstream acceptance for real financial/insurance/task records.


## 04-07 completed evidence

AgencyExternalApiTests validates actual accepted-cookie isolation for three broker roles/two agencies, own user/invitation lifecycle and current permission/session/replay authority. AgencyScopeResolution/SharingProjection/SharingContext/SharingPaging/OwnUserAuthority/OwnInvitationAuthority and permission API/storage tests cover held reads, safe DTOs, scope fingerprints, immutable grants and service races. Final report gate `.local/phase4-external-full-final`:323/56SQL, no skips.80contracts,30frontend/lint/typecheck/build pass. Actual `verify-agency-access-browser.mjs` and shell browser pass with reviewed desktop/mobile screenshots; prior sharing/permissions/users browser evidence is in04-07-PROGRESS. Phase-level Nyquist and AGY acceptance remain04-08.
