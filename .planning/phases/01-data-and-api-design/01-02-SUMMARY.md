---
phase: 01-data-and-api-design
plan: "02"
status: complete
requirements: [DES-02, DES-03, DES-05]
---

# Data, lifecycle and finance design

Defined the relational dictionary/ERD, constraints and ownership; three product-discriminated issued JSON contracts and a partial draft contract; immutable version history; role/agency/field permissions; transaction, renewal, cancellation and durable-job transitions. Financial movements retain original component and coverage lineage for adjusted-policy cancellation. JSON does not contain restricted support flags.

Artifacts: `docs/design/DATA-MODEL.md`, `LIFECYCLE.md`, `PERMISSIONS.md`, `FINANCIAL-EXAMPLES.md`; `contracts/schemas/`, `contracts/examples/`; reproducible schema generator; executable design decision oracles. Ajv and formats are pinned in the root tooling package/lockfile.

Validation: `pnpm test` passed 19 tests (4 source inventory, 15 schema/decision cases). Strict draft-2020-12 schemas compile; all three fictional product examples pass; mutated invalid inputs fail. Cases cover date/precision errors, product mismatch, restricted fields, full-term/MTA/cancellation components, cancellation after MTA, leap/DST calendar days, effective/knowledge history, stale rating/acceptance, authority example, ETag/replay conflict decisions and allocation residual/scope decisions.

Limits: these are design fixtures, not a running API or migrated database. SQL atomicity, complete product-question completeness, real permission enforcement, file delivery, locks and durable retries remain implementation verification. Plan 01-03 must supply the complete API surface; plan 01-04 must independently verify design/action traceability before Phase 1 completion.

Source/control foundation committed separately as `59a9761`. This summary is committed with the data-contract artifacts.
