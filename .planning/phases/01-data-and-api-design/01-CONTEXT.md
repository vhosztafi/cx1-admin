# Phase 1: Data and API design — Context

Gathered: 2026-09-13
Status: Ready for planning
Mode: Autonomous, user-approved engineering discretion; routine discussion skipped.

<domain>
Translate the prototype and read-only Motor Trade funnel reference into concrete schemas, API contracts, lifecycle/permission rules and acceptance examples. Covers DES-01 through DES-06. No changes to frontend-code.
</domain>

<decisions>
- SQL Server relational ownership plus immutable product-specific policy JSON snapshots.
- Separate policy continuity, terms, drafts, insurance transactions and issued versions.
- Stable risk-item IDs; explicit temporal semantics; out-of-sequence rebasing excluded.
- .NET API is the business/security authority; generated TypeScript contracts serve a Next.js/Tailwind frontend.
- Persistent deterministic adapters; no real external submissions or money movement.
- CC internal back-office capture and servicing supported using documented assumptions.
- Internal agency sharing preview only; no separate broker portal application.
- Exact commands, errors, constraints, rounding, seeds and state transitions must be specified and validated; generic architecture prose is insufficient.
</decisions>

<code_context>
Prototype is a bundled HTML with JSON-encoded template; SOURCE-INVENTORY.md identifies its methods. Funnel domain/types, converter and terminal expose raw-versus-converted distinctions. Follow canonical document links for field mapping. Root contains planning only.
Environment preflight found .NET SDK 10.0.401, Node, pnpm, Docker client and sqlcmd. Docker daemon was unavailable during a sandboxed check; resolve SQL Server execution in Phase 2 without reducing persistence requirements. This does not block design.
</code_context>

<specifics>
The five-change MTA, driver missing licence, stock-authority referral, CC location exposure, cancellation/refund, unmatched receipt and bordereau validation are mandatory worked examples. Prototype dates and mock counts must be reconciled into consistent fixtures.
</specifics>

<deferred>
Entra, real integrations, sales-funnel connection, public portals and production underwriting validation.
</deferred>
