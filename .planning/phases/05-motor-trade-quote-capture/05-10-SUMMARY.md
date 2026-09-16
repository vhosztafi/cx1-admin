---
phase: 05-motor-trade-quote-capture
plan: '10'
status: complete
completed: 2026-09-16
requirements_completed: []
requirements_supported: [QUO-01, QUO-06]
---

# 05-10 — quote discovery, matching and safe sharing

Commit `1491934` implements persisted quote search/filter/sort/page, actual client Quotes/Overview/Activity links and optional intake-based creation. Search includes current registrations without duplicate rows. Protected cursors bind actor, route, filters, order and SQL database version; any versioned edit conservatively requires a refresh.

Quote revisions now retain immutable client/relationship ownership. The migration transactionally backfills unchanged pre-matching ownership and restores append-only protection; new revision inserts validate ownership. Matching compares selected stored client identity, pins the rule/signals, and attaches only the same agency/client relationship. Held current authority precedes receipt replay. Fresh decisions require the quote ETag, invalidate it on every review mutation and reject closed captures. Reassociation appends a trusted ownership revision atomically with audit/decision; history/comparison retains the earlier identity. Clone independently assesses destination identity without transferring source process state.

Agency context and internal sharing use the same smaller quote projection. Search/count/cursors only concern visible own-agency values; risk registrations, proposal declarations and evidence remain absent. Policy availability remains explicit and unavailable until Phase6.

## Verification

- Fresh `.local/phase5-integration-final-20260916`: **676 passing = 566 unit + 110 integration;83 real SQL;0 skips**. Integration10m8s; result gate676/83 passed. Earlier failed full run is not accepted evidence.
- SQL migration backfill, same-agency FK/uniqueness, immutable historical ownership, exact concurrent receipt replay, late rollback, stale versions, reassociation/closure race and authority revocation covered. Accepted agency-cookie API tests prove own-only summaries, cursor/filter binding and no hidden registration search oracle.
- Focused matching2 and discovery/API3 cases passed before the final full suite. Tests are split into QuoteMatchStorageTests, QuoteMatchDecisionTests, QuoteMatchAttachmentTests and QuoteDiscoveryApiTests instead of the planned monolithic QuoteIntegrationTests file.
- **80 frontend tests**,294 contract/design checks/341operations, final lint/types and production build passed. Logs: `.local/phase5-discovery-final-webtests.log`, `phase5-discovery-final-contracts.log`, `phase5-discovery-accepted-lint.log`, `phase5-discovery-accepted-types.log`, `phase5-discovery-accessible-webbuild.log`.
- Chrome `.local/phase5-discovery-browser4.log` passes: Road Risks **QT-MT-0000000148 revision4**, Combined149. Client entry, lost-response exact create retry, automatic review, separate/reopen/link, stale quote rejection, current registration search, product/order/paging, client tabs/activity, safe agency summary and restricted-role denial. Screenshots inspected in `.local/browser-evidence/quote-integration`;390px containment and314px matching rail passed.
- Initial browser failure led to explicit accessible filter labels. Subsequent navigation errors were harness scoping/race errors corrected to Client sections. Regression assertions were updated for actual quote links/sharing, and the agency-lock observer now recognises the new held-lock statement without weakening its concurrency assertions.
- EF model synchronization passed. Demo migration preserved existing fictional histories; no reset. Owned API16296/web30644 stopped after command-line verification. Diff check passed; frontend-code unchanged.

## Next

05-11 consolidated acceptance remains required. Readiness deliberately retains the temporary global blocker pending full composition; incomplete legacy client matching assessment must be handled explicitly before removing it. Run all quote and20prior agency journeys, verify persisted restart, reconcile source coverage and measured CI minima. No final QUO signoff, rating, issue or policy-discovery claim.
