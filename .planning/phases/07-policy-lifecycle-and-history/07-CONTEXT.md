# Phase 7: Policy lifecycle and history — Context

**Gathered:** 2026-09-17
**Status:** Ready for planning
**Mode:** Single-pass autonomous discussion under the user's approved working agreement.
No new answers or business approval are attributed to the user.

<domain>
## Phase boundary

Service issued Motor Trade Road Risks and Combined policies without changing
historical facts. Deliver adjustment drafts/leases, rating/referrals/acceptance,
atomic multi-date issue, effective/processing chronology and comparison, renewal
and lapse, cancellation and financial/document lineage. Requirements POL-01..09;
POL-01 remains compound across later generic operational/finance modules. Current
Phase6 baseline:838backend/169realSQL,104frontend,334contract/source/gate tests,
all37retained journeys,three-policy restart and44-set additive preservation.
</domain>

<decisions>
## Decisions

### Scope and draft ownership
- **D-01:** Both Motor Trade products must work; Commercial Combined retainsPhase8.
  Preserve the prototype's pPolicy,pMta,pRenewal,pCancelReview,pIssued,pAsAt,
  driver/vehicle and modal behaviors.137original Phase7 controls are a starting
  inventory, not a complete source audit; include conditional branches and fields.
- **D-02:** Persist an editable servicing draft/revision against an explicit owned
  policy,term and immutable base version. Saving cannot alter issued cover. Use
  five-minute renewable leases plus strong ETags; takeover requires current
  permission and reason. Loss/expiry/revocation makes the editor read-only and
  preserves local edits for explicit compare/reload, rather than discarding work
  merely because the prototype toast says it did. Never silently rebase.

### Chronology and affected risk
- **D-03:** Implement approved LIFE-01 fully: shared effective date plus later
  cover-section schedule dates, cumulative immutable slices in one transaction,
  one fee and one financial posting boundary. Only cover changes may have later
  overrides; driver changes cannot be backdated. Reject out-of-term and before-
  latest-issued-slice changes, including conflicts with a future-effective slice.
  Ordinary permitted backdating requires current senior authority and reason.
- **D-04:** Preserve London local intent, reject nonexistent/ambiguous times unless
  explicitly disambiguated, and use half-open intervals. Effective and processing
  cutoffs select issued versions only. Future adjustments and early renewal issue
  must not replace today's cover, registration search or active term. A current
  pointer is not enough to answer an as-at question. Past cancellation returns
  cancelled cover; future cancellation remains scheduled until its effective time.

### Underwriting, proof and money
- **D-05:** Bind rating/referrals/proof/delivery/acceptance to exact draft revision,
  base,stable item identities,complete dated schedule,configuration and terms.
  Relevant edits invalidate applicability while retaining history. Reuse existing
  pure rating/authority/money primitives; do not reopen the bound source quote or
  copy its acceptance. Research must define servicing subject ownership explicitly
  before expanding quote-specific tables/services. Review all source input options.
- **D-06:** Use FINANCIAL-EXAMPLES exact decimal/component rules, preserving original
  premium/tax/commission/fee coverage intervals and settlement parties. MTA charges
  annualised differences over remaining contractual local days; cancellation
  reverses unearned posted components once, including prior adjustments. One fee
  per multi-slice transaction. Retained fees are not charged again. Return premium
  creates a credit/refund obligation, not a completed cash payment. Closed periods
  preserve original movements and use a supported open posting period.

### Renewal and cancellation
- **D-07:** Renewal starts from the snapshot applicable at the expiring term end,
  not the arbitrary latest version. New term starts exactly at the prior exclusive
  end,uses local-calendar anniversary rules and configured shorter terms,does not
  overlap,and pins effective product/binder/commercial terms. Actual recorded
  renewal experience can drive source UW-31/loss-ratio checks; absent incident data
  must not become the prototype's invented64%/claim counts. Research a typed,
  auditable supplied-experience path where the future claims module is unavailable.
- **D-08:** Invitation due defaults45days before expiry; auto-lapse defaults14local
  days after expiry,each from versioned demo configuration and the configured clock.
  Invited means actual applied demo delivery. Record exact acceptance separately;
  do not auto-bind on sending. Late acceptance must not manufacture uninterrupted
  cover: v1 blocks unsupported late renewal issue and exposes explicit lapse/new-
  business handling. Lapse is audited and queues a persistent demo notification.
- **D-09:** Cancellation has an editable proposal and current preview before issue;
  reason,effective date,notice evidence and authority must be valid. Use versioned
  fictional reason/notice rules,clearly identified as demo assumptions,not claims
  about legal notice. Required approval is explicit/current and cannot be bypassed
  by generic administration. Atomic issue writes cancellation chronology,financial
  credit/refund obligation,audit and durable notice/certificate/MID intents,and
  abandons conflicting drafts. Generic task closure must be integrated whenPhase9
  task storage exists; no invented closed task counts. Reinstatement remains
  unsupported as already approved. A payment failure cannot reverse cancellation.

### Policy record, contracts and verification
- **D-10:** Existing issued-quote-1 bytes/hash remain readable and immutable.
  Introduce versioned servicing contracts and explicit adapters as needed; never
  reinterpret old JSON as the unrelated future-servicing schema. Show actual
  versions/transactions,linked source,financial obligations and document requests.
  Generic tasks/notes/messages/claims/download generation and collection views
  retainPhase9/10; POL-01 is not completed wholesale by placeholder tabs.
- **D-11:** Extend current scoped API/CSRF/idempotency/ETag/no-store and exact-retry
  UI patterns. Reauthorize current identity/agency/grants before receipt replay.
  Demonstrate races between issue,cancellation,leases,base changes and revocation.
  Every compound FK and immutable SQL guard must evolve through an additive
  migration with old graph/hash preservation; never disable guards to pass a test.
- **D-12:** Keep bounded sequential vertical plans,with source/data/API/UI design
  and meaningful pure tests before handlers. Retain all37 earlier browser journeys,
  Phase6 carrier/issue/discovery behavior,fresh full realSQL result gates,actual
  restart and44-set preservation (extend sets when adding servicing records).
  Human UAT,hostedCI andDocker remain distinct unperformed evidence. No live
  providers/messages/payments,deployment,sales-funnel edits or separate portal.

### Agent discretion

Choose maintainable module/table boundaries,strict command shapes,seeded fictional
scenarios and prototype adaptations after source/architecture research. User
approval of autonomous research/planning/implementation persists. Resolve ordinary
choices without questions; document assumptions and do not waive correctness gates.
No pending todo files were present to fold in. Consume the explicit Phase6 handoff.
</decisions>

<canonical_refs>
## Canonical references

- `.planning/PROJECT.md`,`.planning/REQUIREMENTS.md`,`.planning/ROADMAP.md`,
  `.planning/ACCEPTANCE-BACKLOG.md` — approved scope,compound requirements and limits.
- `.planning/phases/06-underwriting-and-first-policy-issue/06-VERIFICATION.md`,
  `.planning/phases/06-underwriting-and-first-policy-issue/06-14-SUMMARY.md`,
  `.planning/phases/06-underwriting-and-first-policy-issue/06-PHASE07-HANDOFF.md` —
  verified prerequisite,immutable graph/current-scope boundary and retained gates.
- `docs/prototype/Cover MGA Back Office-4.html`,
  `docs/design/source/prototype-template.txt`,`docs/design/control-inventory.json`,
  `docs/design/api-control-map.json`,`docs/design/PROTOTYPE-BEHAVIOUR.md` — source UI.
- `docs/design/LIFECYCLE.md` (LIFE-01,drafts,renewal,cancellation),
  `docs/design/FINANCIAL-EXAMPLES.md`,`docs/design/DATA-MODEL.md`,
  `docs/design/API-CONVENTIONS.md`,`docs/design/PERMISSIONS.md`,
  `docs/design/ADAPTERS.md` — authoritative design contracts and worked arithmetic.
- `contracts/openapi.json`,`contracts/schemas/issued-policy.schema.json`,
  `contracts/schemas/policy.schema.json`,`contracts/schemas/policy-draft.schema.json`,
  `scripts/design-rules.mjs`,`tests/policy-contracts.test.mjs` — structural/executable
  design evidence,not proof that future endpoints already run.
- `docs/design/funnel-field-mapping.json`,`frontend-code/src/domain/` — read-only
  Motor Trade reference; do not change the sales funnel.
</canonical_refs>

<code_context>
## Existing code and integration points

- `backend/src/BackOffice.Infrastructure/Persistence/PolicyRecords.cs` and
  `PolicyModel.cs` enforce same-owner policy/term/version/transaction links,unique
  first issue,append-only history and currently new-business-only constraints.
- `backend/src/BackOffice.Infrastructure/Policies/` owns first issue/read/discovery;
  `Underwriting/` owns pinned authority,rating,proof,capacity and exact terms.
  Extract reusable calculations without giving servicing a fake bound-quote cycle.
- `backend/src/BackOffice.Infrastructure/Platform/` supplies audited SQL commands,
  durable outbox/inbox/leases and posting; identity/agency locks precede mutations.
- `apps/backoffice/components/policies/` and `components/underwriting/` provide
  record/tabs/314px rail,exact frozen commands and current-state recovery patterns.
- Phase6 discovery currently uses latest term/version pointers; temporal servicing
  requires deliberate read-model refinement before issuing later/future records.
- `scripts/verify-underwriting-suite.mjs`,`verify-underwriting-restart-browser.mjs`,
  `verify-underwriting-preservation.ps1` and `assert-test-results.ps1` are the
  acceptance baseline. Native SQL full regression now takes tens of minutes.
</code_context>

<deferred>
Commercial CombinedPhase8; generic tasks/documents/messages/incidentsPhase9;
collections/refund payment/bordereaux/accounting screensPhase10; administrative
configuration/MFAPhase11; dashboards/global searchPhase12; full human/demo
acceptancePhase13. These do not defer Phase7's atomic financial and durable
notification/request intents. Unsupported out-of-sequence rebasing and
reinstatement remain explicit v1 exclusions.
</deferred>
