---
phase: 12-dashboards-search-and-reports
plan: '02'
status: complete
requirements_completed: [RPT-02]
---
# 12-02 — Dashboard and own alerts

Stored personal/team task queues, overdue counts, current-cycle referrals, quotes, renewal window, servicing drafts, source-change exceptions, administrator failed jobs and finance receipt allocation queues now replace placeholders. Role-unavailable measures are null. Each count and bounded queue uses the same predicate in a serializable read; search count consistency was tightened too. Workload groups and own task history use existing TaskDiscovery visibility. UTC renewal intervals and London due dates are explicit. Alerts expose only own task/security notices; append-only owner settings preserve acknowledgement and do not discard older read IDs.

Evidence: `.local/phase12-tests/dashboard-final.trx` passes one nonzero SQL case for own counts, overdue membership, null restricted counts, acknowledgement/reload, foreign acknowledgement denial and revoked roles. Earlier failed files preserve fixture/EF diagnostics. `dashboard-browser.json` passes a newly saved isolated task, count/overdue/detail and read-acknowledgement reload. Typecheck/lint/final build pass; `contracts-dashboard.txt` has 45/45 passing tests. Screenshot `output/playwright/phase12-dashboard.png`.

No retained demo changes. The single fictional browser task is in the owned Phase 11 fixture. Broad/human acceptance remains Phase 13; shared source bindings close in 12-05.
