# Cover MGA Back Office

## What This Is

A functional back office for an MGA, reproducing the supplied Cover prototype's design and operational workflows with Next.js, TypeScript, Tailwind CSS, .NET APIs and SQL Server persistence. Business users must be able to demonstrate complete insurance and servicing journeys using fictional data.

## Core Value

Staff can complete a quote-to-policy-to-servicing journey and trust that its policy versions, decisions, documents and financial movements remain consistent after reload or restart.

## Latest Completed Milestone: v1.1 Prototype Alignment and Funnel Servicing

**Delivered:** Prototype section alignment for Motor Trade/shared screens and existing cx1-implementation quote/MTA funnel capture, locally verified across all 11 phases and 38 requirements. See milestones/v1.1-REQUIREMENTS.md and docs/design/PROTOTYPE-ALIGNMENT-v1.1.md.

**Authority:** User approved the complete proposed scope on 2026-10-05, confirmed cx1-implementation, and excluded CC completely. Advance automatically through planning, implementation and focused verification without routine approval stops. User authorization supersedes older funnel transport/integration exclusions for this explicit host integration; canonical legacy questions/semantics remain unchanged. No production deployment is inferred.

## Latest Milestone: v1.0 Functional Back Office MVP — closed 2026-09-26

**Goal:** Implement all available or implied back-office prototype capabilities in manageable, tested phases, with explicit data and API design before implementation.

**Target features:** Motor Trade Road Risks, Motor Trade Combined and assumed Commercial Combined back-office support; clients and agencies; quotes and underwriting; policy issue, adjustments, renewals and cancellation; tasks, documents, messages and claims handoff; accounting, reporting, configuration and audit.

## Requirements

### Validated

- ✓ Persistent Motor Trade Road Risks, Motor Trade Combined and Commercial Combined capture, underwriting and issue. — v1.0, local evidence.
- ✓ Immutable policy history, adjustments, renewals, cancellation and reconciled signed finance. — v1.0, local evidence.
- ✓ Saved tasks, scoped communications, versioned documents, claims/MID handoffs and durable demo retries. — v1.0, local evidence.
- ✓ Agency/client administration, independent sensitive approvals, local MFA and current authorization. — v1.0, local evidence.
- ✓ Scoped search, live dashboards, eleven reports, saved favourites and safe CSV exports. — v1.0, local evidence.
- ✓ 949-control traceability, accumulated SQL/browser evidence, concise demo and developer handover. — v1.0, local evidence.

- ✓ Source-derived Motor Trade/shared UI, contextual client/agency-before-quote entry and existing source-funnel quote/MTA save/resume. — v1.1, local evidence.
- ✓ Saved quote/MTA/renewal/cancellation issue, immutable replay, atomic finance and reloadable confirmations. — v1.1, local evidence.
- ✓ Completed matching/onboarding, generated PDF/demo delivery, claims/capacity outcomes, two-person refund/payment and real account security journeys. — v1.1, local evidence.

### Active

None. The approved v1.1 scope is locally complete; future scope has not been selected.


### Out of Scope

- Commercial Combined changes, capture, servicing and acceptance are excluded from v1.1 by explicit user instruction. Retain existing CC data and code; do not expand or audit CC.
- `frontend-code/` is a historical snapshot. The authorized live Motor Trade funnel repository is `F:/Scratches/AscendX/cx1-implementation`.
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
| SQL Server relational core + versioned JSON snapshots | Aligns experience, flexible risks and transactional issue | Implemented: relational policy graph, immutable issued JSON and atomic first issue |
| Modular .NET solution in one deployment | Clear replacement boundaries without distributed transaction overhead | Accepted; API/Domain/Application/Infrastructure implemented |
| Focused research before requirements | User selected research | Accepted |
| Focused verification after 26 September | Repeated full SQL gates delayed delivery | Good — retain broad baseline, test changed behaviour |
| Close v1.0 with explicit verification debt | User selected closure after audit | Accepted — no fabricated human or database restart pass |
| Reuse cx1-implementation for Motor Trade quote and MTA | Explicit approved prototype feedback | Implemented — scoped identity, raw state, typed projection and exact fenced retries |
| Align source UI with persistent prerequisites | Prototype actions must perform saved operations | Verified locally — lifecycle, documents, providers, finance and account security |
| Scope capacity correspondence paging to its history | Global database updates caused false conflicts | Verified — unrelated updates tolerated; changed history rejected |

## Evolution

At phase transitions, update validated requirements, decisions, new requirements and changed assumptions. At milestone completion, review scope, delivered evidence and remaining limitations before planning module replacements.

## Autonomous working agreement

On 2026-09-13 the user approved the full requirements and roadmap, and authorised autonomous research, planning, engineering choices and progression to the next step. Choose the best supported option, document assumptions and continue through the approved milestone without routine confirmation. This authorisation supersedes skill prompts for routine scope confirmation, option selection, plan approval and phase transitions within approved scope.

Updated by explicit user direction on 2026-09-26: prioritize delivery and remove repeated runbook barriers. Plan directly from the existing code and requirements; research only a concrete unknown. Use focused tests for changed behavior and one relevant saved-data smoke check. Keep a brief inline completion review. Separate research, plan-check, Nyquist, UI-spec/audit and code-review stages are disabled by default. Do not rerun the entire multi-hour SQL suite at each slice or phase; reserve broad regression for Phase 13 or a concrete cross-cutting regression. Record residual verification work without blocking the next independent phase. Fix actual correctness, authorization and data-loss failures; preserve finance invariants, additive migrations, retained data and deterministic external adapters. Do not claim unperformed tests or human UAT passed. This agreement supersedes older plan/runbook mandates for repeated exhaustive gates. Ask only for consequential missing information or access.

Commit reviewed local work in manageable units. No production deployment, real payment/delivery or separate customer-facing funnel is authorised. Continue in this task; no additional user-owned tasks are needed.

Milestone review: 2026-09-26 — 82/83 requirements locally verified; remaining verification limits explicitly accepted for close.


## Current State

v1.1 is locally complete: 11 phases, 11 plans and 38 approved requirements, following the 13-phase v1.0 baseline. Saved browser and focused SQL/contract checks support the delivered behavior. Hosted connection approval is recorded; deployment/push is not performed. v1.0 is archived and locally tagged after completion of 13 phases and 131 plans. Closed as a local MVP with accepted verification limits following the user’s explicit complete-milestone command after the audit. ACC-02 remains partially verified: application restart passed, dedicated SQL engine restart unperformed. Human business and assistive reviews remain pending. Acceptance of debt does not turn these into test passes.

## Next Milestone Goals

No further milestone has been scoped. Preserve the completed v1.1 implementation and accepted historical debt when planning future work.

## Accepted Debt

- Dedicated SQL engine restart and human business/assistive reviews.
- Older CC PDF continuation layout and intermittent development navigation timeout.
- Current reviewer identity deployment absent from retained old preview; earlier automatic approval rejection remains in force.
- Report limits and legacy summary metadata documented in the archived audit.

---
Last updated: 2026-10-05 after verified v1.1 completion.
