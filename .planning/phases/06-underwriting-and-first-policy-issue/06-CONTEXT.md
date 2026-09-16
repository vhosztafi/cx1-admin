# Phase 6: Underwriting and first policy issue — Context

**Gathered:**2026-09-16
**Status:** Ready for planning

<domain>
## Phase Boundary

Deliver UWR-01..07 for both Motor Trade products: reproducible demo rating, multidimensional authority/referrals, capacity escalation, exact-version quotation/acceptance and atomic first policy issue. Complete inherited policy discovery/client links and safe agency policy summaries, applied underwriting endorsements and actual rating/acceptance invalidation. Preserve all verified Phase1–5 behavior. This is approved MVP scope; user authorised autonomous choices and advancement.
</domain>

<decisions>
## Implementation Decisions

### Current versions and edit lifecycle
- **D-01:** Reuse relational SQL ownership plus immutable versioned JSON. Rating/terms/decisions/acceptance bind quote ID, revision ID, hash, client/relationship, product/configuration and agency terms. A hash alone cannot prove ownership after matching reassociation.
- **D-02:** Use an explicit audited return-to-draft/revise action for unbound progressed quotes. It supersedes current applicability and reopens capture under the same held locks; historical results remain immutable. Relevant capture, term, ownership, evidence or rule changes and re-rating require a fresh applicable result and acceptance. Bound/withdrawn records cannot be reopened as an editable new-business quote.
- **D-03:** Pin actual approved commercial/settlement terms used for each contractual result. Recheck applicability and current agency/product authority before sending, acceptance and bind; changed mandatory terms require explicit refresh/re-rating with visible differences. Never silently rewrite commission or historic output. Research must reconcile existing effective-term rules before designing exact commands.

### Deterministic rating and authority
- **D-04:** Use separately versioned, explicitly fictional Road Risks/Combined pricing rules and deterministic adapters. Persist exact input, components/factors, configured taxes/fees/commission, source rule version, response and expiry. Adopt30-day demo quote validity unless the source imposes a more specific rule; no regulatory or production-rating claim. No live provider calls.
- **D-05:** Evaluate all applicable source authority dimensions separately, including driver/data/evidence, stock/vehicle/cover limits and product/binder restrictions. Insufficient authority cannot be bypassed by a high premium limit or UI capability. Capture structural/term/identity/eligibility failures block a meaningful rating; missing proof must still appear as an explicit evidence/referral blocker and can never disappear because pricing succeeded. Resolve this distinction against current readiness contracts during research.
- **D-06:** Persist approval, conditional approval, query, decline, reopen and capacity escalation with actors, reasons, evidence, current input and rule versions. Conditions changing price/coverage require new terms and acceptance. Carrier responses are typed persisted demo outcomes or an explicitly recorded supplied response, never a fabricated carrier identity. Underwriters cannot approve above their effective limits.

### Quote communication and acceptance
- **D-07:** Sending creates durable deterministic delivery/document work tied to exact terms; queued is distinct from delivered. Acceptance records named accepter, timestamp, channel, evidence and exact revision/rating/terms. Protect receipt replay with current authority. UI must retain drafts and exact uncertain requests.
- **D-08:** Minimal versioned quote/policy document payload and durable request ownership belong here so first issue is consistent. Phase9 owns full generic document generation/download/workspace. An unavailable/generated/queued state must be truthful; do not link fake documents or claim actual messages were sent.

### First issue and dependent discovery
- **D-09:** One SQL transaction commits policy identity/reference, term, immutable issued risk, transaction, balanced journal/financial obligations, audit and unique durable document/delivery work. Retry returns the same issued identity, late failure rolls back everything, and competing bind/capture/matching/revocation commands serialize through current held authority.
- **D-10:** Seed reusable financial posting primitives now using exact pennies and the existing financial examples: agency versus direct debtor, net versus separate commission settlement and fee share. Accounting screens/receipt/refund/bordereau workflows remainPhase10. Preserve original component lineage for future servicing.
- **D-11:** Implement actual policy list/search/filter/sort/page including registration and real client Policies/Overview/Activity navigation. Add own-agency safe policy summaries to the common allowlist; no hidden risk search oracle. Preserve prototype policy header/summary and relevant product details, with explicit later-phase owners for unavailable servicing/operations.
- **D-12:** Keep modular projects and existing UI patterns; sequential bounded vertical plans with source/data/API design before migrations/endpoints, semantic unit tests, fresh realSQL/API regression, both-product browser journeys and restart. Do not change the sales funnel or create a portal/microservices. Human UAT/hostedCI/Docker remain separately recorded.

### Agent discretion
Exact class/table boundaries, command decomposition, reproducible fictional examples and UI adaptations follow source and tests. No routine user confirmation is required within this scope. Keep research and plan review enabled, and record assumptions. No new external provider credentials are needed.
</decisions>

<canonical_refs>
## Canonical References

- `.planning/PROJECT.md`, `.planning/REQUIREMENTS.md`, `.planning/ROADMAP.md`, `.planning/ACCEPTANCE-BACKLOG.md` — approved milestone and inherited obligations.
- `.planning/phases/05-motor-trade-quote-capture/05-VERIFICATION.md`, `.planning/phases/05-motor-trade-quote-capture/05-11-SUMMARY.md`, `.planning/phases/05-motor-trade-quote-capture/05-PHASE06-HANDOFF.md` — verified prerequisites/current revision and closure boundary.
- `docs/prototype/Cover MGA Back Office-4.html`, `docs/design/source/prototype-template.txt`, `docs/design/control-inventory.json`, `docs/design/api-control-map.json`, `docs/design/PROTOTYPE-BEHAVIOUR.md` — source controls, business behavior and design.
- `docs/design/DATA-MODEL.md`, `docs/design/API-CONVENTIONS.md`, `docs/design/LIFECYCLE.md`, `docs/design/PERMISSIONS.md`, `docs/design/ADAPTERS.md`, `docs/design/FINANCIAL-EXAMPLES.md` — relational/versioned model, states, authority, adapters and balanced accounting examples.
- `contracts/openapi.json`, `contracts/schemas/policy.schema.json`, `contracts/schemas/policy-draft.schema.json`, `contracts/examples/motor-trade-road-risks.json`, `contracts/examples/motor-trade-combined.json` — existing contract gate; design contracts are not proof of runtime implementation.
- `docs/design/funnel-field-mapping.json`, `frontend-code/src/domain/` — read-only Motor Trade reference where needed; do not modify.
</canonical_refs>

<code_context>
## Existing Code Insights

- `backend/src/BackOffice.Infrastructure/Quotes/QuoteScope.cs`, `QuoteService.cs`, `QuoteLifecycleService.cs`, `QuoteMatching.cs` — held authority, atomic revision/audit/receipt and closed-capture behavior to extend.
- `backend/src/BackOffice.Application/Quotes/QuoteLifecycleRules.cs` — MatchesCurrent includes quote/revision/hash; must augment result ownership/terms/config/evidence context.
- `backend/src/BackOffice.Infrastructure/Persistence/QuoteRecords.cs`, `QuoteModel.cs` — immutable revision and current pointer; no runtime rating/policy/referral records exist yet.
- Existing SqlCommandBoundary, identity snapshots, agency approval/effective terms, OutboxWork/lease/inbox patterns and QuoteLookupWorker support durable scoped operations.
- `apps/backoffice/components/quotes/quote-receipt.tsx`, `quote-history.tsx`, `quote-wizard.tsx` — source-aligned tabs, saved read-only values, exact retries/stale recovery. Extend record rail and policy pages; retain314px/390px contracts.
- AgencySharingQuotes and client linked quote projections provide the closest safe-discovery pattern for actual policies.
</code_context>

<specifics>
## Demo and acceptance

Preserved demo has complete capture257Combined/258RoadRisks, but future tests should create additive owned fixtures. Include successful direct-authority issue, independent missing-evidence/stock referral, carrier query/condition/decline, stale edit/terms/acceptance, revoked actor, duplicate bind, injected rollback and restart. Never count intercepted presentation fixtures as persisted business outcomes.
</specifics>

<deferred>
## Deferred owners

Policy adjustment/renewal/cancellationPhase7; Commercial Combined runtimePhase8; generic tasks/documents/messages/incidentsPhase9; finance workspacesPhase10; admin editors/MFAPhase11; global reportingPhase12; human/full milestone acceptancePhase13. These do not defer the minimal atomic posting/document requests required for first issue.
</deferred>
