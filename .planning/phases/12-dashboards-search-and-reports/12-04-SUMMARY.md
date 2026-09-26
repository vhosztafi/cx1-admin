---
phase: 12-dashboards-search-and-reports
plan: '04'
status: complete
requirements_completed: []
---
# 12-04 — Finance, agency and exception reports

Six further definitions now use saved sources: financial performance, earned premium, bordereau reconciliation, agency conversion/service times, referral turnaround, and compliance/exceptions. Finance remains finance-only. Period movement and closing receivables are distinct; opening sources are explicit. Earning rows validate persisted component schedules and expose slice/period identities, with closed-period cutoffs. Latest provider/period batch members reconcile to their sealed journals. Complaint tasks, overdue support reviews and failed MID submissions expose safe metadata only.

Evidence: `.local/phase12-tests/finance-periods.trx` passes the focused SQL case: exact premium/closing balance, zero period movement with opening balance, cross-role denial, operational query translation, saved earned pence, bordereau zero difference, normal statement/bordereau-backed period closure and identical earned value after closure, plus complaint source/redaction. Earlier `finance.trx` exposed an untranslated legacy date expression; corrected to UTC boundaries for London days. `finance-corrected.trx` exposed annual rather than monthly accounting periods; periods now contain the saved month. The final saved/export browser journey also covers the agency report UI.

Schema clarification: complaints are operational tasks, not a support-flag category. No narrative, support category or provider payload enters these report rows. A month crossing accounting-period boundaries currently returns an explicit unavailable-period error instead of guessing a cutoff. Production commit: 7882e99. No retained database mutation or broad regression.
