---
phase: 05-motor-trade-quote-capture
plan: '09'
status: complete
completed: 2026-09-16
requirements_completed: []
requirements_supported: [QUO-06]
---

# 05-09 — revision history, cloning and withdrawal

Commits `d8ab279` (pure rules) and `13c8bf0` (runtime) complete owned paged revision history, exact typed comparison, historical cloning and atomic withdrawal. Comparison keys repeatable items by stable identity and retains full before/after values, distinguishing missing, null, false and zero. The UI uses catalogue labels and provides a horizontally scrollable history table on mobile.

Cloning validates the retained product/configuration, remaps every child identity and internal link, records provenance and creates an independent first revision. It transfers no evidence or lookup decisions. Destination agency terms are previewed and require explicit confirmation when changed. Held source/destination agency and quote authority precede successful receipt replay. Stale versions, foreign revisions, closed source drafts and revoked authority fail safely. Withdrawal records actor, reason and time while preserving history and evidence; terminal draft actions are unavailable. Both actions retain immutable request bodies, versions and keys through uncertain responses and check the current account before retry.

## Verification

- Final fresh `.local/phase5-lifecycle-final-20260916` and matching.log: **671 passing = 565 unit + 106 integration, 79 real SQL, 0 skips**. Result assertion671/79 passed; integration9m19s. Includes final closure reason/time HTTP assertions. Earlier full `.local/phase5-lifecycle-api-full-20260916` also passed671/79 before those final additions.
- Six semantic unit cases cover composed product fixtures, complete ID/link remapping, retained typed declarations, configuration rejection, keyed reorder/change detection, complete values and current quote/revision/hash matching.
- Two real SQL scenarios cover same-key concurrent clone, cross-agency terms, foreign/stale inputs, authority before replay, injected audit rollback, retained history and save-versus-withdraw serialization. Focused `.local/phase5-lifecycle-services-retest-20260916` passed2cases.
- Real-cookie HTTP scenario covers protected history/comparison cursors, source ownership, CSRF/version/closed inputs, clone and withdrawal replay, anonymous/current-role denial and terminal read capabilities. `.local/phase5-lifecycle-api-20260916` passed before final full regression.
- **79 frontend tests**, **294 contracts/339 operations**, lint, typecheck and production build pass. Logs: `.local/phase5-lifecycle-web-tests-final-20260916.log`, `phase5-lifecycle-contracts-accepted-20260916.log`, `phase5-lifecycle-lint-release-20260916.log`, `phase5-lifecycle-types-release-20260916.log`, `phase5-lifecycle-build-accepted-20260916.log`. One Windows libuv process-exit assertion was followed by a clean complete contract rerun.
- Final Chrome `.local/phase5-lifecycle-browser-accepted-20260916.log`: Combined source **QT-MT-0000000141**, clone **142**; Road Risks source **143**, clone **144**. Sources end at revision3; clones at revision1 withdrawn. Actual editor/history/comparison, historical clone, new IDs/no evidence transfer, lost-response exact retries, persisted closure reason, disabled terminal actions and stale concurrent-save clone dialog pass. Report and inspected desktop/390px screenshots in `.local/browser-evidence/quote-lifecycle/`.
- An initial browser harness race removed interception during clone navigation; awaiting the destination page corrected the harness. Mobile table compression was corrected before accepted screenshots. No application workaround for the harness race was introduced.
- No migration was required. Preserved fictional demo histories remain. All owned previews stopped after identity verification; final API44008/web30208. Diff checks passed; frontend-code unchanged.

## Next

05-10 discovery, client matching and safe agency summaries; initial integration findings are recorded in 05-10-IMPLEMENTATION-NOTES.md. Then 05-11 final acceptance. Phase5 and QUO requirements remain open until the final gate; no rating, issuing or external-provider completion is claimed.
