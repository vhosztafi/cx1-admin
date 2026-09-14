# Cross-phase acceptance obligations

These are approved milestone dependencies, not waived functionality. Owning phases must consume them in their context/plans; Phase 13 audits closure.

| Obligation | Owner | Required evidence |
|---|---|---|
| CLI-01 actual quote links | Phase 5, checked again13 | Client Quotes/Overview/Activity navigate to scoped persisted quote IDs; search/count/list projections derive from real records. Replace explicit unavailable state only after API/browser verification. |
| CLI-01 actual policy links | Phase 6, checked again13 | Client Policies/Overview/Activity open real issued policy records without synthetic IDs or unsupported counts. Mark CLI-01 complete only after quote and policy evidence both pass. |
| Intake-to-quote linkage and progressed-quote guard | Phase 5, checked again6/13 | Real FK from saved intake to quote; atomic creation/replay and scoped candidate linkage. MatchService must reject reopen/reassociation after the downstream quote progresses beyond its allowed draft. Tests race progression against reopen and preserve existing evidence/trails. |
| Information request delivery | Phase 9 | Recorded MatchInformationRequest is not Sent. Add persistent deterministic delivery job/status with stable identity, retry/recovery and truthful projection; preserve original request/trail. No real message sends. |
| Agency lifecycle and trusted scope | Phase 4 | Revisit shared PartyScope and capability boundaries before enabling any external identity. Current implementation has only internal accounts and an audited safe preview, not a broker portal. Suspensions/permission changes must affect future reads/writes/replay. |
| Business and assistive-technology acceptance | Phase 13 | Execute complete demo and obtain actual human UAT when available; never infer acceptance from unit/browser/agent visual review. Preserve open observations if unavailable. |

Phase 3 minor refinement observations: record-specific read-error wording and dense mobile support history; see 03-UI-REVIEW.md. They are separate from the mandatory functional dependencies above.
