---
phase: 17-motor-trade-quote-entry-and-funnel
plan: 01
requirements_completed: [QUO-07, QUO-08, QUO-09, QUO-10]
---

# Saved Motor Trade funnel integration

Quote creation now selects or creates a saved client, links or selects its agency, selects an actual saved broker contact and chooses an eligible Motor Trade product before opening cx1-implementation. The existing source journey retains its fourteen steps. Its host extension saves current and partial source answers with a typed proposal in an immutable revision, resumes the correct saved quote and returns after a confirmed save. Fixed demo identities are no longer used by the operational entry point.

Exact local and explicitly approved hosted origin pairs, source window and nonce checks protect the bridge. The parent retains authenticated actor, relationship authorization, ETag and idempotency checks. A lost committed response followed by denial retains the same command until authorized replay confirms it. No credentials cross the bridge. Generated nested identities are returned into the source state.

Validation: both app type checks and focused parent lint pass; four adapter tests pass; nine backend raw-state tests and 45 API contract tests pass; four source test files/23 tests pass. The additive isolated-fixture browser check passed inline client creation, agency linking, contact/product selection, raw false save/resume, lost-response/denial/identical retry and return to quote 4872a296-56ad-46a3-b104-3a9412bf75bb. Evidence: output/playwright/v1.1/funnel-integration.json and quote-funnel.png.

The additive nullable-column migration was applied to the isolated fixture only. Hosted deployment has not occurred. Vehicle/address provider integrations retain manual fallback; backend readiness still governs rating. Prototype-only review fields, Combined supplemental risk capture and quote issue presentation belong to phase 18 and are not represented as source-funnel questions.
