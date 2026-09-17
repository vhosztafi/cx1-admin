# Servicing contracts — Phase 7 prerequisite

Plan07-01 supplies strict contracts and pure rules. Plan07-03 adds persistent
drafts, revision history, editing leases and their scoped HTTP routes. Those
operations are marked `phase-7-03-implemented`; later rating, evidence, terms and
issue operations remain `phase-7-pending`.

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

## Persistent drafts and leases (07-03)

ServicingDraft retains immutable policy/term/base-version ownership through a
compound foreign key. ServicingRevision is append-only with same-draft current
revision, unique sequence, exact UTF-8 SHA256 and base-version validation. Only
one live adjustment and one live renewal may exist per base term; cancellation
drafts may coexist. Abandonment is final and preserves saved revisions.

Internal servicing, underwriter and senior-underwriter roles may write drafts.
Only underwriter/senior-underwriter may take over with a10..2000-character reason.
System administration and agency identity do not bypass these capabilities.
Current identity/roles and same-policy ownership are locked before receipt lookup.
Writes lock policy, base term, draft and lease in that order after the existing
agency/identity/source-quote/relationship scope; they never reopen the source quote.

Five-minute leases use a fresh GUID fencing token and increasing generation on
every acquisition/takeover. Both current authenticated holder and current token
are required; a token never authenticates its bearer. Renewal/release cannot
affect a replacement lease. Every lease change advances the draft ETag.

GET /terms/{termId}/drafts supplies the term ETag for creation. Draft reads and
all draft/lease commands return the complete saved draft plus its strong ETag,
including nullable lease state, so callers do not guess a parent version after
changing a lease. Creation Location points to /api/v1/drafts/{draftId}. All reads
and writes are no-store. Proposal writes are closed, duplicate-safe and bounded
at2MiB/100 changes/1000 aggregate array items. The shared HTTP reader retains its
existing1MiB default for quotes. Reason/date capture remains distinct from
readiness, rating or issue.

Stable targets use issued driver/vehicle/premises IDs, cover section IDs, policy
ID for singleton business/whole cover, and client ID for the policyholder.
Foreign update/remove targets and duplicate addition identities fail. Full
nested dependency validation, product-specific editors, common/cover date
assessment and comparison belong to07-04. No servicing policy transaction,
payment, document delivery, renewal invitation or cancellation issue is claimed
by the draft/lease implementation.

## Typed proposal backend checkpoint (07-04, editors still outstanding)

PUT /drafts/{draftId}/proposal now projects typed changes against the exact
immutable base under the held policy/term/draft scope. Conflicting target changes
at one instant, foreign targets, duplicate/nested identity ownership changes and
unresolved driver/vehicle/premises references fail atomically. Incomplete typed
declarations remain saveable; capture readiness is not permission to progress.
Object patches retain omitted fields; arrays explicitly replace their contents.
An update may explicitly specify `payloadMode: "replace"` to replace the whole
typed target, preserving its envelope-owned ID while clearing omitted fields.
Replacement is forbidden on add/remove and still rejects system-owned fields,
foreign targets, nested identity movement and dangling references. Use this
mode for explicit field clearing or deselecting a cover section; omission from
a normal partial patch never means deletion.

GET /drafts/{draftId}/editor is an additive, current-scope, no-store read with the
strong draft ETag and saved revision ID. It returns typed base/proposed capture,
stable-ID differences, cumulative dated slices and readiness issues. Derived
issued premium/sections/driver-basis and account ownership fields cannot become
editable capture. The client ID is supplied separately as the policyholder
target; writes cannot transfer that ownership. Reordering identity-keyed rows is
not a material difference. Editor output never establishes rating applicability.

London gap/fold resolution, term boundaries, latest issued effective slice,
common-date ordering, cover-only date mode and current senior backdate authority
are assessed on the server. Driver backdating remains blocked for every role.
Incomplete or invalid dates are retained as draft intent with blockers. Every
cumulative slice is checked, so later correction cannot hide an earlier invalid
slice. Servicing vehicle capture uses manual validation rather than borrowing
lookup authority from the bound quote. Evidence, rating and issue gates remain
with their owning plans.

The typed dialogs, explicit clearing/dependency UX, frontend comparison/date
tests and actual desktop/mobile editor journeys are not yet implemented or
verified. This backend checkpoint does not complete 07-04 or Phase 7.

### Specified vehicle declarations in servicing

Vehicle changes optionally carry a closed `specifiedVehicle` object with both
`selected` and `required` booleans. Selection affects only the change's stable
vehicle ID; required is the shared specified-vehicle requirement. Omission
preserves prior declarations. Removal cannot select its removed vehicle, and
removing a selected vehicle requires explicit deselection. Keeping the requirement
with no selected vehicles produces an incomplete-capture readiness issue.
Conflicting requirement values across vehicle changes are rejected. Restating
membership preserves its order and produces no spurious material difference.
The local form projection follows these rules; the API remains authoritative.
