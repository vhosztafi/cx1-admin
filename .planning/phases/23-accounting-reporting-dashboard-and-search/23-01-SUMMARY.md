---
phase: 23-accounting-reporting-dashboard-and-search
plan: 01
status: implemented-with-final-verification-follow-up
---

Accounting preserves prototype tab order and selects saved agencies by name from finance-authorized report options, rather than requiring a technical ID or agency directory permission. Agency Accounts exposes authorized persisted broker balances. Reporting has category cards, library / selected report tabs and real filters/favourites/CSV exports. Dashboard uses the prototype greeting and contextual actions with actual queue counts and source links.

Agency Overview adds a read-only 90-day operational summary with preceding-period comparison, reusing bounded report calculations, current capability checks and one serializable read. It explicitly defines quote-created cohorts, current saved outcomes and pinned first-issue GWP, and does not create report preferences or recent runs. Generated OpenAPI now documents 540 runtime operations.

Verification: API build, targeted TypeScript/ESLint and the reporting insurance database test passed, including bound-cohort reconciliation and unchanged preferences. The additive browser check passed dashboard actions, real agency summary, report filters/favourite reload/CSV source IDs, search drill-down, all accounting tabs/account preservation, saved agency broker balances and 390px containment with no browser errors. Evidence: output/playwright/v1.1/discovery-alignment-report.json and reporting/accounting screenshots.

Final phase 24 follow-up: receipt allocation/refund outcomes and agency onboarding final checks. No mock numbers or unverified provider outcomes are claimed complete.
