# Plan07-01 in progress

2026-09-17. Not a completion SUMMARY; plan remains unfinished.

Implemented in working tree (not yet production-committed):

- backend/src/BackOffice.Application/Policies/ServicingRules.cs: pure E/K issued
  version selection, ordered cumulative change schedules, date/identity checks,
  local-calendar annual proration and signed original-component returns.
- backend/tests/BackOffice.UnitTests/ServicingRulesTests.cs:14 meaningful cases,
  including mixed-date326.96 net credit, negative original movements, term/DST
  behavior, duplicate changes, future slices and forbidden driver backdating.

RED evidence:.local/phase7-01-pure-red.log shows missing new types before
implementation (compile failure, not a runtime assertion failure). GREEN:
.local/phase7-01-pure-1.log and TRX directory,14 passed,0 skipped.
Full unit regression:.local/phase7-01-unit-full-1.log and TRX directory,
656 passed,0 skipped. No Phase7 SQL/API/browser behavior claimed; these functions
do not themselves authorize, persist, lock, detect previously returned components
or apply typed risk changes. Their callers must enforce those boundaries.

Next: implement strict servicing schemas/examples and generated OpenAPI catalogue,
source-contract tests and SERVICING-CONTRACTS documentation. Preserve old issued
JSON, generated operation IDs and unavailable runtime endpoint fences. Complete
remaining07-01 pure edge cases as schemas settle, then run full plan gates and
commit implementation followed immediately by a real completion SUMMARY.

Planning commit correction:176cdbc recorded review/supporting docs but the GSD
helper omitted absolute-path plan arguments.09dc2b9 persisted all16 reviewed plan
revisions using repository-relative paths; working tree was clean afterward.
Do not re-run plan generators, which would overwrite approved refinements.
