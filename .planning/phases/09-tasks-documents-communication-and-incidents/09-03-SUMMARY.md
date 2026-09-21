---
phase: 09-tasks-documents-communication-and-incidents
plan: '03'
status: complete
completed: 2026-09-21
requirements: [OPS-01]
---

# 09-03 — Persistent task screens

Delivered /tasks and /tasks/[id] with scoped My open/Team queue/Created by me/Completed views, real5-KPI summary, search/priority/type/due filters, visible-row selection and reasoned atomic bulk reassignment/due/completion. Detail has Task/Linked records sections, editable title/type/priority/due/eligible assignment, required checklist, internal comments and immutable readable activity, explicit complete/reopen via state transition, current-source warning and linked record/ownership rail. Attachment text honestly awaits09-08. Manual tasks do not invent workflow evidence.

Create task uses a role-appropriate paged parent selector and independently recoverable subject registration/create commands. Saved quote/policy/agency/client-relationship/servicing-draft entrypoints pass actual parent IDs, including Commercial Combined. Names/references replace UUID display. TaskReferenceSequence migration20260921140631_TaskReferences preserves previous identifiers. No migration or seed was applied to retained CoverMGA_Demo.

Pending commands freeze body/key/actor and displayed ETags. Current account/CSRF are refreshed before submission; uncertain response retries the same action. Stale errors retain forms and selections. Saved-version review requires explicit adoption before a new command; bulk conflict does not silently select newer versions. Server command scope remains authoritative.

## API refinements and commits

b5b1ee7 added task command client and5 meaningful tests;1462acf added scoped TaskPresentation and readable references;fe335d5 added screen/history/KPI foundation;43965ff added eligible assignee discovery. Final creation/contextual/browser changes follow in the plan completion commit. Exact signatures/routes are documented in docs/design/OPERATIONS-CONTRACTS.md and generated OpenAPI. GET tasks/summary counts recent immutable completion events rather than comments. GET task-assignees checks1..100distinct subjects against current command assignment eligibility and exposes only eligible labels with bounded result count/search.

## Verification

- Strict.local/phase9-03-final-strict:29 unique passing cases,27 task/actor units and2realSQL API/browser cases, no skips. Reports combine final-unit, assignees-green-r2 and final-browser; repeated exploratory runs are not added.
- Final browser16checks plusSQLreadback passed in.local/phase9-03-final-browser. Evidence.local/browser-evidence/tasks/CoverMGA_Test_e27afa8c73534cb69ac20e276e16f678. Default node scripts/verify-task-browser.mjs validates current source hash and saved SQL evidence without rerunning acceptance. Test assembly SHA256:6AE912759BEB20E2D4ACCD7C5180E363CF7BFC09E52F249895500E69D4CEC70E.
- Browser proves actual creation/reload/eligible assignment, lost response after persisted comment with same-key retry and one effect, stale edit preservation/review, complete/reopen reasons, required checklist rejection/save, atomic bulk stale rollback with selection retained, due/reassignment persistence, creator-versus-owner distinction/search, Escape focus return, desktop and390px layout, immutable event readback. C# independently reads saved task/comment/checklist/reopen event fromSQL.
- Root411/frontend177 tests passed in.local/phase9-03-root.log andfrontend.log.59focused contract/client checks and validOpenAPI87pre-existing warnings. Final8task/source checks passed. TypeScript/focusedlint pass; final productionbuild.local/phase9-03-final-build.log passes with isolateddist.local/next-phase9-03-browser.
- Desktop and390px screenshots inspected. Review fixed unstyled filters, accessible names for selects/retained textareas, role-inappropriate initial parent lookup, and raw event codes. Failed test-host fallback5000 and worker Fetch.status() were corrected, not suppressed. Reused webpackcache caused WasmHash failures; clearing only the resolved isolated disposable cache restored builds. No live preview process was replaced.

## Review and downstream obligations

Inline diff/security/accessibility review found no remaining HIGH/CRITICAL issue for this slice. No real messages/payments, external deployment, identity privilege changes, or sales-funnel edits. Current source ledger distinguishes verified generic task mechanics from source-specific workflow content still requiring09-04 and final09-17 reconciliation. Published rule/source provenance, resolved-source state and actual generated checklist wording belong09-04; documents/attachments09-08. OPS-01 remains open until its workflow/final phase acceptance obligations close. No human business or assistive-technology UAT, hostedCI, Docker runtime or Phase9-wide completion is claimed.

Next:09-04 published workflow-task rules, stable event identities, transactional deduplication and real source adapters.
