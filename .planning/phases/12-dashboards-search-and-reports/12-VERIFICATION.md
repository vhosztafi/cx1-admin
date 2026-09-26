---
phase: 12-dashboards-search-and-reports
status: passed
verified: 2026-09-26
requirements: [RPT-01, RPT-02, RPT-03, RPT-04, RPT-05]
---
# Phase 12 verification

All five implementation slices are delivered. Inline goal review found current identity locks and family permissions at every query/export, shared count/row predicates, pinned policy/quote/finance sources, explicit cohort/date/ratio definitions, real record links, and persisted owner preferences. No database migration was required.

| Requirement | Delivered evidence |
|---|---|
| RPT-01 | Global/advanced search, saved-ID filters, current or explicitly historical registrations, stable paging and scoped links. `search-corrected.trx`; two `search-browser.json` checks. |
| RPT-02 | Personal/team queues, source counts, overdue work, activity and own read acknowledgements. `dashboard-final.trx`; two `dashboard-browser.json` checks. |
| RPT-03 | Eleven distinct report definitions across six categories; 8 underwriting/servicing and 3 finance definitions according to role. `insurance-final.trx`, `finance-signed.trx`; three insurance and five saved/export browser checks. |
| RPT-04 | Owner IDs, optimistic edits, reload/delete, recent runs, current permission checks and complete safe CSV. `saved-duration-corrected.trx` (one SQL plus four CSV cases), saved/export browser. |
| RPT-05 | Pinned first-issue premium, active temporal policy selection, invitation rules, empty ratios, sealed written/closing finance, signed cash/debt, saved earning slices before/after a normal period close, and zero bordereau difference. Focused SQL above. Completion-event service duration is insulated from later task edits. |

Evidence aggregate: nine distinct backend cases (five isolated SQL scenarios and four CSV cases), 45 API contract cases, 12 saved-browser assertions across four focused journeys, TypeScript/lint/API build. Evidence is accumulated across the slices; this is not a fresh full-suite or exact-head certification. Source snapshot: 66/66 unchanged identities mapped in `12-DELIVERED-BINDINGS.json`. The main saved-report screenshot was inspected for usable table/filter/card layout.

## Boundaries and Phase 13

- Broad regression, human/assistive-technology UAT, narrow-screen comparison and development-navigation diagnostics remain Phase 13. No human acceptance is inferred from automation.
- Earned reports require complete past calendar months; a month crossing configured accounting-period boundaries fails explicitly rather than selecting an ambiguous cutoff. Annual periods and a normally closed annual period are covered. No automatic accounting configuration changes.
- CSV is a current rerun of applied filters, not an immutable historical result. It uses the same sources as the screen and includes up to 10,000 rows; beyond that the caller must narrow filters.
- The former Phase 10 closed-period earned-readback residual is covered by this phase's normal close/re-read test.
- Retained `CoverMGA_Demo`, files/keys and old 3100/5087 preview were untouched. Business fixtures and all SQL mutations were isolated. Owned API 5095 is stopped after browser acceptance; frontend 3193 remains owned.

Failed intermediate SQL/browser attempts are retained as diagnostics and superseded by the named passing evidence. No real external delivery/payment, hosted CI or production deployment is claimed.
