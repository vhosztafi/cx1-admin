---
phase: 12-dashboards-search-and-reports
plan: '01'
status: complete
requirements_completed: [RPT-01]
---
# 12-01 — Scoped search

Delivered current-role SQL search for clients, quotes, policies, agencies and matching reviews, with source-pinned registration predicates, typed advanced filters, 25-row stable paging, change detection and authorised links. Shell search and advanced filters work; supplied policy effective time explicitly selects historic policy registrations. Applied filters are retained in the URL. Product/provider/underwriter filters use saved IDs and available options. Existing family capabilities remain unchanged.

Production: `5655102`.

Evidence: `search-corrected.trx` has one nonzero SQL case passing, covering stored quote/policy/registration reads, denied families/agency identity, revoked roles, invalid combinations, stale paging and 30-row page boundaries. `search.trx` preserves the earlier failed EF projection attempt. `.local/phase12-tests/search-browser.json` passes saved client filtering, detail navigation and reload; quote checks use the separate SQL fixture. Typecheck/lint/build pass; `contracts-search.txt` records 45/45 API contract tests. Screenshot: `output/playwright/phase12-search.png`.

No retained business data changed. The existing isolated browser fixture contains clients but no capturable quotes; attempted draft setup did not create quotes, so the browser journey uses real saved clients. No broad regression or human UAT claimed. Additional per-source binding review and shared generated reporting types close in 12-05.
