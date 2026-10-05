---
phase: 19-motor-trade-mta-funnel-and-review
plan: 01
status: complete
requirements_completed: [POL-10, POL-11, POL-12, POL-13, POL-14]
---

The saved Motor Trade adjustment launches the existing fourteen-stage cx1-implementation funnel with issued base, requested-by, client/agency and effective date context. Source answers and typed changes save atomically under the current actor, strong ETag, idempotency key and lease fence. Nullable raw state is retained per servicing revision. Source adjustment dates update MTA effective intent while the bound policy term remains fixed. Editing leases can be renewed without resetting unsaved source answers.

Changes, Review & rate and Documents match the prototype workspace. The record header presents saved policy, base, requested-by, date and change count. Saved comparisons group policyholder, drivers, vehicles, business, premises, cover and other risk declarations. A closed policy-owned risk-details change supports Motor Trade declarations without replacing independently owned records. Normal mutation invalidates rating and dependent proof; canonical save clears superseded raw state. Pending commands retain exact body, ETag, key and fence through uncertain responses and subsequent denial.

Validation: both app type checks, focused parent lint, four MTA adapter tests, twelve existing projection tests, eight source proposer tests and fifty backend servicing projection tests pass. The additive local browser journey issued fictional quote 7fce86f8-9520-4ffd-b124-d20e8bbd84bf through normal lookup/manual decision, rating, reviewed actual proof, delivery, acceptance and issue APIs. Policy c3e7d7d9-ae02-4ad0-9d96-bf9838cb564d has a balanced opening journal. Draft 2f021d56-c7c9-4121-9ed8-b59cf9fa3a24 passed source prefill, lease renewal, atomic save, unchanged-prefill neutrality, lost committed save/denial/exact retry, reload, return and three sections while its issued policy version stayed unchanged.

The additive migration was applied only to the isolated fixture. Hosted connection is explicitly approved; deployment has not occurred. Status: implemented with phase-24 follow-up for supplemental prototype-only MTA declarations, complete MTA rating/acceptance/issue and multi-date atomic finance verification. POL-13/POL-14 remain open until that integration evidence is complete.


## Phase 24 verification closure — 2026-10-05

Source-captured MTA supplemental declaration saved under lease while preserving raw resume state and unchanged issued base. Actual current rating/proof/acceptance and UI issue exact replay created one issued version and balanced finance. Two Motor Trade SQL issue scenarios additionally verify two dated versions, rollback boundaries, concurrency, authority and replay.

The earlier follow-up is now closed by recorded local engineering evidence. Historical limitations above describe the earlier verification point. See docs/design/PROTOTYPE-ALIGNMENT-v1.1.md and 24-VERIFICATION.md for exact evidence and boundaries; no human UAT or hosted deployment is claimed.
