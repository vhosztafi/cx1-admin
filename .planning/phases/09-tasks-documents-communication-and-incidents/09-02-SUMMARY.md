---
phase: 09-tasks-documents-communication-and-incidents
plan: '02'
status: complete
completed: 2026-09-21
requirements: [OPS-01]
---

# 09-02 — Scoped subjects and task persistence/API

Implemented the bounded backend slice. OperationalSubject has exactly one typed parent FK, filtered unique identity and immutable parent/creator. OperationalTask has rowversion, stable reference, source type/state union and due date; events/comments are append-only and checklist definitions immutable. Migration20260921131842_OperationalTasks, OperationalTaskGuards and EF snapshot enforce these constraints. Foundation committed in31c5248. No retained demo database migration/reset was performed.

TaskService.Register/Create/Read/Update/Transition/Checklist/Comment/BulkAssign/BulkDue/BulkComplete use held current parent/identity authority before receipt replay. Assignment revalidates current active internal user or a team with an eligible member for every selected subject. All selected heads/ETags are checked under ordered locks before changes. Failure rolls back effects/events/audit/receipt. Complete/cancel/reopen require reasons; reopening preserves prior immutable events and never decides a referral. Checklist snapshots are retained in each event. Subject links resolve actual agents/clients/quotes/policies/drafts routes.

TaskEndpoints and TaskDiscoveryEndpoints are wired in Program/DI and explicit authorization policies. Closed bounded transport rejects duplicate/unknown/null/system fields; CSRF and per-head versions protect commands. List discovery filters typed parent authority before counts/search/view/due/type/priority/owner/team, uses true creator and derives overdue in London. Cursor binds current identity/filter and visible task plus relevant owner versions; another user, task change or assignee team change invalidates it. Comments/events paginate under held task scope. OpsTask now includes each row's strong etag so bulk commands use the version displayed to the user. Thirteen task/subject OpenAPI operations are marked09-02 API-verified; other operational families remain contract-only.

## Evidence

-27 unit cases(task rules17 plus retained ActorContext10): .local/phase9-02-final-unit.
-5 integration cases,3 actualSQL scenarios: .local/phase9-02-final-integration-r2. SQL tests execute sequentially by existing assembly setting. Covers invalid/duplicate/foreign parent storage, current/suspended/forged/external identity, append-only history/reload, exact replay, stale update, atomic invalid bulk rollback, team/user assignment, checklist/comment snapshot/retry, real login/CSRF, concurrent changed same-key command, creator-v-owner views, hidden task counts/detail/history, cursor foreign/stale/roster changes, and revoked relationship before saved registration replay.
-Strict .local/phase9-02-final-strict confirms32 unique passes/3 realSQL/no skips. Assembly hash saved.local/phase9-02-final-assembly.json.
-Root411/411 (.local/phase9-02-root.log); final generated contract/source/API checks57/57 (.local/phase9-02-final-contracts.log). OpenAPI valid(exit0,87 retained warnings) .local/phase9-02-openapi.log; generated TS compiles with installed5.9.3.
-Strict backend build0warnings/errors. git diff --check passed. Reviewed exact schema/runtime correspondence, scope before replay, rollback and no parent locking after task mutation. Fixed owner-team cursor invalidation and made assignment capability explicit during review. Initial missing-service compile, missing SQL immutability and mistaken400-vs403 CSRF expectation are retained failed evidence, not counted as passes.

## Downstream boundaries

Task screens/browser/source-control acceptance belong09-03; published workflow task/checklist provenance belongs09-04. Manual tasks start without invented completed evidence; workflow source snapshots supply their required checklist.09-03 may add scoped presentation/eligible-assignee projections and readable reference allocation needed by source UI; current task references are full stable UUID-based strings. External agency/finance task workflows need their later explicit parent scopes, not permission broadening. Existing data/prototype/sales snapshot untouched. No human UAT or Phase9-wide acceptance claimed; OPS-01 remains open until its UI/workflow obligations close.
