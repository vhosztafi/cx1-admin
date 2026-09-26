# Phase 12 delivery agreement

Auto-transition from completed Phase 11. Follow PROJECT.md's 2026-09-26 lightweight agreement: five sequential inline feature slices, no research/checker/UI/audit agents, focused nonzero checks and saved-data journeys. Scope: RPT-01..05 and all 66 original RPT control identities. Keep original source inventory unchanged.

1. Scoped global/advanced search.
2. Stored dashboard queues/activity and own alerts.
3. Underwriting, portfolio and renewal reports.
4. Finance, agency performance and compliance/exception reports.
5. Saved reports, recent runs, CSV and concise acceptance.

Existing code provides SQL identity locks, role-specific domain visibility, QuoteRegistration/PolicyRegistration, real AssignedUserId, operational tasks/referrals, immutable policy versions and transactions, Phase 10 finance/earning models and versioned settings. Dashboard/search/report endpoints presently exist only as contracts, and dashboard/shell/reporting pages contain placeholders. Replace them directly. Reuse current APIs and primitive UI rather than adding infrastructure.

All three products are included. Distinct reports use explicit source/cohort/earning definitions, current role checks and matching count/drill-down/export predicates. Never grant finance to admins or risk access to finance merely to populate dashboards. Current-only registration search must not leak removed historical assets. Agency identities cannot enter internal reports; no external account or provider connection is needed. CSV is the source's actual export format; no unimplemented formats.

Keep retained database/files/keys and old preview untouched. Use a disposable fixture and existing migration tools if schema is necessary. Phase 11 isolated fixture is available, but has limited business cohorts; extend only owned test data. Phase 13 owns broader regression and human UAT, Phase 10 closed-period earned readback if not covered naturally here, and intermittent development navigation diagnostics.
