---
phase: 12-dashboards-search-and-reports
plan: '03'
status: complete
requirements_completed: []
---
# 12-03 — Insurance report library

Five actual definitions now run: underwriting performance, policy transaction movements, active policy portfolio, renewal retention and invitations due. Date windows use inclusive London calendar days, translated to UTC boundaries. Current capabilities and quote/policy lineage govern rows. Bound GWP uses pinned first-issue rating; portfolio movement uses the existing authorised policy financial source. Snapshot and transaction views are distinct. Renewal due dates reuse RenewalLifecycleRules and saved settings, exclude acceptance/invitation/successor/lapse, and expose the rule ID. Rate denominators are explicit and zero denominators return null.

The library has applicable product/agency/provider/underwriter filters, period/custom dates, role-specific definition counts, grouped breakdown and bounded source rows/real links. More than 10,000 sources returns an explicit narrowing error. Finance/operations definitions remain unpublished until 12-04. Corrected the dashboard receipt link to the actual payments tab and agency context.

Evidence: `.local/phase12-tests/insurance-final.trx` passes the expanded SQL cohort case, including first-issue premium, empty conversion, active snapshot, future retention denominator, invitation before/inside the saved rule window and role/filter denials. `insurance.trx` is the earlier passing version. `insurance-browser.json` passes three saved product quotes, exact source count, quote link and agency filter reload. `contracts-insurance.txt` has 45/45 passes; TypeScript/lint/final API build pass. Screenshot: `output/playwright/phase12-report-library.png`.

The browser fixture was additively extended with a fictional agency/client/terms and three service-created quotes under the existing owned-database guard. An initial fixture terms attempt failed because multiple product versions were selected; it was resumed using one published version per product. No retained demo changes or broad/human acceptance.
