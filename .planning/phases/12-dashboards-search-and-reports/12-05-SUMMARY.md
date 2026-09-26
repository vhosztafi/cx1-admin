---
phase: 12-dashboards-search-and-reports
plan: '05'
status: complete
requirements_completed: [RPT-03, RPT-04, RPT-05]
---
# 12-05 — Saved reports, CSV and acceptance

Owner-scoped immutable settings retain up to 100 favourites and 20 recent filter/run records. Favourites have real IDs and optimistic collection versions; current report capabilities are checked on reads, changes, reruns and exports. No cached result grants access. The UI saves, reloads, updates/removes favourites, recalls recent filters, and downloads all matching sources as CSV. Applied filters, generation time, stable source/rule identities and safe columns are explicit. CSV quotes text and neutralises spreadsheet formulas while preserving signed numeric money. Over 10,000 sources is a clear narrowing error, not truncation. Unsupported formats and undeclared filter fields are rejected.

The generated OpenAPI and frontend types describe the implemented synchronous export and owner preferences, replacing old export-job aliases. `12-DELIVERED-BINDINGS.json` covers exactly all 66 original control IDs plus shell alerts and favourite actions; the original snapshot is unchanged. Task service time uses the first completion snapshot in the current completed sequence, with its event identity, rather than mutable last-update time.

Evidence: focused saved/export SQL and CSV cases in `.local/phase12-tests/saved-duration-corrected.trx`; signed cash/debt and normal closed-period reconciliation in `finance-signed.trx`; 45 API contract checks in `contracts-final.txt`; TypeScript, lint and API build pass. `saved-export-browser.json` records five checks: three saved product cohorts, favourite ID/filter reload, complete CSV source IDs, stored quote drill-down and persistent removal. The browser caught an empty filter after recall; waiting for current options and remounting the form fixed it. A subsequent ambiguous library/recent-run test selector was narrowed. Screenshot `output/playwright/phase12-saved-report.png` was visually inspected.

Earlier failed diagnostics remain local. The extra cash fixture initially correctly blocked period close until reconciliation; moved the independent signed-cash scenario to the next open period after the already-tested legitimate close. No close guard was bypassed. No retained demo mutation, unfiltered SQL suite, human UAT or production deployment is claimed. See `12-VERIFICATION.md` for the concise aggregate and residual limits.
