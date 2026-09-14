# Phase 4 plan review

Reviewed inline2026-09-14 under approved sequential/autonomous workflow. Result: PASS for planning; runtime implementation and verification remain pending.

## Coverage and sequencing

Eight plans,23 tasks. GSD verify.plan-structure passes all eight with no errors/warnings. YAML/dependency check confirms backward-only acyclic dependencies,2..3 tasks per plan, all five AGY requirements and existence of referenced context/research/pattern/UI/data-API/validation artifacts. No subagents or branch changes were used.

| Requirement | Delivery owners | Verification owner |
|---|---|---|
| AGY-01 | 04-01 contracts,04-02 drafts,04-03 evidence,04-05 staged administrator,04-06 activation | 04-08 full wizard and negative activation matrix |
| AGY-02 | 04-05 users/invitations,04-06 suspension,04-07 trusted scope/current permissions | 04-08 real-cookie access/role/token/approval races |
| AGY-03 | 04-02 list/detail,04-06 products/terms,04-07 permissions/activity/accounts settings | 04-08 present features; real statements remain Phase10 |
| AGY-04 | 04-07 common safe projections/internal preview/agency identity | 04-08 competitor/internal-field denial; real policy/task sections remain owning phases |
| AGY-05 | 04-04 durable notifications consumed by04-05/06 | 04-08 provider/worker restart, retry and truthful delivery results |

## Semantic review and resolved issues

1. Source draft user list versus ordinary issued invitation was initially underspecified. CONTEXT/DATA-API now separate staged/no-token/no-delivery from issued14-day invitations and independent delivery status. Resend revokes old acceptance, not merely sends the same credential again.
2. Activation countersign and commercial version authority cannot be client-supplied. State/terms requests now bind base version and immutable intent; different authorized user applies in the same transaction. Proposal creation specifically does not advance its captured business version, preventing immediate self-staleness. Stale live proposals may be superseded with preserved history.
3. Phase2 products are draft definitions. Distribution eligibility is explicit and separate; no false published/rateable status. Capture/issue readiness stays in Phase5/6 backlog. The Fleet-only prototype row does not expand the approved three-product scope.
4. Suspension introduces a cross-phase lock risk. DATA-API defines agency-first ordering, and04-06 explicitly owns ClientCommands/MatchService integration and a suspension/link race test rather than adding an inverse lock in just one branch.
5. Evidence cannot rely on future Document IDs or form status fields.04-03 owns minimal scoped file storage plus immutable verification/fingerprint/expiry records;04-01 adds the required typed contracts. Phase9 owns generic document/task integration.
6. Internal sharing preview must not imply a broker portal.04-07 uses the same safe services for audited internal selection and real trusted agency API identity, preserving staff sessions. Future statements/policies/open items are explicitly partial acceptance, with backlog owners.
7. All ordinary notification reads/receipts/audit exclude raw invitation material. A protected Development-only audited reveal is separately specified for local demo acceptance; expired/revoked tokens remain invalid even if an old simulated delivery completes.

## Size and execution guidance

04-06 is the largest slice: activation, suspension and agreed terms each has its own task, unit/SQL/API checks and atomic commit. Execute sequentially and retain progress checkpoints between tasks; no task may be marked complete because its UI exists. If implementation reveals a necessary additional contract or migration, document and test it before moving on. Each other plan owns a coherent vertical responsibility and2..3 tasks; existing modules are reused rather than replaced.

UI contract check passes source layout/typography/rail, full state coverage, explicit permission boundaries, responsive/dialog accessibility and honest future content. Research is based on repository inspection and cited Microsoft primary docs. No runtime, human UAT or hosted CI result is claimed by this review. No unresolved planning blocker requires user input. Begin04-01 automatically.
