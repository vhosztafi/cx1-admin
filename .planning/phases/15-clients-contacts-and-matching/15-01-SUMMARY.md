---
phase: 15-clients-contacts-and-matching
plan: 01
status: implemented-with-follow-up
requirements: [CLI-05, CLI-06, CLI-07]
---

Restored contextual new quote and advanced search, full-width saved Motor Trade portfolio preview, account/activity headings, and matched-account portfolio/claims navigation. A sole active agency relationship is selected automatically for contacts; multi-agency selection and disclosure restrictions remain explicit. Client list trade activities now come from validated current Motor Trade proposals and remain restricted to quote readers.

Verification: API build passed with zero warnings/errors; TypeScript passed; focused ESLint passed after replacing effect-based contact selection with derived selection. Actual local app signed in against the retained fictional Phase 11 fixture: client list, two saved Motor Trade quotes, linked overview tabs and automatic sole relationship contact selection observed. Screenshot: output/playwright/v1.1/client-overview.png (ignored local evidence).

Follow-up: portfolio premiums and account exposure require the current rating/issued read models aligned in phases 18/20. Do not infer sanctions/appetite checks or claims disagreements from absent evidence. Match mutation behavior was preserved, not re-exercised or claimed as new human UAT. Final section verification in phase 24 must revisit these source differences; phase 15 is not yet declared fully verified.
