---
phase: 09-tasks-documents-communication-and-incidents
plan: '13'
status: complete
completed: 2026-09-22
requirements-completed: []
---

# Exact claims handoffs and administrator summaries

Motor Trade and Commercial Combined incidents now queue one persistent fictional
administrator handoff for an exact revision, historical resolution and source version.
Selected insurer-visible evidence is pinned by version/hash; internal evidence is withheld.
Original current authority is checked before replay, provider execution and application.
Unknown outcomes retain the same actor/body/key/ETag. Only definitive rejection permits
correction; retry recovers the original provider operation. Contact and refresh requests,
attempts and null-preserving paid/reserve summaries are persisted and shown in the UI.
Summaries sort by administrator as-of time, receipt time and stable ID; later receipt
cannot make an older summary current. No real provider communication occurs.

## Implementation

Backend commit224e0d8: ClaimsHandoffService.Handoff/Queue, ClaimsHandoffWorker.ExecuteProvider/
Apply, ClaimsSummaryService.Summaries/Handoffs/Requests/FollowUp/Retry and ClaimsEndpoints.
Persistence adds ClaimsAdministrator/Handoff/Request/Summary with migration
20260922125221_OperationalClaims, Guards partial and updated EF snapshot. ClaimsSnapshots
captures claims-handoff-2 and claims-operation-2; current original policy access, lease
fencing, immutable requests/summaries, duplicate quarantine and protected downgrade apply.
The provider persists its effect separately before local application. A post-effect
revocation preserves that effect without disclosing/applying an unauthorized summary.
The UI adds claims-api.ts and claims-summary.tsx, recovery guards, administrator tabs,
contact/refresh/retry controls and actual policy Claims table values. API contracts,
generators and OPERATIONS-CONTRACTS describe final signatures and nullable money.

## Accepted evidence

.local/phase9-13-accepted-strict verifies1280 unique passing cases/4 real SQL/no skips.
Inputs: units-all1276, recovery-final2, browser-motor-visual1, browser-commercial-visual1.
Do not add earlier focused/repeated runs. Unit/frontend failure assertions preceded fixes.
SQL covers rollback injection, actual API host disposal/recreation after committed provider
timeout, immutable history, protected downgrade, stale leases, duplicate conflict quarantine,
revoked reads/replay/execute/late application, rejected correction, same-operation retry,
one terminal exception task and distinct as-of versus receipt ordering with paging.

Current browser manifests .local/phase9-13-browser/{motor-trade,commercial-combined}.json
pass verify-claims-browser.mjs:19 MT and20 CC checks plus SQL readback. Each proves one
handoff, two summaries, three requests/provider operations and exact source/evidence hashes.
CC selects the original pre-adjustment source after a real midday adjustment. Lost
post-commit responses retain the exact command and block navigation. Final desktop/mobile
screenshots inspected in evidence roots CoverMGA_Test_abd232a50e64439fbff267b797d55418 and
CoverMGA_Test_7eed4512960f4ae6902f0fd3406f7057. Root420/frontend198; lint, build/typecheck pass;
OpenAPI0 errors/102 warnings. All acceptance processes ended.

## Boundaries and next work

OPS-07/CC-05 remain compound pending final source reconciliation09-17 and retained demo09-18.
No blanket acceptance of148 source display occurrences: source-specific copy, rail/header
placement and fields absent from provider summaries remain explicit09-17 obligations.
Claim table narrow columns wrap dates/status; record as LOW presentation refinement09-17.
Unknown financial/settlement facts remain Not advised, never fabricated zeroes.
Human UAT, real provider transport and retained CoverMGA_Demo migration are unperformed.
frontend-code and retained keys remain unchanged. Continue09-14 MID submissions.
