# Phase 7 implementation decomposition

Prepared 2026-09-17. Planning input only; these are not executable PLAN files.
Execute sequentially after the source, contract, validation and plan checks pass.
Each implementation plan must provide current scope/ETag/idempotency tests and
the UI/API/storage portion of its named capability; shared foundations are
explicit prerequisites, not a claim that later journeys already work.

| Plan | Capability and concrete boundary | Requirements | Decisions |
| --- | --- | --- | --- |
| 07-01 | Strict servicing schemas, source fixtures, full operation catalogue and pure chronology/schedule/financial rules. Resolve rule catalogue and exact data invariants before migrations. | POL-02,04,05,06,07,08,09 | D-01,03,04,05,06,07,08,09,10,11 |
| 07-02 | Temporal policy read/discovery: E/K selector, scheduled/active/cancelled/expired states, term/version lineage, applicable registrations and safe agency projection; old first-issue reader retained. | POL-01,05,06 | D-04,10,11 |
| 07-03 | Persist servicing drafts/revisions and five-minute leases; API create/save/acquire/renew/release/takeover/abandon plus minimal resume workspace and actual conflict recovery. | POL-02,03,06 | D-02,03,11 |
| 07-04 | Full typed change picker/editors for all eight source categories, stable-ID diff, common and cover-only dates, conditional inputs, requested-by and retained unsaved changes. | POL-01,02,03,06 | D-01,02,03,04 |
| 07-05 | Servicing rating cycles and deterministic worker, complete cumulative-slice input, current rule/binder/terms pins, expiry/invalidation and rate/review UI. | POL-04,06 | D-03,05,06,11 |
| 07-06 | Servicing evidence, validation requirements, referrals and current-authority decisions; item-specific proof, conditions and histories with UI. | POL-04 | D-05,11 |
| 07-07 | Servicing capacity requests/responses/conditions and correspondence, withdrawal/reopen/assignment, late-worker fences and actual provider demo outcomes. | POL-04 | D-05,11 |
| 07-08 | Exact servicing terms, reviewed signed proof, persistent demo delivery and separate acceptance, immutable history and UI recovery. | POL-04,07 | D-05,08,11 |
| 07-09 | Signed financial movements, multiple component intervals, minimal locked accounting periods, immutable source lineage and sign-aware sealed posting guards. | POL-04,09 | D-06,10,11 |
| 07-10 | Atomic multi-slice MTA issue with graph/provenance, finance, document/MID intents, policy readback and receipt. Include future-date and same-policy race tests. | POL-04,05,06 | D-03,04,05,06,10,11 |
| 07-11 | Renewal preparation from term-end snapshot, typed supplied experience and UW-31, real fair-value/account checks, term/calendar/config pins and review/rating workspace. | POL-07 | D-07,08 |
| 07-12 | Renewal invitation/acceptance/atomic linked-term issue, configured-clock due/overdue/manual and automatic lapse with persisted demo notifications. | POL-07,08 | D-04,05,07,08,11 |
| 07-13 | Cancellation proposal/reason/notice catalogue, component return preview, eligible separate approval and editable two-step review UI. | POL-09 | D-06,09,11 |
| 07-14 | Atomic cancellation graph/credit/refund obligation, conflicting-draft abandonment, durable notice/certificate/MID intents and scheduled/effective readback. | POL-09,05,06 | D-04,06,09,10,11 |
| 07-15 | Complete policy/product/vehicle/driver record and chronology comparison, as-at reconstruction request and policy-to-new-quote cloning. Carry explicit Phase9/10 module owners. | POL-01,02,05 | D-01,04,10,11 |
| 07-16 | Demo scenarios, complete source/requirement review, retained browser suites, both-product servicing journeys, fresh full SQL gate, restart, additive preservation and Phase8 handoff. | POL-01,02,03,04,05,06,07,08,09 | D-01,02,03,04,05,06,07,08,09,10,11,12 |

All plans depend on the preceding plan for this sequential run. Plan writing may
split an oversized plan without dropping scope; update source ownership and
validation IDs together. Do not mark POL-01 wholly complete while Phase9/10
generic linked modules remain outstanding. Context mentions a full source audit;
137 original controls are the baseline, not permission to omit conditional issue,
non-driver editor fields, rating/documents displays or clone behavior.

## Contract decisions to finalize in 07-01

- Versioned cancellation catalogue includes all five source reasons. Select
  fictional notice requirements and current approval dimensions; label demo
  assumptions. Separate cancellation authority from eventual refund payment
  authority. Approval pins exact preview and forbids required self-approval.
- Define missing/zero-denominator renewal experience as a referral/blocking
  assessment outcome, never a fabricated loss ratio. Supply the real evidence
  association and configured fair-value assessment provenance.
- Enumerate servicing capacity/referral/evidence/terms operations missing from
  the current 29-operation path inventory, strict request/response DTOs and
  permissions. Source prototype stage navigation never grants authority.
- Finalize nullable provenance one-of constraints and SQL trigger replacement
  branches. Existing inner joins must not bypass validation for servicing rows.
- Preserve published first-issue schema/hash bytes and old compound FKs; define
  exact servicing snapshot discriminator, registration paths and document kinds.
- Define cancellation of future slices and already-issued future renewal terms
  explicitly. V1 must block unsupported combinations with clear reasons before
  accepting cancellation, rather than leave a later term silently active.
- Complete source actions/fields with owning plan IDs and negative fixtures.
  Required source follow-ups are implementation tasks, not indefinite research.

## Verification cadence

Pure/domain and contract tests give quick feedback for each logic change.
Targeted real-SQL tests exercise each storage/command boundary before its commit;
UI plans also use actual persisted browser readback. Run the complete retained
backend and frontend suites at final integration, and sooner if shared changes
leave an unresolved regression concern. Do not repeat a 37-minute full SQL suite
for documentation-only work. No test result from Phase6 proves Phase7 behavior.

Final acceptance increases the 838/169 baseline by actual new tests and validates
fresh TRX results with the recorded run start. Extend the 44-set preservation
inventory for all new servicing records. Keep exact old policy JSON/hash and
sealed journal comparisons across upgrade and restart. Human UAT, hosted CI and
Docker runtime remain separately unperformed until actually run.
