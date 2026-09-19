# Phase 8: Commercial Combined back office — Context

**Gathered:** 2026-09-19
**Status:** Ready for research and planning
**Mode:** Automatic; decisions selected under the user's approved autonomous working agreement. No new user answers are claimed.

<domain>
## Phase Boundary

Implement distinct internal Commercial Combined quote capture, rating/referral/authority, first issue, adjustment, renewal, cancellation and historical policy views using the verified shared lifecycle. Capture property locations, construction/protections, flood/subsidence answers, sums insured, business interruption, liabilities, wage rolls, health/safety and losses. Demonstrate reproducible location/MEL/postcode aggregation. Define CC incident/document payloads for shared Phase9 operations. No public CC sales funnel or changes to frontend-code.

CC-01..04 must become functional and persistent here. CC-05's product-specific payload and integration contract is in scope; generic incident logging, rendering and delivery remain the already-approved Phase9 owner. Keep that compound requirement partial until its complete operational journey exists.
</domain>

<decisions>
## Implementation Decisions

### Capture and product boundaries
- **D-01:** Use the existing internal New quote flow and shared client/agency/quote identities. Enable CC only when its published product, agency terms and complete capture pipeline are implemented. Preserve all Motor Trade behavior and snapshots.
- **D-02:** Follow the prototype's ten CC stages: Agency & product; Proposer & history; Claims & losses; Locations & occupancy; Construction & protections; Flood & subsidence; Sums insured & BI; Liability & wage roll; Health & safety; Cover & declarations. Allow incomplete saved drafts, with explicit readiness and underwriting blockers. Do not require drivers, vehicles, NCD or Motor Trade-only proof for CC.
- **D-03:** Maintain stable IDs for locations, wage categories and loss records; explicit add/remove/clear semantics, saved before/after comparison and exact per-location proof subjects. Preserve unknown versus zero/No. Share the issued envelope while using distinct closed CC capture/risk schemas and versioned option/question catalogues.

### Product-specific underwriting
- **D-04:** Implement source CC readiness and referral branches, including flood, movement/subsidence, panels/timber/listing, unoccupancy, waste/recycling, heat/height/hazardous work, territory/products, health/safety, insolvency/declined cover and losses. Keep evidence, internal decision, carrier permission and separate acceptance independent. Do not broaden Motor Trade evaluators through permissive fall-through.
- **D-05:** Use reproducible published demo rating/authority parameters with exact pounds/pennies and explicit configuration pins. Include property, BI and liability/wage dimensions, positive/zero/negative servicing movement and one fee. Clearly label demo assumptions; no production underwriting or legal certification is claimed.
- **D-06:** Treat postcode-district aggregation as an actual whole-book underwriting constraint. Use normalized UK outward district, same binder/product scope, dated issued exposure and proposed replacement of the current policy contribution. Drafts and cancelled/expired slices must not silently inflate exposure; early renewals and future changes must not release or double-count current exposure. Concurrent issues/changes must recheck authoritative capacity atomically; UI filters or caller-visible policy subsets cannot understate book exposure. Display only authorized aggregate information, without leaking foreign policy identities.
- **D-07:** Start from prototype dimensions and thresholds (single-location sum insured, largest-location exposure as demo MEL proxy, district sum insured/headroom, liability limits), then document validated formulas and configurable demo thresholds during research. Distinguish any supplied loss estimate from the conservative proxy; never imply a real flood model or probabilistic maximum-loss model. Do not copy fixed sample counts or headroom into runtime truth.

### Servicing and policy record
- **D-08:** Reuse current-scope-before-replay, exact ETags, leases, hashed prerequisites, immutable version JSON, temporal selection, atomic balanced journals and durable outbox behavior. Add CC-specific typed adjustment editors and correct expiring-base renewal capture. Do not duplicate the policy/term/transaction model or introduce a second database.
- **D-09:** Retain the prototype shell, density and colors, replacing Drivers/Vehicles with Property schedule, Liability & employees and Business interruption. Show saved property, location aggregation, wage/liability, BI, loss and authority values on desktop and390px layouts. Links must reach real scoped records or expose a justified unavailable state. No Motor Trade-specific labels, registration/MID actions or validation in CC flows.
- **D-10:** Define versioned CC property/liability incident subjects and product-appropriate document content from the exact immutable source version. Existing terms proof/delivery remains functional; generic documents/incidents stay Phase9. Pending requests must not be labeled generated, logged or sent. Cash settlement/reconciliation remains Phase10.

### Demonstration and acceptance
- **D-11:** Add deterministic fictional CC examples through normal services and missing-only configuration. Include two-location exposure, refer/decline/conditional cases, postcode headroom/contention, complete issue and servicing, and interrupted/retried commands. Preserve all retained Motor Trade data, user-edited configuration, immutable history and persistent keys; no database reset or real provider communication.
- **D-12:** Plan manageable sequential feature slices with concrete data/API/UI ownership and meaningful RED/GREEN unit/SQL negatives. Require contract, current-scope, cross-product/foreign-item, concurrency, timing, exact-money and actual browser tests; a full current-source backend gate, retained Motor Trade regression and actual restart/preservation close the phase. Human UAT/hostedCI/Docker limitations remain explicit.

### Agent discretion
The user delegated research, planning, implementation choices and progression. Choose feature-local strategy boundaries, schema versions, migrations, exact endpoints and plan count from actual code/source evidence. Keep research, UI design, plan/security checks and runtime verification enabled. Execution remains sequential inline unless the user changes the existing parallelization preference.
</decisions>

<canonical_refs>
## Canonical References

- `.planning/PROJECT.md`, `.planning/REQUIREMENTS.md` (CC-01..05), `.planning/ROADMAP.md` Phase8 — approved scope and autonomy.
- `.planning/phases/07-policy-lifecycle-and-history/07-PHASE08-HANDOFF.md`, `.planning/phases/07-policy-lifecycle-and-history/07-VERIFICATION.md` — verified shared lifecycle and product-specific seed limitations.
- `docs/prototype/Cover MGA Back Office-4.html`, `docs/design/source/prototype-template.txt`, `docs/design/source/prototype-render-data.json` — source CC capture, rules, policy tabs and implied actions. Preserve occurrence-level coverage, not just labels.
- `docs/design/DATA-MODEL.md`, `docs/design/LIFECYCLE.md`, `docs/design/SERVICING-CONTRACTS.md`, `docs/design/UNDERWRITING-CONTRACTS.md`, `docs/design/API-CONVENTIONS.md`, `docs/design/PERMISSIONS.md`, `docs/design/FINANCIAL-EXAMPLES.md` — accepted invariants and boundaries.
- `contracts/schemas/policy.schema.json`, `contracts/schemas/quote-draft.schema.json`, `contracts/schemas/issued-policy.schema.json`, `contracts/schemas/servicing.schema.json`, `contracts/schemas/issued-servicing.schema.json`, `contracts/examples/commercial-combined.json` — existing schema envelope and illustrative CC assumptions, not proof of implemented capture.
- `docs/SETUP.md`, `docs/DEMO.md` — native SQL/demo and additive evidence procedures.
- `frontend-code/` — read-only Motor Trade reference only; it does not specify CC behavior.
</canonical_refs>

<code_context>
## Existing Code Insights

QuoteRules and several quote validators currently explicitly support only the two Motor Trade products. The existing CC policy JSON example is not a live capture/issue pipeline. Servicing workspace imports typed Motor Trade editors, and source policy tabs are product-specific. Extend deliberate product dispatch rather than weakening existing validators.

PolicyScope resolves current quote/agency/client ownership before access. Shared quote/servicing command boundaries hold current authority before exact receipt replay. Existing lifecycle services, evidence/referral/capacity/terms workers, exact-money routines and durable notification patterns are reusable after product-specific assumptions are removed safely. All application API/data changes require generated closed contracts and preserved historic responses.

Product seeds for servicing terms, policy templates and renewal settings currently select Motor Trade codes. Add separately published CC configurations and fixtures without replacing existing versions. Shared-book district exposure needs explicit temporal/concurrency design before rate/issue integration.
</code_context>

<specifics>
## Specific Ideas

Use fictional light engineering/wholesale premises with two districts, then a second risk in the same district to demonstrate actual changing aggregation and stale-capacity denial. Reproduce prototype CC tabs and all conditional capture branches; sample company names and amounts are examples, not constants.
</specifics>

<deferred>
## Deferred Ideas

Generic document rendering, incident/task/communication workflows belong to Phase9; reconciled cash balances belong to Phase10; configuration administration screens belong to Phase11. No new scope was introduced. Phase8 must provide concrete CC contracts and preserve those later obligations.
</deferred>
