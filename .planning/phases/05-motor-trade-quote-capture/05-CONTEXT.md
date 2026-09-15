# Phase 5: Motor Trade quote capture — context

Gathered 2026-09-15 from the approved roadmap, user instructions, prior phase evidence and read-only references. Autonomous research/planning is authorised; no new product-preference confirmation is needed.

## Boundary

Implement QUO-01 through QUO-06 for Motor Trade Road Risks and Motor Trade Combined in the back office. Capture incomplete drafts, resume them, validate complete risks, preserve stable child identities and immutable revisions, run deterministic lookup/manual-entry paths, discover actual quotes and revise/clone/withdraw with reasons. Commercial Combined capture remains Phase8. Policies in QUO-01 require Phase6 and cannot be falsely marked complete here. Actual rating/referrals/acceptance/bind remain Phase6, but their revision/terms invalidation boundaries must be designed now.

## Locked decisions

- Keep Next.js/TypeScript/Tailwind, .NET and native SQL Server2022. Store versioned risk JSON with relational ownership, references and searchable indexes. No document database migration is justified by this phase.
- The supplied HTML prototype owns back-office design. The sales snapshot is read-only field/rule evidence, not an API specification or a UI to modify. Both Motor Trade products must work; use documented demo assumptions for missing Combined facts.
- Existing canonical policy/draft schemas and the255-occurrence funnel mapping own the starting shape. Do not silently copy permissive RawForm into APIs, coerce typed reference values, guess a person's split name or lose nested histories/conditional explanation fields.
- Current agency/relationship/product eligibility and identity authority must be checked on new commands and before replay. Agency suspension must fence quote creation/revisions against the same agency locks already used by matching and client relationships.
- Preserve incomplete answers, explicit negative answers and applicability separately. Strict validation rejects unknown sensitive/system-owned fields even for incomplete drafts. All references and question sets have pinned version/provenance.
- No fake successful lookups, rating or issued policy IDs. Lookup outcomes and selected/manual values remain distinct. Demo data and failed attempts survive reload/restart without resets.
- User-facing errors retain drafts; uncertain writes retain exact payload/key/version. Follow the existing resource-specific ETag and ID-only receipt patterns. Unit, real SQL/API, contract and browser tests remain mandatory.
- Implementation stays in manageable feature slices and existing solution projects. No new microservices, separate broker portal or real external calls.

## Dependencies that must close

Consume `.planning/ACCEPTANCE-BACKLOG.md`: real client quote navigation; persisted MatchSubmission-to-Quote association; duplicate-review reopen/reassociation fence after downstream progression; agency safe quote sharing; distribution eligibility versus actual product/rating readiness. Preserve existing client/contact/support/match regression journeys and agency access tests.

## Agent discretion and initial choices

Use an immutable QuoteRevision plus a mutable Quote current pointer/rowversion, typed owned search projection and stable UUID risk items. Capture edits can save incomplete valid-shaped documents; readiness is a separate result. New revisions require a reason where the source requires one. Clone retains provenance but resets operational/evidence/rating/acceptance state and does not widen relationship access. Determine exact item-ID remapping and reference dependencies before implementation. Decide public endpoint boundaries only after the source-operation audit and data/API design pass.

## Canonical references

- `.planning/PROJECT.md`, `.planning/REQUIREMENTS.md`, `.planning/ROADMAP.md`, `.planning/ACCEPTANCE-BACKLOG.md`
- `docs/prototype/Cover MGA Back Office-4.html`, `docs/design/control-inventory.json`, `docs/design/api-control-map.json`
- `docs/design/FUNNEL-MAPPING.md`, `docs/design/funnel-field-mapping.json`, read-only `frontend-code/src/contracts/questions.ts` and `frontend-code/src/domain/`
- `contracts/schemas/policy.schema.json`, `contracts/schemas/policy-draft.schema.json`, `contracts/examples/motor-trade-road-risks.json`, `contracts/examples/motor-trade-combined.json`
- `docs/design/DATA-MODEL.md`, `docs/design/API-CONVENTIONS.md`, `docs/design/PERMISSIONS.md`, `docs/design/LIFECYCLE.md`, `docs/design/ADAPTERS.md`
- `.planning/phases/04-agency-onboarding-and-access/04-VERIFICATION.md`, `04-REVIEW.md`, `04-UI-SPEC.md` and current identity/agency/party services

## Completion gate

Before executable plans: reconcile every relevant prototype control and mapped risk-field occurrence to a path, validation rule, UI section, API owner and test. Write the data/API design, UI contract, closest-code patterns, validation strategy and bounded plans; run plan review and fix gaps. Do not substitute a generic JSON editor for the source's actual capture surfaces.
