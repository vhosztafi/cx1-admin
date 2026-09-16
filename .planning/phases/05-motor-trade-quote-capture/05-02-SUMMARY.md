---
phase: 05-motor-trade-quote-capture
plan: '02'
status: complete
completed: 2026-09-16
requirements_completed: []
requirements_supported: [QUO-02, QUO-06]
---

# 05-02 summary — persistent quote aggregate and capture APIs

Implemented SQL quote headers, immutable revisions and activity, current registration projections and composite ownership/current-pointer constraints. Scoped services hold current stored authority before replay, recheck capture eligibility, retain explicit product/question/reference/agency-term pins, serialize competing writes, reject stale edits and preserve no-op revision semantics. Quote records, safe business audit and ID-only receipts commit atomically; injected late failures verify rollback.

Strict bounded JSON parsing, closed capture schemas, typed question/reference identity, child ownership, exact canonical JSON/hash behavior and London policy-term assessment back the API. Current quote reads materialize names, capture availability and term assessment under held authority. Product selection reads the current explicit capture catalogue for the selected relationship; malformed/missing configuration fails rather than inventing offers.

Five authenticated endpoints are implemented: POST /api/v1/quotes, GET /api/v1/quotes/{quoteId}, PUT /api/v1/quotes/{quoteId}/proposal, GET /api/v1/quotes/{quoteId}/readiness and GET /api/v1/quote-products. Mutations use quote-capture; reads use quote-read. Requests enforce CSRF, strong ETags and command keys; confidential responses are no-store. Current views do not serialize internal persistence entities, history reasons or raw rowversion bytes.

## Scope clarification and retained gates

Readiness is explicitly partial and fail-closed: structural, local-term and current-capture issues are returned, with a server-owned quote-assessment-unavailable blocker. Semantic capture sections are implemented in05-03..06, evidence in05-08 and matching in05-10. Their real assessments must be composed before removing the blocker. This prevents a prerequisite cycle while preserving the requirement for full readiness before Phase6 progression. No QUO requirement is accepted by this plan alone.

Clone/withdraw/evidence capabilities remain false until their handlers exist; optional match association returns409 without creating a quote. Revision/history/discovery/sharing remain dependent plans. Before later reassociation, retain original client/relationship ownership per revision and include changed ownership in the contractual fingerprint. The sales funnel is unchanged.

## Fictional demonstration

A Development-only --seed-quote-demo command, guarded to CoverMGA_Demo, imports a dedicated clearly labelled fictional pre-approved agency/client/relationship/terms context. It does not claim to execute agency activation. Eight quotes are then created through QuoteService: an incomplete and three captured variants per Motor Trade product. Stable command keys preserve IDs and user edits on repeat, and suspended/revoked contexts are not repaired.

Native no-reset initialization/seed created8quotes; repeated initialization/seed created0, retaining all8IDs. SQL confirmed4quotes/current revisions per product and0missing current pointers. Original AG-DEMO-01/02 remain drafts. Startup instructions and limitations are in docs/DEMO.md.

## Verification

Fresh full476backend tests passed:383unit+93integration, including67realSQL,0skips. Contracts291passed (949controls/336operations); frontend30passed. Exact evidence is in05-VALIDATION.md and05-02-PROGRESS.md; final results are .local/phase5-quote-demo-final, .local/phase5-quote-demo-final.log, .local/phase5-quote-demo-contracts.log and .local/phase5-quote-demo-web.log. Native result logs are .local/phase5-quote-demo-native-first.json and .local/phase5-quote-demo-native-repeat.json.

Inline review found no blocking issue for this plan; see05-02-REVIEW.md. No browser or human UAT completion is claimed for the backend slice. Native initialization was additive, not a reset.

Continue05-03 autonomously: prototype-aligned Motor Trade quote creation/resume/business/term wizard, real persistence, bounded uncertainty handling and actual browser verification. Read05-03-PLAN.md,05-UI-SPEC.md,05-PATTERNS.md and source mappings before UI work.
