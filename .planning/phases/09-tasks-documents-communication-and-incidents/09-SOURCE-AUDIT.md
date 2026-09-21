# Phase9 source audit — initial reconciliation

2026-09-21. Extraction complete; field-by-field API mapping and final runtime evidence remain pending. Never interpret this inventory as implementation acceptance.

`scripts/build-operations-source.mjs` preserves the original prototype hash and control IDs. It unions62 directly assigned Phase9 controls,34 explicit Phase7/8 handoffs and mapped operational consumers, resulting in118 unique original controls. This includes future dashboard/report/template consumers with explicit Phase12/11 owners; inclusion does not move their whole phase intoPhase9.509 rendered occurrences retain their global source indices and exact raw observations.33 commercial claims occurrences retain their separate Phase8 inventory indices (do not sum these overlapping populations). Ten supplemental source branches retain exact decoded-source needles and explicit implementation owners.

Direct source methods pTasks/pTask/pLogClaim/pClaim are fully retained at rendered-occurrence level. Shared policy/quote/client/agency/commercial operational tab observations and the task modal are included. Unrendered selectors/branch actions are recorded separately, including vehicle, driver, drivable, successful handoff, task reopen and bulk complete/reassign/due. Existing global control extraction renders only default/ready scenarios, so absence from its control list never proves absence from source.

## Discovered requirements retained in plans

- Task type menus differ: filters include Agency onboarding, while creation includes Complaint. The implementation must cover the union with a closed published type catalogue and retain meaningful filters; do not drop Complaint because it was absent from the first API enum.
- Medium source priority maps to normal API priority; record the mapping. Awaiting information is an explicit stored state, overdue is derived. Source-created-by view has a prototype bug and must use actual CreatedBy.
- The default source renders disabled handoff and property fields; successful handoff plus vehicle/driver/drivable selectors are supplemental branches, not new invented capabilities.
- Document rows imply functional Preview/Download even when no onClick exists. Statement of fact, selected endorsements, current/superseded schedules and uploaded evidence must all become actual authorized bytes.
- Match information queries and quote request-more-information actions connect to real communication without treating prior decision records as already delivered. Historical as-at Schedule links choose the exact source version.
- Commercial “See the motor trade version” is an invalid prototype fallback already removed inPhase8; verify it stays removed, rather than implement a motor route on CC.

## Current evidence

Three Node source-invariant tests initially failed because the ledger did not exist, then passed after extraction. They independently compare original IDs/handlers, exact display indices/objects and source branch needles. This proves preservation and planned ownership only. Each runtimeStatus remains not-implemented-in-phase9; later plans replace it only after actual saved behavior and tests. Detailed schema/operation field mapping and additional inherited non-rendered display review remain09-01 work before its summary/09-02.
