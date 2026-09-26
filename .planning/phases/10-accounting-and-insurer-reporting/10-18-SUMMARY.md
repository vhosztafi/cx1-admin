---
phase: 10-accounting-and-insurer-reporting
plan: '18'
subsystem: finance
requirements-completed: [FIN-01, FIN-05, FIN-08]
completed: 2026-09-26
---

# Retained refund/payment and final acceptance

A legitimate posted cancellation credit and collected same-policy allocation fund a £1.00 refund, independently approved by the additive finance reviewer and paid through the deterministic demo adapter. Same-key replay retains the same refund/payment and one provider operation/cash posting. No real transfer occurred. Implementation commit: `806eb85`; fixture/evidence correction: `daaec6e`.

The fresh full gate passed 1,400 unit and 577 integration cases. Strict TRX validation counted 1,977 unique passed cases, including 512 real SQL, zero skips. Root 451 and web 214 tests, lint/typecheck/build, 25 live finance browser checks, ten operational collectors, two identical additive-initialization fingerprints and ten exact restart readbacks passed. Evidence is under `.local/phase10-gap-final/`; refund journey is `.local/phase10-gap18-refund/journey.json`.

The first full attempt was stopped after finding a stale browser fallback bundle and the old seven-user seed count. Both focused regressions passed 2/2; the final unfiltered run used the current-source acceptance manifest. Independent non-SQL gates and retained checks ran while the long disposable SQL suite continued. The owned restart used ports 5095/3193; the existing process on 5087 was left untouched. Inherited generated files were not committed.

The user replaced repeated exhaustive runbooks with focused validation on 2026-09-26. See PROJECT.md. Closed-period earned-premium read coverage and human UAT remain explicit Phase 13 follow-ups, with no further full gate required before Phase 11.
