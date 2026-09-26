# Requirements: Cover MGA Back Office

**Milestone:** v1.0 Functional Back Office MVP
**Defined:** 2026-09-13
**Status:** Approved by the user on 2026-09-13; autonomous implementation authorised. Requirement completion remains subject to verification.

Core value: complete persistent insurance and servicing journeys with consistent versions, decisions, documents and finance.

## v1 requirements

### Design contracts

- [x] **DES-01**: Reviewer can trace every prototype screen, tab, modal, link, command and disabled-state rule to an implementation requirement and acceptance scenario.
- [x] **DES-02**: Reviewer can inspect an ERD, relational data dictionary, keys, indexes and retention rules that distinguish clients, people, quotes, policies, terms, transactions, versions and financial entries.
- [x] **DES-03**: Reviewer can validate versioned JSON schemas and realistic examples for both Motor Trade products and Commercial Combined, including extension and migration rules.
- [x] **DES-04**: Reviewer can inspect OpenAPI request/response/error examples, permissions, pagination, concurrency and idempotency rules for every feature command.
- [x] **DES-05**: Reviewer can inspect state-transition and authority tables plus worked examples for issue, amendment, renewal, cancellation, effective dates and financial calculations.
- [x] **DES-06**: Reviewer can trace Motor Trade fields and option identities to the read-only funnel reference, with missing or conflicting evidence explicitly recorded.

### Persistent foundation

- [x] **FND-01**: Developer can run the Next.js application, .NET API and SQL Server with documented commands, version pins, migrations and persistent volumes.
- [x] **FND-02**: Staff can sign in with seeded local accounts, keep a secure session across refresh, sign out and have API requests checked against server-side permissions.
- [x] **FND-03**: Staff can navigate a responsive shell matching the prototype's typography, blue record headers, sidebar, tabs, tables, action rails and meaningful loading/error/empty states.
- [x] **FND-04**: Developer can seed a repeatable fictional dataset with a consistent clock and reset only a designated demo database through an explicit command.
- [x] **FND-05**: Developer can run unit tests, API checks and SQL Server integration tests in the documented verification workflow.
- [x] **FND-06**: Staff can see audited mutations and durable demo-adapter jobs that survive application restart; sensitive values are excluded from ordinary logs.

### Clients and contacts

- [x] **CLI-01**: Staff can search, filter and page clients and open linked policies, quotes, contacts and activity.
- [x] **CLI-02**: Staff can create and update business accounts and contacts, recording legal identity, addresses, consent and a single primary contact.
- [x] **CLI-03**: Authorised staff can add, review, amend and resolve person-level support flags, with audit and separate internal versus agency wording.
- [x] **CLI-04**: Staff can review duplicate-match evidence and record link, new-client or reject decisions without exposing another agency's restricted records.

### Agencies

- [x] **AGY-01**: Staff can create and resume agency onboarding with contacts, products, commission terms, required evidence and activation checks.
- [x] **AGY-02**: Authorised staff can maintain agency users and invitations, expire/resend/revoke invitations, suspend access and enforce internal/agency role separation.
- [x] **AGY-03**: Staff can view agency products, access settings, accounts and activity from persisted records.
- [x] **AGY-04**: Staff can inspect an internal agency-sharing view with field-level restrictions; an agency-scoped API identity cannot access another agency's records.
- [x] **AGY-05**: Agency activation and user invitation create persistent demo notifications with visible delivery outcomes.

### Product and quote capture

- [x] **QUO-01**: Staff can browse, search, sort, filter and page policies/quotes by product, agency, client, reference, registration and status.
- [x] **QUO-02**: Staff can create, save and resume Motor Trade Road Risks and Motor Trade Combined quotes linked to a client, agency and effective product configuration.
- [x] **QUO-03**: Staff can capture business activities, experience, declarations, premises, cover, endorsements, previous insurance and claims history with field validation.
- [x] **QUO-04**: Staff can add/edit/remove drivers, licence evidence, convictions, losses, vehicles and trade plates with stable risk-item identities.
- [x] **QUO-05**: Staff can run deterministic postcode/vehicle/driver lookup adapters, inspect failure outcomes and complete supported manual entry.
- [x] **QUO-06**: Staff can revise, clone and withdraw quotes with reasons and version history; changed risk or terms invalidates prior rating and acceptance.

### Rating, referrals and issue

- [x] **UWR-01**: Staff can rate a quote and inspect reproducible premium components, factors, rule version and rate-input version.
- [x] **UWR-02**: Staff can submit and route referrals generated from all applicable product authority dimensions and missing information.
- [x] **UWR-03**: Authorised underwriters can approve, condition, query or decline referrals with actor, reason and evidence; insufficient authority blocks decision or issue.
- [x] **UWR-04**: Staff can escalate to a capacity provider, record correspondence, process deterministic responses and resolve attached conditions.
- [x] **UWR-05**: Staff can send a quotation through a demo delivery record and record acceptance of its exact current version and terms.
- [x] **UWR-06**: Staff can bind only a valid, current, accepted quote whose referrals and evidence are satisfied; repeated issue requests cannot create duplicate policies or charges.
- [x] **UWR-07**: Staff can open the issued policy, term, immutable risk snapshot and transaction, with initial financial obligations and durable document/delivery work committed together.

### Policy servicing

- [x] **POL-01**: Staff can inspect policy summary, product sections, drivers/vehicles, documents, transactions, finance, tasks, notes, messages and claims using linked persisted records.
- [x] **POL-02**: Staff can create an adjustment draft, compare proposed changes by stable item identity, edit effective date and resume the draft without changing in-force cover.
- [x] **POL-03**: Staff can acquire/release an expiring editing lease, take over with permission and reason, and receive a clear conflict when saving a stale revision.
- [x] **POL-04**: Staff can rate, refer, obtain acceptance, issue or abandon an adjustment with recalculated premium and all issue prerequisites enforced.
- [x] **POL-05**: Staff can inspect issued versions and before/after differences and query policy history by effective date or processing date; drafts never alter issued history.
- [x] **POL-06**: Staff receive explicit blocks for out-of-term, unsupported out-of-sequence or conflicting changes, and authority checks for permitted backdating.
- [x] **POL-07**: Staff can prepare renewal terms from the correct expiring snapshot, rate, resolve referrals, invite, record acceptance and issue a linked new term.
- [x] **POL-08**: Staff can lapse an unaccepted renewal with reason and notification; date-driven due/overdue states use the configured clock.
- [x] **POL-09**: Staff can review cancellation dates, reasons and premium return, obtain required approval and issue cancellation with a refund obligation and notification.

### Commercial Combined

- [x] **CC-01**: Staff can create/resume an internal Commercial Combined quote using the shared client, agency and quote lifecycle.
- [x] **CC-02**: Staff can maintain locations, construction/protections, property sums insured, liabilities, wage rolls, business interruption and losses using product-specific JSON sections.
- [x] **CC-03**: Staff can rate and refer CC risks using documented demo assumptions, including location exposure, estimated loss and postcode aggregation.
- [x] **CC-04**: Staff can issue and service CC policies through adjustments, renewals, cancellation and historical views without Motor Trade-only tabs or validation.
- [x] **CC-05**: Staff can log CC property/liability incidents and generate product-appropriate document content through the shared operational infrastructure.

### Work management

- [x] **OPS-01**: Staff can view personal/team queues, create tasks, set priority/due date/owner, link records, change status and record completion reasons.
- [x] **OPS-02**: Stored workflow rules create tasks for referrals, missing information, renewals, agency evidence and integration exceptions without duplicates.
- [x] **OPS-03**: Staff can add internal notes and send agency-visible thread messages with attachments, recipients and visibility enforced on the server.
- [x] **OPS-04**: Staff can generate, preview and download actual policy/quote/renewal/cancellation documents linked to the exact source version and template version.
- [x] **OPS-05**: Staff can upload and retrieve evidence files through authorised endpoints and see durable metadata, checksums and document version history.
- [x] **OPS-06**: Staff can send/resend a document pack and inspect delivery failures/retries; historical issued documents retain their original content.
- [x] **OPS-07**: Staff can log a Motor Trade or CC incident, select relevant risk items, send a demo claims handoff and view returned administrator summaries.
- [x] **OPS-08**: Staff can inspect and retry MID/data-reporting exceptions for relevant Motor Trade vehicles with stable request and response history.

### Accounting

- [x] **FIN-01**: Finance users can inspect transaction-ledger entries and agency balances derived from issued insurance movements.
- [x] **FIN-02**: Finance users can generate and download agency statements with period filters and consistent opening/movement/closing balances.
- [x] **FIN-03**: Finance users can record demo receipts, assign payers, allocate/unallocate amounts to invoices and see residual balances without over-allocation.
- [x] **FIN-04**: Finance users can reconcile recorded bank lines against receipts/refunds/settlements and resolve or explain variances with an audit record.
- [x] **FIN-05**: Authorised finance users can approve/reject refunds within limits and execute a retry-safe demo payment with persisted outcome.
- [x] **FIN-06**: Finance users can generate versioned bordereau batches, inspect validation failures, correct data or exclude records with reasons, revalidate and export CSV.
- [x] **FIN-07**: Finance users can submit only valid bordereau batches through the demo adapter and inspect submission status and history.
- [x] **FIN-08**: Finance users can close a period and handle later corrections through auditable adjustments rather than editing posted entries.

### Administration and accounts

- [x] **ADM-01**: Administrators can maintain product/scheme versions, capacity providers, cover sections, effective dates and draft/published status.
- [x] **ADM-02**: Administrators can configure multidimensional delegated authority and referral routing with effective dates, approval evidence and audit.
- [x] **ADM-03**: Administrators can maintain workflow types, queues, assignment rules, document/message templates and organisation/reference/notification settings.
- [x] **ADM-04**: Administrators can manage internal users, teams, roles and authority, with required second-person approvals for sensitive identity changes.
- [x] **ADM-05**: Users can update permitted profile fields, change passwords, inspect/revoke sessions and complete functional local MFA enrolment, verification, recovery and disable/reset flows.
- [x] **ADM-06**: Administrators can suspend accounts or force resets; revoked sessions and disabled users lose API access.
- [x] **ADM-07**: Authorised users can search/filter audit history and inspect actor, timestamp, reason and appropriate before/after fields.
- [x] **ADM-08**: Administrators can inspect integration health, adapter scenario configuration, failed jobs and retry history without viewing secrets.

### Reporting and discovery

- [x] **RPT-01**: Staff can use global and advanced search across authorised clients, quotes, policies and registrations and navigate to matching records.
- [x] **RPT-02**: Staff can see dashboard counts, queues, recent activity and renewal/exception measures computed from stored records.
- [x] **RPT-03**: Staff can run distinct underwriting, portfolio, renewal, finance, agency-performance and compliance/exception reports with relevant filters and drill-down.
- [x] **RPT-04**: Staff can export authorised report results and manage recent/favourite reports; agency and sensitive-field restrictions apply to exports.
- [x] **RPT-05**: Staff can reconcile report measures and dashboard totals against underlying records, including empty results and defined date/earning bases.

### Demo acceptance

- [x] **ACC-01**: Business users can follow a documented demo script covering Motor Trade quote issue, blocked/referral adjustment, renewal, cancellation/refund and CC servicing.
- [ ] **ACC-02**: Business users can restart the application and database and recover saved work, adapter outcomes, files, history and balances.
- [x] **ACC-03**: Reviewers can verify every prototype action has a working destination or documented domain-based disabled state and acceptance evidence.
- [x] **ACC-04**: Developers can run passing unit, component, API, real-SQL integration and critical browser checks, including permission denial, stale writes, rollback and retries.
- [x] **ACC-05**: Reviewers can compare rendered key screens with the prototype and confirm usable desktop/narrow layouts and keyboard interaction.
- [x] **ACC-06**: Developers can use setup, seed/reset, troubleshooting and module-boundary documentation to prepare later project-by-project improvements.

## Cross-cutting completion rules

Each feature phase includes UI, API, SQL persistence, relevant fictional seed scenarios and unit tests. Domain changes include meaningful rule tests; persisted workflow invariants include SQL Server integration tests. Server-side permissions, validation, audit, error states and reload behaviour are part of each requirement, not postponed to acceptance. Prototype control inventory is refined in Phase 1 and any uncovered capability gets a new requirement and owner.

## Future requirements

- FUT-01: Connect the existing Motor Trade sales funnel through a reviewed adapter without changing its confirmed field semantics.
- FUT-02: Add Azure Entra authentication behind the established identity mapping.
- FUT-03: Build a separate broker portal and Commercial Combined customer sales funnel.
- FUT-04: Replace demo integrations with contracted production services.
- FUT-05: Improve individual modules following business acceptance of MVP.

## Out of scope

No changes in frontend-code; no real external delivery or money movement; no separate portal; no full claims administration; no general out-of-sequence policy rebasing engine. CC demo assumptions are documented in ASSUMPTIONS.md. Local MFA is included to honour the prototype, while the identity provider remains simple/local.

## Traceability

Every v1 requirement has one owning delivery phase; later phases may consume its outputs.

| Requirement | Phase | Status |
|---|---|---|
| DES-01 | 1 | Complete |
| DES-02 | 1 | Complete |
| DES-03 | 1 | Complete |
| DES-04 | 1 | Complete |
| DES-05 | 1 | Complete |
| DES-06 | 1 | Complete |
| FND-01 | 2 | Complete |
| FND-02 | 2 | Complete |
| FND-03 | 2 | Complete |
| FND-04 | 2 | Complete |
| FND-05 | 2 | Complete |
| FND-06 | 2 | Complete |
| CLI-01 | 3 | Complete — Phase6 verified2026-09-17 |
| CLI-02 | 3 | Complete: native SQL/API/browser verified2026-09-14 |
| CLI-03 | 3 | Complete: native SQL/API/browser verified2026-09-14 |
| CLI-04 | 3 | Complete: saved intake review verified2026-09-14; quote integration due5 |
| AGY-01 | 4 | Complete |
| AGY-02 | 4 | Complete |
| AGY-03 | 4 / 10 / 13 | Complete — compound evidence reconciled in Phase 13 acceptance record |
| AGY-04 | 4 / 5 / 6 / 9 / 13 | Complete — compound evidence reconciled in Phase 13 acceptance record |
| AGY-05 | 4 | Complete |
| QUO-01 | 5 / 6 | Complete — Phase6 verified2026-09-17 |
| QUO-02 | 5 | Complete — Phase5 verified2026-09-16 |
| QUO-03 | 5 / 6 | Complete — Phase6 verified2026-09-17 |
| QUO-04 | 5 | Complete — Phase5 verified2026-09-16 |
| QUO-05 | 5 | Complete — Phase5 verified2026-09-16 |
| QUO-06 | 5 / 6 | Complete — Phase6 verified2026-09-17 |
| UWR-01 | 6 | Complete — Phase6 verified2026-09-17 |
| UWR-02 | 6 | Complete — Phase6 verified2026-09-17 |
| UWR-03 | 6 | Complete — Phase6 verified2026-09-17 |
| UWR-04 | 6 | Complete — Phase6 verified2026-09-17 |
| UWR-05 | 6 | Complete — Phase6 verified2026-09-17 |
| UWR-06 | 6 | Complete — Phase6 verified2026-09-17 |
| UWR-07 | 6 | Complete — Phase6 verified2026-09-17 |
| POL-01 | 7 / 8 / 9 / 10 / 13 | Complete — compound evidence reconciled in Phase 13 acceptance record |
| POL-02 | 7 | Complete |
| POL-03 | 7 | Complete |
| POL-04 | 7 | Complete |
| POL-05 | 7 | Complete |
| POL-06 | 7 | Complete |
| POL-07 | 7 | Complete |
| POL-08 | 7 | Complete |
| POL-09 | 7 | Complete |
| CC-01 | 8 | Complete — 2026-09-21;08-VERIFICATION |
| CC-02 | 8 | Complete — 2026-09-21;08-VERIFICATION |
| CC-03 | 8 | Complete — 2026-09-21;08-VERIFICATION |
| CC-04 | 8 | Complete — 2026-09-21;08-VERIFICATION |
| CC-05 | 8, 9 | Complete — Phase8 product payloads plus Phase9 CC incident logging, actual document rendering and demo delivery verified2026-09-23;09-VERIFICATION |
| OPS-01 | 9 | Complete — 2026-09-23;09-VERIFICATION |
| OPS-02 | 9 | Complete (09-04) |
| OPS-03 | 9 | Complete — 2026-09-23;09-VERIFICATION |
| OPS-04 | 9 | Complete — 2026-09-23;09-VERIFICATION |
| OPS-05 | 9 | Complete — 2026-09-23;09-VERIFICATION |
| OPS-06 | 9 | Complete — 2026-09-23;09-VERIFICATION |
| OPS-07 | 9 | Complete — 2026-09-23;09-VERIFICATION |
| OPS-08 | 9 | Complete — 2026-09-23;09-VERIFICATION |
| FIN-01 | 10 | Complete — local automated evidence |
| FIN-02 | 10 | Complete — local automated evidence |
| FIN-03 | 10 | Complete — local automated evidence |
| FIN-04 | 10 | Complete — local automated evidence |
| FIN-05 | 10 | Complete — local automated evidence |
| FIN-06 | 10 | Complete — local automated evidence |
| FIN-07 | 10 | Complete — local automated evidence |
| FIN-08 | 10 | Complete — local automated evidence |
| ADM-01 | 11 | Complete |
| ADM-02 | 11 | Complete |
| ADM-03 | 11 | Complete |
| ADM-04 | 11 | Complete |
| ADM-05 | 11 | Complete |
| ADM-06 | 11 | Complete |
| ADM-07 | 11 | Complete |
| ADM-08 | 11 | Complete |
| RPT-01 | 12 | Complete |
| RPT-02 | 12 | Complete |
| RPT-03 | 12 | Complete |
| RPT-04 | 12 | Complete |
| RPT-05 | 12 | Complete |
| ACC-01 | 13 | Complete on documented local evidence |
| ACC-02 | 13 | Partial — application restart passed; database engine restart unperformed |
| ACC-03 | 13 | Complete on documented local evidence |
| ACC-04 | 13 | Complete on documented local evidence |
| ACC-05 | 13 | Complete on automated visual/keyboard evidence; human review outstanding |
| ACC-06 | 13 | Complete on documented local evidence |

**Coverage:** 83 v1 requirements; 83 mapped; 0 unmapped. Completion is recorded per requirement above; compound requirements remain partial until their downstream acceptance passes.
