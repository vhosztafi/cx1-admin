# Phase 8 implementation handoff

Phase7 completed on2026-09-19. Its final summary and verification record all
required local gates passing. Commercial Combined assumptions are authorised by the user;
the Motor Trade sales funnel remains a read-only reference.

## Reuse the persisted lifecycle

The shared policy graph separates policy ownership, terms, transactions and
immutable version JSON. Effective-time and known-at selection must continue to
resolve the exact retained version rather than the latest issued pointer.
Adjustment and renewal draft revisions, editing leases, rating cycles, proof,
referral/capacity decisions, terms and acceptance retain subject-specific keys.
Cancellation uses its reviewed preview and approval, with a signed return
premium and balanced posting. Manual or clock-driven lapse records an event at
expiry without manufacturing a policy term or shortening existing cover.

Primary implementation entry points under `backend/src/BackOffice.Infrastructure/Policies`:

- `PolicyScope`, `PolicyReadService`, `PolicyTemporalSelector` and
  `PolicyHistoryService`: authorised policy reads and immutable chronology.
- `ServicingDraftService`, `ServicingRatingScope`, `ServicingRatingService` and
  `ServicingIssueService`: exact saved proposal, configuration pins, authority
  checks and atomic adjustment/renewal issue.
- `ServicingEvidenceService`, `ServicingReferralService` and
  `ServicingCapacityService`: current-purpose proof and separate internal/carrier
  decisions. Quote evidence approval is not servicing approval.
- `RenewalPreparationService` and `RenewalLifecycleService`: supplied experience,
  configuration-driven term dates, invitation checks and lapse.
- `CancellationIssueService` and `CancellationIssueWriter`: independently
  authorised cancellation and retained credit provenance.
- `PolicyCloneService`: fresh quote/risk identities; no copied rating,
  acceptance, provider authority or private client flags.

Keep current identity/scope checks before idempotency replay, command ETags,
holder-bound leases, immutable input hashes and compound foreign keys. Preserve
signed exact-money components, one fee per transaction, journal balance and
original settlement provenance. Extend through new migrations; do not rewrite
old migrations or remove downgrade guards to make tests pass.

## Add Commercial Combined deliberately

Motor Trade Combined is not Commercial Combined. Create distinct product schema,
forms, stable location/risk identities, property/liability/business-interruption
sections and reproducible location aggregation. Do not expose vehicle forms or
route CC captures through the Motor Trade schema just because the lifecycle
tables are shared. Inspect capture, readiness, rating, referral, issued snapshot,
document and incident contracts together before planning the implementation.

Product-specific seeds currently limit policy templates, servicing terms, renewal
preparation and lifecycle settings to the two Motor Trade products. Add explicit
CC versions and assumptions; do not broaden those seeds without corresponding
product rules, authority limits, templates and tests. Normal initialisation must
remain additive and preserve operator-modified settings and issued history.

Phase 8 must demonstrate CC issue, adjustment, renewal and cancellation with its
own risk structure. Include cross-product denial, stable-item ownership, exact
configuration pins, negative/zero/positive money, location aggregation, restart
readback and immutable-history checks. Existing Motor Trade regression remains
required. Demo provider decisions are deterministic persisted outcomes, not
real carrier permissions.

## Downstream boundaries

Phase 9 owns generic tasks, messages, document rendering/delivery and MID/incident
handoffs. Phase 7 saves durable version-bound requests and actual reviewed
supporting files; queued requests are not generated PDFs or sent messages.
Phase 10 owns settlement/allocation/refund workflows and reconciled cash balances.
Issue or cancellation posts a charge/credit and never claims cash collection or
payment. POL-01 remains compound across these later phases.

Prototype coverage currently assigns 104 of 137 original controls to Phase 7,
32 to Phase 9 and one to Phase 10. Preserve the explicit owners in
`07-SOURCE-INVENTORY.json` and the 393 field occurrences/10 branches in
`07-SOURCE-FIELDS.json`; static mapping alone is not runtime verification.

## Local acceptance and retained examples

Use `docs/SETUP.md` and `docs/DEMO.md`. API and CLI processes must share the same
persistent data-protection directory. Build Next with its actual API origin;
the rewrite is fixed at build time. Do not overwrite retained keys, reset the
demo database or edit `frontend-code/`.

The final servicing suite creates fresh fictional policy bases for each run,
then carries their identities through the complete sequence. Existing issued
renewals cannot be issued again. Seed journals retain the original command key,
body and version after uncertain responses; keep them with the demo database.
Whole-database fingerprints and explicit-version API readback verify additive
initialisation and actual process restart separately from intentional seed writes.

Final acceptance evidence is recorded in07-16-SUMMARY and07-VERIFICATION; it
passed. Human business/assistive-technology UAT, hosted CI and Docker execution
remain unperformed unless a later record explicitly supplies that evidence.
