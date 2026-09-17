# Servicing contracts — Phase 7 prerequisite

Plan 07-01 supplies strict design contracts and tested pure rules. It does not
implement servicing persistence, public endpoint handlers or an editing screen.
The API catalogue marks added/refined commands `phase-7-pending`. Runtime commands
remain unavailable until their owning plans implement and verify them.

## Formats and capture

`servicing.schema.json` defines closed proposal, change, lease, evidence,
decision, capacity, terms, acceptance, cancellation preview/approval and issue
contracts. The generator imports the existing Motor Trade capture vocabulary;
top-level change target IDs live in the envelope, while nested rows retain their
stable IDs. Empty typed payloads permit incomplete capture, never issue readiness.
Business descriptions are legitimate typed input; arbitrary free-form risk keys
are rejected. Actor, calculated premium, current status and approval identities
cannot enter through proposal writes. Proposal changes are bounded at 100.

The service must additionally enforce the 2 MiB JSON limit, 1,000 total risk
items, current product limits, nonempty UUIDs, same-policy target ownership,
operation eligibility, ordered dates and complete issue readiness. Schema
validity cannot establish those facts. `dateBasis` preserves the source choice;
shared mode prohibits overrides and per-cover mode allows only later cover dates.
The pure schedule builder enforces cover-only overrides and cumulative ordering.

Effective intent preserves London local date/time and optional explicit UTC
offset. The server derives UTC, rejects gaps and requires a choice at folds.
Source mappings previously bound to `effectiveAt` now bind to
`commonEffectiveIntent`; client-entered UTC is no longer the authoritative write.

`issued-servicing.schema.json` is a distinct `issued-servicing-1` immutable
snapshot. It retains the complete issued capture shape but requires servicing
decision/base/revision/transaction lineage. It cannot masquerade as a new-business
quote decision. Existing `issued-quote-1` files, schema and readers are unchanged.
The premium in a complete snapshot represents its rated cover; signed financial
movements remain separate original-component records, not edits to that premium.

## API boundary and ownership

Existing operation IDs remain stable, including `attachDraftEvidence` and
`withdrawDraftEvidence`. The evidence input now references exact `fileVersionId`
and `cycleId` rather than the earlier placeholder `documentVersionId` shape.
New routes cover evidence review/upload, selected referral decisions, capacity,
terms/delivery, persisted cancellation previews/approval and renewal experience.
Uploads use the existing bounded multipart transport, never unlimited base64 or
client storage paths. New schemas have separate OpenAPI names to avoid weakening
quote and issued-policy definitions.

Every command requires current identity/capability/record scope before receipt
replay, CSRF and an operation key. Fresh draft commands also require a strong
draft ETag and the current lease generation/token; lease acquisition has no
pre-existing lease requirement. Creation checks the base term ETag. Child
commands lock their parent draft/cycle and recheck the exact child state under
that serialized boundary; selected decisions additionally supply every child
ETag and apply atomically. All affected successful mutations must advance the
parent ETag. Responses, including errors, are `no-store`.

Current state must reject superseded cycles, evidence and terms even when their
immutable IDs still exist. Persisted acceptance requires exact delivery, terms
and accepted proof. Cancellation issue requires its exact preview/approval pair;
adjustment/renewal issue requires cycle/rating/terms/acceptance. Neither variant
accepts quote provenance or client money. SQL ownership keys, append-only guards,
locking, receipt persistence and transaction boundaries belong to later plans as
specified in `07-RULES-AND-RELATIONSHIPS.md`.

## Pure rules and fictional examples

`ServicingRules` selects issued versions using effective and processing cutoffs,
then transaction/slice ordering; drafts cannot win. Its schedule rejects duplicate
change IDs/typed targets, dates outside the half-open term, changes before an
already-issued future slice, and backdated drivers even with senior authority.
The caller supplies scoped candidates and current authorization under its lock.

Annual differences use contractual local calendar days and anniversary basis,
including leap/short terms. Each component rounds separately, half away from zero.
One fee is added across all slices. Cancellation reverses unearned parts of the
original signed components, including negative adjustments; the caller excludes
retained fees and already-returned components. A resulting credit is not payment.
The worked fixture reconciles 129.14 gross / 118.95 net adjustment and 326.96
cancellation credit. Existing financial examples remain covered by retained tests.

The five `demo-servicing-1` cancellation rules in `servicing-demo.json` are
fictional demo settings, not legal rules. Every reason needs accepted evidence
and a current approval; three require a separate senior approver. Pure date
checks block surviving later issued renewals, earlier-than-latest slices and
insufficient local notice. They do not themselves validate evidence or grants.

Missing/unaccepted renewal experience or a zero denominator produces
`information-required`. The exact decimal ratio above 50% requires senior review;
it never rounds into approval. The later rating implementation applies the pinned
fictional 8% loading, 45-day invitation and 14-day lapse rules. Fair-value evidence
and current agency eligibility remain separate required checks.

## Verification boundary

Source tests retain all 137 original controls, 393 exact-source field occurrences
and 10 conditional branches with explicit owners. They verify source fidelity and
ownership, not working UI behavior. Later owners must produce persisted browser
readback. Unit tests cover pure money/date/experience rules; AJV and OpenAPI tests
cover malformed input, strict unions, provenance and command safeguards. No SQL,
browser, human UAT, hosted CI or delivery/payment outcome is claimed by this plan.
