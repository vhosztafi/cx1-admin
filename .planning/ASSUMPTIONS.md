# Assumptions and scope decisions

Status: proposed implementation defaults except explicitly confirmed scope. Commercial Combined assumptions are authorised by the user; validate them through demo UAT before production use.

| ID | Assumption / decision | Basis | Revisit |
|---|---|---|---|
| A-01 | UK-oriented demo, GBP; store instants in UTC and contractual local dates/times with explicit Europe/London interpretation | Prototype UK addresses/currency | Phase 1 date cases |
| A-02 | One MGA organisation, several capacity providers and agencies; agency-scoped access | Prototype | Phase 1 permission matrix |
| A-03 | Motor Trade Road Risks, Motor Trade Combined and Commercial Combined are distinct product codes | Prototype | Phase 1 schema |
| A-04 | CC uses locations/property, protections, liabilities, wage rolls, business interruption and loss history | `pCcPolicy`, CC capture | Phase 8 |
| A-05 | CC shares issue, adjustment, renewal, cancellation and finance mechanics; demo rating/authority differ by product | User permits assumptions; prototype shared model | Phase 8 |
| A-06 | Motor Trade Fleet remains draft product configuration and cannot be quoted/bound until published with complete definitions | Prototype draft | Phase 11 |
| A-07 | No public agency portal; internal agency-sharing preview and scope checks are functional | Explicit `pPortal` description | Future portal milestone |
| A-08 | Local users/password sessions first; prototype MFA/account controls added using local identity capabilities, not Entra | User simple auth plus prototype functional parity | Phases 2 and 11 |
| A-09 | Safe default rejects adjustments earlier than the latest issued effective change; ordinary backdating within that boundary needs authority and a reason | Prototype intent; hardcoded dates not reusable | Phase 1 worked cases |
| A-10 | No retroactive rebasing engine in v1; explicitly blocked cases explain why | Derived from A-09 | Future policy engine |
| A-11 | Money calculations use decimal arithmetic; API monetary values are decimal strings with currency; explicit rounding and tax/commission bases | Engineering proposal | Phase 1 worked examples |
| A-12 | Demo tax rates and financial rules are configurable sample values, not validated current legal advice | Prototype mock values | Production readiness |
| A-13 | Seed demo clock is configurable and consistent across policy terms, tasks and renewal windows | Contradictory prototype dates | Phase 2 seeds |
| A-14 | Development document files use a persistent filesystem volume behind a blob-store interface; metadata in SQL Server | MVP simplicity | Phase 9 |
| A-15 | Adapter scenarios include success, failure, timeout, retry and duplicate delivery; no real external action | Explicit user confirmation | All feature phases |
| A-16 | Dashboard/report totals are computed from seed records; decorative prototype counts are not mandatory constants | Persistent functionality | Phase 12 |
| A-17 | Duplicate matching proposes candidates and records decisions; no silent cross-agency disclosure or automatic destructive merge | Prototype match review | Phase 3 |
| A-18 | CC new-business capture is internal back-office capture; no new customer sales funnel is built | User clarification | Phase 8 |

Unresolved legacy funnel clarifications remain unresolved reference facts. New demo defaults must be explicitly labelled and must not overwrite or claim to resolve them.
