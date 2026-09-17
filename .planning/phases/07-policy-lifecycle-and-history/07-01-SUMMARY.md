---
phase: 07-policy-lifecycle-and-history
plan: '01'
status: complete
completed: 2026-09-17
requirements: [POL-02, POL-04, POL-05, POL-06, POL-07, POL-08, POL-09]
requirements_completed: []
production_commit: 66da10d
---

# 07-01 — strict servicing contracts and pure lifecycle rules

Completed the prerequisite library and contract plan. No servicing runtime API,
SQL write, browser journey or whole POL requirement is declared complete here.

## Delivered

- Closed capture/change/lease, evidence/referral/capacity, terms/acceptance,
  cancellation preview/approval and mutually exclusive issue contracts.
- New immutable `issued-servicing-1` schema with exact servicing lineage;
  retained `issued-quote-1` schema and fixtures unchanged.
- Generated OpenAPI now has 378 operations (364 retained plus 14 additions).
  Existing operation IDs are preserved. Refined/new servicing commands are
  explicitly pending runtime implementation, with current scope before replay,
  strong ETag/lease, CSRF, operation keys and no-store response contracts.
- Typed capture reuses existing Motor Trade fields, keeps stable target IDs in
  the envelope and rejects arbitrary server-owned authority and money fields.
- Pure effective/processing selection, cumulative schedules, duplicate/temporal
  checks, annual proration, original signed-component returns, single-fee totals,
  renewal-experience assessment and cancellation date blockers.
- Fictional versioned cancellation/renewal examples; exact multi-date 129.14
  gross / 118.95 net movement and 326.96 cancellation credit, never cash paid.
- Source assertions retain 137 controls, 393 exact-source occurrences and 10
  conditional branches with explicit owners. No control is marked implemented
  merely because it has a contract.

## Verification and review

- `.local/phase7-01-targeted-final.log`: 14 contract/source tests passed, 0 skipped.
- `.local/phase7-01-contracts-4.log`: OpenAPI lint and all 348 contract/source/gate
  tests passed, 0 skipped; 949 reviewed controls resolve against 378 operations.
- `.local/phase7-01-unit-full-2.log` and fresh TRX directory of the same stem:
  659 unit tests passed, 0 skipped, including 17 ServicingRules cases. TRX counters
  and test-class membership were inspected directly.
- RED evidence is recorded in pure-red and pure-extension-red logs (missing-type
  or missing-method compile failures) and contract-red (missing schema). This is
  not misrepresented as assertion-only RED. During implementation source mapping
  and prior evidence-shape assertions failed and were corrected to the approved
  London-intent/file-version contracts; the final full contract suite passes.
- `git diff --check` passed. No migration, running API restart, demo reset,
  sales-funnel edit, external delivery or payment occurred.

Inline review found no unresolved HIGH/CRITICAL defect within this prerequisite
scope. The pure helpers explicitly require scoped inputs/current authorization;
they cannot themselves authorize or persist issue. Schema validation cannot
prove aggregate identity, readiness, 2 MiB/1,000-item aggregate limits, current
evidence, duplicate target semantics or receipt replay safety. The owning plans
must enforce these in services and SQL, with negative tests.

## Refinements and downstream obligations

A separate servicing model/generator and new issued-servicing schema keep shared
quote schemas unchanged. Source mappings were updated in their generator and
reviewed JSON: effectiveAt becomes commonEffectiveIntent; draft evidence pins
fileVersionId/cycleId. Existing contract tests were updated to assert the new
required pins rather than retain the old placeholder shape.

07-02 now implements scoped temporal reads/discovery and browser cutoffs.
07-03..14 own persisted drafts, runtime DTO parsing, same-owner SQL constraints,
leases, rating/authority, proof, terms, acceptance, issue and lifecycle outcomes.
Later issue writers must exercise real future/multiple-transaction SQL cases.
07-15/16 complete history/UI/source and regression verification. POL-01 retains
Phase9/10 operational/finance boundaries. Human UAT, hosted CI and Docker remain
unperformed; no SQL/browser evidence is claimed for this pure/contract plan.
