---
phase: 09
status: in-progress
reviewed_commit: ae51811
---

# Operational source audit — working findings

This is a source comparison in progress, not a passed plan17 or phase verification. The findings below retain their original discovery context; current status is recorded here and in the checkpoint. Plan16 is complete. Commits4608c61/d827548 resolve saved claims fields/context, linked task records, inherited navigation, the underwriting view and personal task measures. Focused tests and23retained entry checks have passed. Query correspondence is now committed d811fc6 and passed2SQL/browser cases (18checks per product), plus23retained entry/390px checks. No implementation gap remains identified by this review; final current regression and evidence collection remain pending. The full549-case regression53019 was deliberately interrupted early when that gap was found; it is not an acceptance result.

Subsequent current-build regression92140 found an additional inherited gap: the selected issued version's document-request table had been removed while history still linked to it. That run was interrupted and is not passing evidence. A shared request-history panel is now restored for both products, with exact request/version assertions in commercial issue and Motor Trade history browsers. Focused verification is pending; this supersedes the earlier no-gap assessment above.

The ledger now contains individual sourceReview entries for109Phase9 controls,509global display occurrences,33commercial occurrences and10branches. Commercial occurrence indices belong to a different original inventory and are matched by exact raw source object to global indices, not by coincidental index equality.09-17-DISPLAY-REVIEW documents the reviewed field bindings and explicit semantic changes. Runtime acceptance statuses remain pending until final current reports pass.

## Preserved scope

The original ledger remains unchanged:62direct controls,34explicit inherited controls,118selected control identities,509display occurrences,33Commercial Combined claims occurrences and10supplemental branches.109selected controls belong toPhase9; one remainsPhase11 and eight remainPhase12. Original source methods, paths, handlers and raw occurrence indices must remain intact. Fixture values are illustrative; runtime data must come from saved authorized records.

| Raw display family | Count | Implementation inspected | Audit disposition |
| --- | ---: | --- | --- |
| Policy |117|RecordTasks, RecordDocuments, RecordCommunications, RecordIncidents, PolicyRiskHistory, policy chronology|Task lists and direct Claims/Notes/Messages links now have retained evidence. Claims handler/context gaps below remain. Current document/communication/incident refresh pending.|
| Quote |33|RecordTasks, RecordDocuments, Notes|Actual source-quote task lists and document/note histories exist; match each state/file occurrence to current evidence.|
| Task queues |120|TaskList, TaskSummaryEndpoints, TaskDiscoveryEndpoints|Personal/team/creator/completed queues, filters, paged saved rows, atomic bulk actions and scoped KPIs. Initial all-accessible KPI interpretation was insufficient; d827548 now displays personal totals and team completions. SQL RED/GREEN and19current task browser checks passed. Prototype numeric fixtures are not copied.|
| Task detail |47|TaskDetail, TaskPresentation, WorkflowTaskProvenance, TaskAttachments|Persistent controls, source-change state, checklist/comments/events and files exist. Related agency/insured/policy context is incomplete. Generic source state must not claim delegated authority.|
| Task modal |6|TaskCreate, TaskAssignmentField, TaskCommand|Saved parent identity, type/title/priority/due and currently eligible assignment; existing lost-response/conflict/focus regressions.|
| Incident entry |65|IncidentEditor, IncidentFields, IncidentEvidence, historical resolver|Draft/log/handoff controls and closed product fields exist; historical policy context/direct link needs completion. Unknown/approximate facts remain distinct from fabricated certainty.|
| Incident detail/administrator |84|IncidentFacts, ClaimsSummaryPanel, ClaimsSummaryService|Immutable submitted facts, attempts, administrator contact and paid/reserved/status snapshots exist. Provider summary detail fields and explanatory boundaries need completion.|
| Commercial policy |37|Shared task/document/note/message components plus separate commercial incident fields|33claims occurrences are retained separately. No motor fallback; product-specific property/location/occupation input. Exact summary/excess display still needs the shared fix.|

## Blocking source findings

1. **Related task context.** CTL-abcd385e28c9 andCTL-e2d56c8dcbb7; display2223,2233,2234. TaskPresentation exposes only the primary task subject. Dynamic-navigation.json proves Agency→agency and Insured→policy. Add currently authorized related agency/client/policy projections and direct links, preserving the main servicing draft. A client-only link does not replace the policy destination. Prepared regression: OperationalTaskContextAcceptanceTests (not yet run).
2. **Claims summary loss.** Display4119,4122–4124,4131,4136,4141,4146. ClaimsAdministratorSummary discards liability, incurred, recovery, applied excess and movement notes. Nine initial unit RED cases confirm field loss/missing validation; revised legacy-byte regression prepared. Add optional bounded provider fields, money validation, append-only SQL/API/read-only display and actual deterministic demo evidence. Older absent values stay Not advised. Null new fields must not alter old serialized provider events, inbox hashes or replay behavior.
3. **Incident historical context.** CTL-026efb151c2a and display4055–4062. Add actual policy/insured and selected historical source links/cover context. Unresolved, ambiguous or uncovered occurrences must remain explicit. Reading a version is not a claims coverage or liability decision.
4. **Driver chase destination.** CTL-d5646e53396b. Original driver banner opens policy Messages. The new issued/proposed driver task caller needs that context-preserving navigation; no message is sent by opening it.
5. **Administrator identity and boundaries.** Display722/730/738 and743–746,4040–4054,4105–4115. Show the actual saved handoff administrator and explain local logging versus administrator handling. No fabricated contact route, one-day SLA, weekly delivery or local settlement capability. Broader loss-ratio/renewal analytics4147–4150 need explicit real data and roadmap ownership; do not mark fabricated ratios as implemented.

## Verified inherited fixes so far

Commitf5fc1a8 added scoped MT/CC policy/quote tasks and preserved CC Notes/Messages deep links. Commita2eaf0c added Tasks/Claims deep links, client policy claims selection, heading/rail incident entry, driver risk-item task filtering and issued/proposed driver task navigation, plus the MT Vehicles/MID rail shortcut.15retained navigation checks passed.local/phase9-16-navigation-final;1proposed/issued-driver SQL/browser case passed.local/phase9-16-proposed-driver-green. Mobile/focus images inspected. These are bounded proofs, not acceptance of an entire source tab.

The policy rail controlCTL-60ba3b194aa0 resolves toVehicles in the captured original handler (prototype line1236). Its old operation mapping saysgetPolicyAsAt; the real handler controls the navigation audit. The implemented rail shortcut opensVehicles and its actual saved MID submissions.

Plan16's retained scenarios preserve original references, historical hashes, file bytes, keys and failed attempts. The retry demo compresses only two jobs' next-attempt schedule; it does not prove real-duration backoff. Two complete initializations preserved74856business rows/175tables/167files exactly. Plan18 must still run its full current regression and restart gate.

## Completion rules

After fixes, map each source control/display/branch to concrete implementation and current report references in09-SOURCE-INVENTORY.json; never bulk-mark a whole tab from one interaction or shrink the denominator. Run affected browser/SQL checks sequentially and collect the current aggregate. Preserve older failed evidence. Human business and assistive-technology UAT remain unperformed; Phase13 owns that milestone gate.

Update19:29UTC:4608c61 resolves blocking findings1–5 above except the specifically separate underwriting view4147–4150. Newsummaryfields/identity, taskrelatedlinks, occurrencecontext, driverchase andentryshortcuts all have focusedSQL/browser proof. See09-16-SUMMARY for exact mixed-failure/rerun evidence;20retainedentrychecks and3detailbrowserSQLpassed, claims/drivercollectorscurrent. Plan16 complete;17active. Broad sourceledger status has not been bulkchanged. Underwriting view still requires its concrete saved-source implementation, then exactper-control/display/branch mappings and fullcurrentverification.

Update21:46UTC: Phase4 pPortal open-items handoff remained a placeholder. Implemented explicit delivered-message tracking/closure and current delivered unaccepted quotation/servicing terms. Supplemental raw display occurrences3806–3818 are added independently to the ledger; original509display and all control denominators remain unchanged. Scoped SQL and both MT servicing checks pass; commercial/browser/migration-preservation acceptance remains in progress. See09-17-AGENCY-OPEN-ITEMS and09-16-CHECKPOINT. No broad source completion is claimed.

Current bounded status: agency response tracking, broker/current-scope isolation, MT and commercial terms lifecycle,22browser checks per product and SQL readback passed. Migration preserved74892original rows/175tables/167files; two retained requests and repeat setup passed. Feature1c7b285/source9f1637c are committed. Earlier gap statements above are discovery history; current full v4 session64243 remains the unfinished regression gate. No final source acceptance is claimed until current collectors, retained journeys and preservation/restart pass.

## Current-source continuation

The full v6 native SQL gate later passed exactly550/550 discovered integration cases plus1337/1337 units,485 named real SQL/process-restart cases, no skips and unchanged frozen source/assembly hashes. Operational, clean retained servicing and clean retained underwriting collectors passed. Two browser-test-only corrections followed that snapshot and passed their targeted retained journeys. A fresh v7 full SQL gate now owns literal final-source acceptance; its unit gate passed1337/1337 and discovery remains550. The guarded follow-through will run the strict streaming check, root/frontend gates and individual binding reconciliation after successful v7 completion. Thus the old blocking findings above are discovery history; do not interpret their earlier present tense as unresolved current product gaps. The ledger runtime statuses remain pending until that guarded final reconciliation writes them. Nine Phase11/12 controls retain their future ownership.

## Structural reconciliation before final runtime gate — 2026-09-23

A read-only independent pass recorded `.local/phase9-17-source-structural-audit.json`: original prototype SHA-256 `8F2CEBFC855FEF104A24A53CDA82DDDD7DD9774000B287EEC9BB050335048A77` matches the ledger; denominators remain 118 controls (109 Phase9-owned), 509 original display occurrences, 33 separate Commercial Combined claims occurrences, 10 branches and 13 supplemental inherited agency displays. Every one of the 109 owned controls has a nonempty per-control implementation and evidence reference, and every referenced path exists. Each original display, commercial occurrence and branch has a review binding; supplemental displays have implementation and evidence paths. No structural mapping is missing.

Runtime status fields remain stale despite these individual reviews: 31 owned controls, 432 original display occurrences, 20 commercial occurrences and one branch still say `not-implemented-in-phase9`; additional rows say the final acceptance is pending. Do not convert those fields or assert source completion from this structural audit. Reconcile each reviewed binding against the completed exact current SQL/browser reports, with remaining future Phase11/12 ownership explicit. The v6 gate later passed; the v7 current-source gate and guarded follow-through are active.
