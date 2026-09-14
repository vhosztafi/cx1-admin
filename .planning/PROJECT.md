# Cover MGA Back Office

## What This Is

A functional back office for an MGA, reproducing the supplied Cover prototype's design and operational workflows with Next.js, TypeScript, Tailwind CSS, .NET APIs and SQL Server persistence. Business users must be able to demonstrate complete insurance and servicing journeys using fictional data.

## Core Value

Staff can complete a quote-to-policy-to-servicing journey and trust that its policy versions, decisions, documents and financial movements remain consistent after reload or restart.

## Current Milestone: v1.0 Functional Back Office MVP

**Goal:** Implement all available or implied back-office prototype capabilities in manageable, tested phases, with explicit data and API design before implementation.

**Target features:** Motor Trade Road Risks, Motor Trade Combined and assumed Commercial Combined back-office support; clients and agencies; quotes and underwriting; policy issue, adjustments, renewals and cancellation; tasks, documents, messages and claims handoff; accounting, reporting, configuration and audit.

## Requirements

### Validated

Phase 1 design and Phase 2 native foundation are verified locally. See DES-01..06 and FND-01..06 in REQUIREMENTS.md and their phase verification reports. The prototype and copied sales funnel remain reference material; full business MVP delivery is still in progress.

### Active

- [ ] Preserve the prototype's UI and implement its actual and implied actions with persistence.
- [ ] Design product-specific policy JSON, relational records, lifecycle rules and API contracts before coding.
- [ ] Supply deterministic, persistent demo integrations and fictional scenario data.
- [ ] Enforce permissions, workflow prerequisites and concurrent-edit checks in the API.
- [ ] Include unit tests in every implementation phase and integration tests for persistence invariants.
- [ ] Keep components and projects independently replaceable during later improvements.

### Out of Scope

- Changes to or live integration with `frontend-code/`; use only for questions, field semantics and data gathering.
- New customer-facing sales funnels, including Commercial Combined.
- A separate broker portal application. The prototype explicitly describes `pPortal()` as an internal data-sharing reference.
- Real rating, money movement, email delivery, MID submissions, claims-provider connections or production external lookups; use recorded demo adapters.
- Azure Entra integration in v1; preserve a provider-independent identity boundary.
- Full claims adjudication or settlement; prototype scope is incident logging, handoff and administrator summaries.
- Production underwriting/compliance certification; prototype values and new assumptions remain labelled demo rules.

## Context

- Source: `docs/prototype/Cover MGA Back Office-4.html`, a bundled HTML template with embedded application logic. Inspected by parsing its template without executing embedded scripts.
- Sales reference: ignored `frontend-code/`, including canonical funnel documentation, `src/domain/types.ts`, `converter.ts`, `terminal.ts` and development samples. Its raw form and converted submission shapes differ, and unresolved legacy clarifications must not become invented production facts.
- The root has no previous `.planning` milestone. Begin numbering at 1 independently of the copied funnel.
- User confirmed 2026-09-13: both products may be considered in back office; assumptions allowed for Commercial Combined; persistent demo adapters preferred; funnel stays unchanged. User selected focused architecture research.
- Prototype portal boundary was clarified by source inspection, resolving the earlier tentative portal scope question.

## Constraints

- **Stack:** Next.js/TypeScript/Tailwind frontend, .NET API, SQL Server with policy JSON; no document database required initially.
- **Design:** Prototype layout, density, colours, navigation and product-specific tabs are authoritative. Catalyst is optional, not a prerequisite.
- **Delivery:** Vertical feature phases include UI, API, persistence, seeds and tests. Initial design and foundation phases precede them.
- **Identity:** Simple local authentication initially, plus functional prototype account controls; no pretend security toggles.
- **Scope fidelity:** A toast-only prototype action becomes a real demo workflow. Any discovered omission is added to requirements rather than silently dropped.

## Key Decisions

| Decision | Rationale | Outcome |
|---|---|---|
| v1.0, phase numbering starts at 1 | First back-office milestone | Accepted scope |
| Preserve sales snapshot unchanged | Explicit user instruction | Accepted |
| Motor Trade and Commercial Combined back office | User permits CC assumptions | Accepted; assumptions tracked |
| Persistent deterministic external adapters | User's preferred demo approach | Accepted |
| Internal agency visibility reference only | Explicit `pPortal()` source boundary | Source-derived |
| SQL Server relational core + versioned JSON snapshots | Aligns experience, flexible risks and transactional issue | Accepted; foundation implemented, policy snapshots follow in feature phases |
| Modular .NET solution in one deployment | Clear replacement boundaries without distributed transaction overhead | Accepted; API/Domain/Application/Infrastructure implemented |
| Focused research before requirements | User selected research | Accepted |

## Evolution

At phase transitions, update validated requirements, decisions, new requirements and changed assumptions. At milestone completion, review scope, delivered evidence and remaining limitations before planning module replacements.

## Autonomous working agreement

On 2026-09-13 the user approved the full requirements and roadmap, and authorised autonomous research, planning, engineering choices and progression to the next step. Choose the best supported option, document assumptions and continue through the approved milestone without routine confirmation. This authorisation supersedes skill prompts for routine scope confirmation, option selection, plan approval and phase transitions within approved scope.

Keep research, plan checks, unit tests, integration checks and verification enabled. Fix failures rather than accepting missing functionality to advance. Do not mark unperformed human UAT as passed; record it separately while completing available automated/browser verification. Ask only when missing information/access or a consequential scope change cannot be resolved within the user's instructions. Keep the sales funnel unchanged and external services simulated.

Commit reviewed local work in manageable units. No production deployment, real payment/delivery or separate customer-facing funnel is authorised. Continue in this task; no additional user-owned tasks are needed.

Last updated: 2026-09-13 — full requirements/roadmap approved; autonomous progression authorised.


Phase 2 completed 2026-09-14: native SQL persistence, local authenticated shell and durable diagnostic operations verified. FND-01 through FND-06 complete; see phase verification for scope. Next: Phase 3 client/contact servicing. Business workflows, human UAT and hosted deployment remain pending.
