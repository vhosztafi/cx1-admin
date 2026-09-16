# 06-04 execution checkpoint

Started after06-03 commits b3c642d/c22cf42/d55f31b. Inline autonomous execution;
no subagents. No06-04 completion is claimed by this checkpoint.

## Implemented, not yet committed

- Rating overview, real premium/factors/provenance, independent blockers,
  team assignment, saved job status and discoverable paginated rating history.
- Exact frozen action requests for rate/re-rate, submit, return and published
  refresh. Current account guard, unknown-outcome exact retry, navigation guard,
  stale reason retention/readback and explicit keyboard focus wrapping.
- Requested stock/premises/tools choices with stable IDs, explicit selected/not
  selected, GBP limits/excess, actual premises targets and persistent field error
  buffers. Uses quote-cover.tsx/RequestedCover rather than placing the whole form
  in the wizard; wizard guidance and existing capture controls remain integrated.
- Exhaustive quote labels in receipt/list; source layout314px rail, mobile stack.
  Rejected historical requests never display zero as an offered premium.
- Required UI projection refinement: assessment job/team/approved refresh offers,
  plus scoped protected-cursor GET quotes/{quoteId}/ratings. Same-current-authority
  checks and conservative SQL version cursor invalidation. No new migration.

## Evidence to date

- Red projection tests failed for missing fields/routes;12SQL projection/runtime
  tests passed after implementation in `.local/phase6-04-projections-first`.
- Full backend final production changes:727passed (601unit/126integration),99
  real SQL, zero skips, `.local/phase6-04-backend-20260916-first`; integration12m2s.
  assert-test-results passed exact727/99 minimums. No later backend code change.
- 86 frontend tests passed, `.local/phase6-04-web-tests-final.log`;316 contract
  tests passed, `.local/phase6-04-contracts-final.log`; OpenAPI lint valid with10
  retained warnings before latest required-array contract-only refinement.
- First Chrome run caught missing exact accessible names on cover choices; fixed.
  Second correctly blocked rating after product refresh invalidated vehicle
  provenance. Browser now records fresh explicit manual decisions after refresh.
- Third Chrome run passed both products with real agency activation/terms approval,
  stored cover, explicit refresh/immutable old proposal, durable rating, response
  loss exact replay, submission, stale412/retained reason, return/history and
  desktop/390px containment. `.local/phase6-04-browser-third.log`.
- Expanded final browser run found native dialog Tab focus leaving the document.
  Explicit first/last wrapping and retry focus added; final rebuild/rerun pending.
  Do not claim expanded re-rate/history paging/presentation fixtures passed yet.

## Current processes / next work

API preview PID48116; Next preview PID24620 before focus-fix restart. Validate
owned command line before stopping. API port5087, web3100; no notification worker.
`.local/phase6-04-focus-build.log` is the current production build. Browser script
`scripts/verify-underwriting-rating-browser.mjs` creates only additive fictional
agency/client/quotes through actual APIs; old demo agreements remain untouched.

After build, restart owned Next; rerun browser script. It now includes actual
re-rate, paginated history and keyboard focus, and separately labelled intercepted
pending/failure/expired read fixtures. Inspect fresh desktop/mobile screenshots
after scrolling to top (older full-page captures placed fixed headers mid-page).
Finish contract status/docs/source ownership reconciliation and relevant checks;
commit production, then SUMMARY/evidence, thenSTATE/ROADMAP. Keep compound
requirements pending. Stop only owned previews before06-05 schema work.

06-05 read-only reconnaissance done: PLAN, relevant data-design record tables,
RULE-CATALOG conditions/source UW09/UW22/W07 and QuoteUnderwritingScope inspected.
No06-05 implementation yet. Exact-cycle any-driver minimum-licence warranty remains
an explicit05 responsibility. Existing quote fixture browser scripts assume two
catalogue rows; final regression owners must distinguish eligible retained versions
from newly published offers rather than removing valid historical choices.

## Final checkpoint supersedes provisional sections above
Completed in da15815; final backend727/99SQL in phase6-04-backend-20260916-metadata, integration13m6s;86frontend/316contracts, lint/typecheck/build and expanded browser all passed. Final focus wrapping, metadata and screenshots verified. API65288/Next1732 stopped after command-line validation. See06-04-SUMMARY for exact evidence and limitations. Proceed06-05.
