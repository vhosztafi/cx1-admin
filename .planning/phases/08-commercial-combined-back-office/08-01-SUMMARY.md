---
phase: 08-commercial-combined-back-office
plan: '01'
status: complete
subsystem: contracts
tags: [commercial-combined, json-schema, openapi, source-coverage]
requires: [07-16]
provides: [closed-cc-capture, closed-cc-issued-risk, cc-source-ledger, cc-data-api-contract, independent-cc-money-examples]
affects: [08-02, 08-03, 08-04, 08-05, 08-08, 08-09, 08-10, 08-15]
requirements-completed: []
completed: 2026-09-19
---

#08-01 — Closed CC contracts and source reconciliation

Implementation commit: `46c9b24`. Planning prerequisite: `cf55ba5`. Shared lifecycle prerequisite: `3b99709`.

CC now has separate closed draft/issued schemas, scoped question/reference definitions, source/field ownership, exposure/incident payload contracts and independently reconciled demo money examples. Runtime CC quote/issue activation is intentionally the responsibility of the following plans; no completed business requirement is claimed from contracts alone.

## Delivered

- commercial-combined-capture-1 preserves schemaVersion1.0 for the current SQL QuoteRevision guard, with a separate CC discriminator. No writable premium/book result, motor fields or system identity.
- issued-commercial-1 retains shared term/premium/settlement/provenance while giving CC its own risk/cover. Old MT schema/operation semantics compare unchanged.
- All166 original capture controls,109 questions and60 CC policy-control records retained;298 display items and7 supplementary policy facts mapped. Runtime evidence remains pending. Six proof controls belong to actual evidence workflows.
- Exact source occupancy, eight employee wage categories and12 loss types retained. Cross-field examples reject duplicate IDs/questions and foreign loss subjects; selected-cover conflicts remain visible readiness blockers on incomplete drafts.
- Three pending exposure GET contracts use closed internal/agency DTOs. Internal book fields require extra capability; agency DTOs cannot include book totals or foreign identifiers. No route was registered in the API.
- Data/API design fixes stable book and published limit versions, immutable zero/cancellation headers, E/K metadata, lock-before-scope protocol, new-effect-only capacity checks and exact source/template payloads.
- Fictional annual and positive/zero/negative half-term examples independently prove exact pennies, tax/commission and one MTA fee.

## Acceptance evidence

| Check | Result |
|---|---|
| Full source/contract suite |375 passed,0 skipped/failures —.local/phase8-01-full-source-final.log |
| Final affected CC checks after schemaVersion correction |12 passed,0 skipped/failures —.local/phase8-01-final-targeted.log |
| OpenAPI final validation |Exit0,422 operations —.local/phase8-01-openapi-accepted.log |
| Warning audit |2 retained existing composition warnings;56 unused components including future contracts —.local/phase8-01-openapi-warning-audit.log |
| Original API/schema retention |Every pre-existing path/component deeply equal to the pre-slice committed baseline |
| Source/API matrix |949 controls,5 conditional rules,422 operations validate; regenerated five stale permission labels from current contracts |
| Diff integrity |git diff --check passed; no frontend-code/API/backend/database changes |

RED evidence:.local/phase8-01-contract-red.log and.local/phase8-01-semantic-red.log. The full test caught an obsolete generated six-field ledger after a seventh source field was added; rebuilding produced the checked seven-field output. An initial restricted-wrapper Node shutdown assertion occurred after valid OpenAPI output; subsequent native runs exited0 and are the accepted evidence.

## Deviations and boundaries

Additional source generator, invariant helper, issued-schema and golden-example companion files were needed to make the planned contract executable. JSON Schema cannot express uniqueness by one member of objects: the composed semantic contract handles that, and08-02 must enforce the same rules in .NET. The plan wording was corrected accordingly without relaxing the intended invalid-input behavior.

No new unit framework/package, live API, SQL migration, product activation, browser claim, actual document generation or incident creation. CC-01..04 remain pending operational implementation; CC-05 remains partial through Phase9. Human UAT, hostedCI and Docker remain unperformed.

## Next plan

08-02: publish CC capture/reference/question configuration, wire the typed .NET proposal/readiness boundary and prove create/save/reload/current-scope/replay on native SQL. Read the actual current model constraints before extending service dispatch; retain the existing schemaVersion and product-identity guards. Keep original source catalogues as provenance and use separately published CC configuration.

## Self-Check: PASSED

Implementation commit exists; named generated/code/doc artifacts exist; current affected tests and retained full-suite evidence pass. Source runtime status is not overstated. Production changes were committed before this summary.
