# Phase 7: Policy lifecycle and history — Discussion log

Audit trail only; planning consumes07-CONTEXT.md.
Date:2026-09-17. Single autonomous pass; all eight areas selected.
Selections below were made by the agent under the standing user authorization,not supplied as new user answers.

## Draft ownership and recovery

[auto] Q: How should editing conflicts work? → Selected: Five-minute renewable lease plus ETag; permissioned reasoned takeover; retain unsaved edits for comparison. (recommended default;D-02).

| Option | Rationale | Selected |
|---|---|---|
| Recommended | Five-minute renewable lease plus ETag; permissioned reasoned takeover; retain unsaved edits for comparison. | Yes |
| Alternative | Unrestricted last-write-wins | No |

## Effective chronology

[auto] Q: How should future and backdated changes affect views? → Selected: Approved LIFE-01: half-open London intervals, effective/knowledge cutoffs, later cover slices, explicit backdating authority and no out-of-sequence rebasing. (recommended default;D-03/D-04).

| Option | Rationale | Selected |
|---|---|---|
| Recommended | Approved LIFE-01: half-open London intervals, effective/knowledge cutoffs, later cover slices, explicit backdating authority and no out-of-sequence rebasing. | Yes |
| Alternative | Replace the current snapshot in place | No |

## Underwriting and acceptance

[auto] Q: Can adjustment or renewal reuse old acceptance? → Selected: Require current exact draft/schedule/rating/terms/evidence acceptance and authority; reuse pure rules but keep subject ownership explicit. (recommended default;D-05).

| Option | Rationale | Selected |
|---|---|---|
| Recommended | Require current exact draft/schedule/rating/terms/evidence acceptance and authority; reuse pure rules but keep subject ownership explicit. | Yes |
| Alternative | Copy the original acceptance | No |

## Financial movements

[auto] Q: How should return premium be computed? → Selected: Exact component/coverage-interval lineage and approved worked examples; one fee per transaction; credits are not cash refunds. (recommended default;D-06).

| Option | Rationale | Selected |
|---|---|---|
| Recommended | Exact component/coverage-interval lineage and approved worked examples; one fee per transaction; credits are not cash refunds. | Yes |
| Alternative | Prorate the original premium regardless of prior adjustments | No |

## Renewal timing

[auto] Q: Which snapshot and dates drive renewal? → Selected: Expiring-end applicable snapshot; linked nonoverlapping local-calendar term;45-day invitation and14-day post-expiry auto-lapse defaults; no fabricated uninterrupted late cover. (recommended default;D-07/D-08).

| Option | Rationale | Selected |
|---|---|---|
| Recommended | Expiring-end applicable snapshot; linked nonoverlapping local-calendar term;45-day invitation and14-day post-expiry auto-lapse defaults; no fabricated uninterrupted late cover. | Yes |
| Alternative | Copy arbitrary latest version and automatically bind at expiry | No |

## Cancellation consequences

[auto] Q: What does issuing cancellation commit? → Selected: Current reason/notice/authority review, immutable cancellation graph, credit/refund obligation and durable notices; preserve source history and abandon conflicting drafts atomically. (recommended default;D-09).

| Option | Rationale | Selected |
|---|---|---|
| Recommended | Current reason/notice/authority review, immutable cancellation graph, credit/refund obligation and durable notices; preserve source history and abandon conflicting drafts atomically. | Yes |
| Alternative | Change a status flag and claim payment completed | No |

## Policy record and future modules

[auto] Q: How should remaining policy tabs appear? → Selected: Show actual persisted lineage/requests/obligations; retain explicit Phase9/10 module owners; no fabricated documents/tasks/claims/cash. (recommended default;D-01/D-10).

| Option | Rationale | Selected |
|---|---|---|
| Recommended | Show actual persisted lineage/requests/obligations; retain explicit Phase9/10 module owners; no fabricated documents/tasks/claims/cash. | Yes |
| Alternative | Populate prototype sample rows as live records | No |

## Source fidelity and delivery

[auto] Q: How should this large phase be divided and verified? → Selected: Source/data/API/UI design first; bounded sequential vertical slices; fresh unit/SQL/browser/restart/preservation gates. (recommended default;D-11/D-12).

| Option | Rationale | Selected |
|---|---|---|
| Recommended | Source/data/API/UI design first; bounded sequential vertical slices; fresh unit/SQL/browser/restart/preservation gates. | Yes |
| Alternative | Implement one large unverified policy module | No |

## Discretion and deferred scope

Architecture and exact seeded demo rules remain agent choices after research. Approved lifecycle/financial rules are retained; unsafe alternatives are recorded for audit,not re-opened as user questions. No pending todo files to fold in. Deferred modules remainPhase8..13 as enumerated inCONTEXT.
