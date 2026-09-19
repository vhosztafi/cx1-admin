---
phase: 07-policy-lifecycle-and-history
plan: '16'
status: complete
completed: 2026-09-19
requirements_completed: [POL-02, POL-03, POL-04, POL-05, POL-06, POL-07, POL-08, POL-09]
requirements: [POL-01, POL-02, POL-03, POL-04, POL-05, POL-06, POL-07, POL-08, POL-09]
---

# 07-16 — Complete servicing acceptance and demonstration

Completed after the fresh full backend gate passed at2026-09-19T21:33:22Z.

Implementation commits: `64b2b4e`, `e72cf61`, `7bf0da4`.

Both Motor Trade products have complete persisted adjustment, renewal, lapse and cancellation journeys, typed risk edits, scoped history and exact command recovery. The final17-stage servicing acceptance passes. Retained underwriting passes its carrier/discovery checks and all37 quote/agency/client journeys. Additive demonstration records include editable/leased/historical-base drafts, missing trading proof, internal referrals, queried/conditional carrier decisions, signed future adjustments, linked renewals, retained nonrenewal notification attempts and cancellation credits. No demo reset, sales-funnel edit, real provider delivery or cash payment occurred.

## Changes and review

Final acceptance exposed missing draft context and revised premium. Current scoped draft reads now supply policy reference, original preparer and exact immutable base premium; the UI shows issued insured context and a penny-exact proposed revised term premium. Old immutable command receipts remain compatible. Acceptance rechecks chronology after current-authority validation to reject a backwards system-clock step with409 before SQL. Windows event evidence and two-product RED/GREEN SQL regressions retain the original failure and corrected behavior.

Migration tests now respect existing template downgrade guards. Browser harnesses use fresh normal-API policy bases, actual cancellation reasons, adjustment rather than cancellation risk-editor drafts, current coverage state, paginated retained activity/quote lookup and explicit dynamic loopback ports. These changes preserve exact persistence/security assertions. Readback log labels no longer claim a process restart that the script itself does not perform.

Source review retains137 controls,393 field occurrences and10 branches.104 controls and330 field occurrences belong to Phase7; remaining33 controls/63 fields stay with approved Phase9/10 owners.110 composite field occurrences and7 branches have source/readback review with explicit distinctions from individual browser assertions. POL-01 remains compound and partial until documents, tasks/messages/claims and settlement views reach their owning phases. POL-02..09 are complete; POL-01 remains explicitly partial.

## Measured evidence

- Full current backend: PASSED. .local/phase7-16-current-full-5b1b949943ca497188ceb2fa6cb7b121, 883unit+339integration=1222 unique passing cases, including303SQL, no skips. The strict result gate passed; integration runtime1.3870hours. Built at implementation commit64b2b4e after the clock/context and dynamic-port corrections.
- Full servicing: .local/servicing-suite/2026-09-19T19-33-35-262Z-6d5132e1-90ba-406b-8010-12bd8bc85865/report.json,17/17 stages. Final isolated wrappers execute4 lifecycle+2 cancellation review+2 cancellation issue+2 history cases with no skips. These overlap backend identities; do not add them to1222.
- Retained underwriting: .local/underwriting-suite/2026-09-19T20-00-35-470Z/report.json; quote17 and agency/client20 reports linked in07-16-PROGRESS.
- Root source/contracts363 pass: .local/phase7-16-final-source-contracts.log. Frontend149 pass plus lint/typecheck/Next production build in.local/phase7-16-price-*. Current Release and solution builds pass with zero warnings/errors.
- Draft context/premium API-contract and desktop/390px visual checks: .local/servicing-context/2026-09-19T19-38-38-108Z/report.json. Both product values match actual immutable base plus saved rating; headers/premium mobile captures inspected.
- Dedicated RED/GREEN evidence: original-template migration fixture corrections, additive draft seed cases, scoped context, acceptance clock regression and dynamic-port browser regression remain listed in07-16-PROGRESS. Target reruns overlap full discovery cases.

Failed aggregates and TRX are retained. The previous complete baseline had883unit+337integration passes and2 port-binding failures; it is not a clean result and is not counted instead of the current full gate. Original earlier8 migration failures were corrected and rerun. Browser fixture/clock/discovery failures and fixes are recorded without inventing successful outcomes.

## Preservation and handoff

.local/phase7-16-final-preservation/report.json proves two additive initializations preserved all133 table count/SHA256 fingerprints. .local/phase7-16-final-restart retains actual before/after process identities and fresh-login identical6 policy/18 issued-version API graphs. Only request-time cutoff metadata is excluded from explicit-version comparison; history cutoffs are pinned. Current demo API38760/web79728 use canonical persistent keys and Next price build.

07-PHASE08-HANDOFF.md maps shared lifecycle/authority/money primitives and required distinct Commercial Combined risk/schema/product configuration. Human business/assistive-technology UAT, hostedCI and Docker execution remain unperformed. Native SQL2022 and Chrome provide the measured local evidence. Finance cash reconciliation and document/MID rendering remain their approved later-phase responsibilities.

Implementation path refinement: the existing .github/workflows/foundation.yml was extended instead of introducing the proposed ci.yml. Windows gate minimums are1222/303; Linux intentionally excludes the two Windows DPAPI cases and uses1220/301. Both production Next outputs and Chrome dependencies are prepared. Hosted execution remains unperformed.

## Self-Check

All required local gates passed. All16 plan summaries and phase verification exist.
Reviewed artifacts include `scripts/verify-servicing-suite.mjs`,
`scripts/verify-servicing-restart.mjs`, `scripts/verify-servicing-preservation.ps1`,
`backend/src/BackOffice.Infrastructure/Policies/ServicingDemoSeed.cs` and
`scripts/demo-command-journal.mjs`. Referenced implementation commits exist.
