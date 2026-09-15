# Roadmap: v1.0 Functional Back Office MVP

**Status:** Autonomous progression authorised. Phases1–4 complete (24 plans). Phase4 final329backend/57SQL,81contracts,30frontend and all20browser journeys pass, including restart persistence. AGY-01/02/05 accepted; AGY-03/04 retain later insurance/task/finance owners. Phase5 Motor Trade quote capture research/planning is next.

13 phases, 83 requirements, 100% ownership mapping. Phase numbers start at 1 because the sales snapshot belongs to a separate project. Every feature phase includes its own unit tests, persistence and demo seeds.

## Milestones

- [ ] v1.0 Functional Back Office MVP — phases 1–13 (approved, in progress)

## Phases

- [x] **Phase 1: Data and API design** — Translate both references into implementable contracts.
- [x] **Phase 2: Application and persistence foundation** — Run a persistent authenticated system with the prototype shell.
- [x] **Phase 3: Clients and contact servicing** — Maintain insured businesses and person-level servicing information.
- [x] **Phase 4: Agency onboarding and access** — Onboard agencies and enforce their sharing boundaries.
- [ ] **Phase 5: Motor Trade quote capture** — Capture complete persistent Motor Trade risks in the back office.
- [ ] **Phase 6: Underwriting and first policy issue** — Complete the first quote-to-issued-policy journey.
- [ ] **Phase 7: Policy lifecycle and history** — Service issued policies without corrupting history.
- [ ] **Phase 8: Commercial Combined back office** — Demonstrate shared workflows with a different risk structure.
- [ ] **Phase 9: Tasks, documents, communication and incidents** — Make servicing activities and demo handoffs operational.
- [ ] **Phase 10: Accounting and insurer reporting** — Reconcile policy movements through finance workflows.
- [ ] **Phase 11: Configuration and account administration** — Expose controlled configuration and complete account functions.
- [ ] **Phase 12: Dashboards, search and reports** — Provide trustworthy cross-module discovery and reporting.
- [ ] **Phase 13: Complete demo and acceptance** — Verify prototype completeness and deliver a repeatable demonstration.

## Phase details

### Phase 1: Data and API design

**Goal:** Translate both references into implementable contracts.
**Depends on:** None
**Requirements:** DES-01 through DES-06
**Plans:** 4 plans: 01-01 source/field inventory; 01-02 data, schemas and lifecycle; 01-03 APIs/adapters; 01-04 design validation.

**Success criteria:**

1. Reviewers can follow every prototype control through requirement, data record, command, permission and acceptance case.
2. Motor Trade and CC schema examples validate; issue, backdate, renewal, cancellation and rounding examples have expected outcomes.
3. ERD, data dictionary, OpenAPI, lifecycle/permission tables and funnel mapping resolve implementation-critical ambiguities or explicitly block unsupported cases.

**Deliverables and boundaries:** Screen/action inventory; ERD and migrations design; policy JSON schemas; OpenAPI contract; transition and permission matrix; calculation examples; scenario fixtures. This is a dedicated design phase, not a generic discovery placeholder.

### Phase 2: Application and persistence foundation

**Goal:** Run a persistent authenticated system with the prototype shell.
**Depends on:** Phase 1
**Requirements:** FND-01 through FND-06
**Plans:** 6 plans: scaffold; SQL persistence; local identity; prototype shell; durable infrastructure; foundation verification.

**Success criteria:**

1. A new developer can start web/API/SQL Server, migrate and seed the demo with documented commands.
2. Seeded users can sign in/out; unauthorised API access is denied, and saved foundational records survive restart.
3. Shell states, audit, durable jobs and the first unit/SQL integration checks run successfully.

**Deliverables and boundaries:** apps/backoffice; backend API/Domain/Application/Infrastructure projects; migrations; local identity; demo clock/seeds; CI and test setup.

### Phase 3: Clients and contact servicing

**Goal:** Maintain insured businesses and person-level servicing information.
**Depends on:** Phase 2
**Requirements:** CLI-01 through CLI-04
**Plans:** 6 plans: contract refinements/replay; client identity; contacts; support flags; match intake/review; verification.

**Success criteria:**

1. Staff create an account and contacts, reload it, and see one primary contact plus linked activity.
2. Support flags expose only appropriate internal/agency wording and are absent from risk/rating projections.
3. Duplicate review records the chosen decision and prevents unauthorised cross-agency disclosure.

**Deliverables and boundaries:** Client/contact/flag/match vertical slices; list/detail screens; SQL constraints and visibility tests.

### Phase 4: Agency onboarding and access

**Goal:** Onboard agencies and enforce their sharing boundaries.
**Depends on:** Phase 3
**Requirements:** AGY-01 through AGY-05
**Plans:** 8 plans: contracts/data/API; persistent draft wizard; evidence/checks; durable notifications; users/invitations; activation/suspension/terms; trusted scope/sharing; acceptance.

**Success criteria:**

1. An incomplete agency cannot activate; required evidence/products/users allow activation.
2. Invitation, suspension and permission changes persist and affect API access.
3. Internal agency preview reflects actual shared records; another agency's data remains inaccessible.

**Deliverables and boundaries:** Agency workspace/onboarding; user lifecycle; product access and commission terms; demo invitations.

### Phase 5: Motor Trade quote capture

**Goal:** Capture complete persistent Motor Trade risks in the back office.
**Depends on:** Phase 4
**Requirements:** QUO-01 through QUO-06
**Plans:** Not yet planned.

**Success criteria:**

1. Users create and resume both Motor Trade product quotes with drivers, vehicles, premises and cover.
2. Validation and lookup failure/manual-entry paths work after reload; list/search filters use real records.
3. Risk revisions, clones and withdrawal have stable identities/history and invalidate stale downstream results.

**Deliverables and boundaries:** Product seed definitions; draft schemas; back-office capture; lookup adapters; risk validators. No funnel edits. Consume ACCEPTANCE-BACKLOG.md: actual client-to-quote navigation and progressed-quote matching guard are required.

### Phase 6: Underwriting and first policy issue

**Goal:** Complete the first quote-to-issued-policy journey.
**Depends on:** Phase 5
**Requirements:** UWR-01 through UWR-07
**Plans:** Not yet planned.

**Success criteria:**

1. Rating is repeatable for the same risk/rule version and referrals evaluate product dimensions.
2. A servicing user, stale quote or unresolved referral cannot bind; accepted approved terms can bind.
3. Issue commits one policy version, transaction, accounting obligation and durable document work; retry and injected failure tests prove no duplicates or partial issue.

**Deliverables and boundaries:** Rating adapter; authority/referrals/escalation; acceptance; atomic issue; minimal finance posting and document-job contracts used by later phases.

### Phase 7: Policy lifecycle and history

**Goal:** Service issued policies without corrupting history.
**Depends on:** Phase 6
**Requirements:** POL-01 through POL-09
**Plans:** Not yet planned.

**Success criteria:**

1. An adjustment changes only a draft until valid rating/approval/acceptance; stale writes and unauthorised takeovers fail.
2. Effective-date and processing-date queries return the correct immutable version and documents; unsupported dates return clear errors.
3. Renewal creates a linked term and cancellation creates auditable return-premium/refund obligations; lapse and notification work persist.

**Deliverables and boundaries:** MTA workspace; editing leases; version diff/as-at; renewal and cancellation commands; finance obligations. Split detailed plans by MTA, chronology, renewal and cancellation.

### Phase 8: Commercial Combined back office

**Goal:** Demonstrate shared workflows with a different risk structure.
**Depends on:** Phase 7
**Requirements:** CC-01 through CC-05
**Plans:** Not yet planned.

**Success criteria:**

1. Users capture and issue a CC risk with property, liability and business-interruption sections rather than vehicle tabs.
2. Assumed CC rating/authority checks and location aggregation produce reproducible outcomes.
3. Adjustment, renewal, cancellation and incident/document payloads use CC risk items and preserve the same history/finance invariants.

**Deliverables and boundaries:** CC schema/forms and seeded product rules; shared lifecycle reuse; contract tests for future operations integration. No public sales funnel.

### Phase 9: Tasks, documents, communication and incidents

**Goal:** Make servicing activities and demo handoffs operational.
**Depends on:** Phase 8
**Requirements:** OPS-01 through OPS-08
**Plans:** Not yet planned.

**Success criteria:**

1. Tasks, notes and messages persist with correct visibility and reproducible workflow-created tasks.
2. Users generate/download actual documents, upload evidence and retry delivery without changing historical issued files.
3. Motor Trade MID and Motor Trade/CC claims handoffs record outcomes, failures and retry history.

**Deliverables and boundaries:** Task queues; note/thread services; document/file storage and generation; delivery, MID and claims adapters. Separate detailed plans for work management, documents and integrations.

### Phase 10: Accounting and insurer reporting

**Goal:** Reconcile policy movements through finance workflows.
**Depends on:** Phase 9
**Requirements:** FIN-01 through FIN-08
**Plans:** Not yet planned.

**Success criteria:**

1. Statements and ledger balances reconcile with policy issue/MTA/cancellation obligations and allocation movements.
2. Receipts cannot be overallocated; refund authority and duplicate-payment checks hold under retries.
3. Invalid bordereaux cannot submit; corrections, exclusions, CSV export and demo submission persist; closed-period corrections remain auditable.

**Deliverables and boundaries:** Ledger/accounts UI; receipts/allocations/reconciliation; refunds; bordereaux; period close. Posting primitives already delivered in Phase 6.

### Phase 11: Configuration and account administration

**Goal:** Expose controlled configuration and complete account functions.
**Depends on:** Phase 10
**Requirements:** ADM-01 through ADM-08
**Plans:** Not yet planned.

**Success criteria:**

1. Effective-dated product/authority/template/workflow edits affect future commands without rewriting historic decisions or documents.
2. Identity changes use required approvals; local MFA and session revocation actually control sign-in/API access.
3. Administrators can audit mutations and inspect/retry integration work without exposing secrets.

**Deliverables and boundaries:** Admin editors over earlier seeded configuration; account security/profile/session flows; audit and integration health. Separate plans keep identity and insurance configuration independent.

### Phase 12: Dashboards, search and reports

**Goal:** Provide trustworthy cross-module discovery and reporting.
**Depends on:** Phase 11
**Requirements:** RPT-01 through RPT-05
**Plans:** Not yet planned.

**Success criteria:**

1. Global search and dashboard queues link to authorised persisted records with correct counts.
2. Each named report category has meaningful filters, drill-down, export and empty-state behaviour.
3. Computed financial/portfolio/renewal metrics reconcile against source transactions and scoped exports exclude sensitive fields.

**Deliverables and boundaries:** Search projections; dashboard queries; report definitions; exports and saved favourites; measure definitions.

### Phase 13: Complete demo and acceptance

**Goal:** Verify prototype completeness and deliver a repeatable demonstration.
**Depends on:** Phase 12
**Requirements:** ACC-01 through ACC-06
**Plans:** Not yet planned.

**Success criteria:**

1. All inventoried controls have implementation evidence and business scenarios pass for both product families.
2. Restart, stale-edit, rejected permission, failed-job retry and rollback scenarios preserve data consistency.
3. Automated checks pass, key screens receive visual comparison, and setup/demo/module handover documents support another developer.

**Deliverables and boundaries:** Full acceptance traceability; browser scenario suite; visual QA; demo runbook; setup/recovery/module documentation. This phase validates earlier work rather than deferring unit tests.

## Dependency and delivery notes

The conservative sequence keeps review and execution manageable. Phase 6 owns minimal posting primitives and durable document requests so policy issue is consistent before finance/document administration arrives. Phase 8 produces product-appropriate incident/document contracts; Phase 9 completes their user workflows. Configuration is seeded and versioned when its first consumer is built; Phase 11 adds administration screens. Phase 13 cannot compensate for missing phase-level tests.

Phase 1 must refine any newly discovered control into a requirement before implementation. Commercial Combined assumptions are permitted, but must remain documented. Exact task counts and estimates follow phase planning, not guesswork in this milestone roadmap.

## Progress

| Phase | Plans complete | Status | Completed |
|---|---|---|---|
| 1. Data and API design | 4/4 | Complete | 2026-09-13 |
| 2. Application and persistence foundation | 6/6 | Complete | 2026-09-14 |
| 3. Clients and contact servicing | 6/6 | Complete | 2026-09-14 |
| 4. Agency onboarding and access | 8/8 | Complete | 2026-09-15 |
| 5. Motor Trade quote capture | 0/TBD | Not started | — |
| 6. Underwriting and first policy issue | 0/TBD | Not started | — |
| 7. Policy lifecycle and history | 0/TBD | Not started | — |
| 8. Commercial Combined back office | 0/TBD | Not started | — |
| 9. Tasks, documents, communication and incidents | 0/TBD | Not started | — |
| 10. Accounting and insurer reporting | 0/TBD | Not started | — |
| 11. Configuration and account administration | 0/TBD | Not started | — |
| 12. Dashboards, search and reports | 0/TBD | Not started | — |
| 13. Complete demo and acceptance | 0/TBD | Not started | — |


### Phase 3 downstream acceptance obligations

CLI-01 client discovery/contacts/activity is owned by Phase 3, but the requirement remains partial until Phases 5/6 verify navigation to actual linked quotes/policies. Phase 5 connects MatchSubmission to a real quote, implements automatic detection/rating blocks and guards reopening after downstream progression. Phase 9 connects persisted match information requests to the demo communication delivery workflow. Phase 13 checks these obligations before full requirement acceptance; placeholders and Recorded requests do not count as live records or Sent messages.
