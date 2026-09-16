# 06-06 preparation

**Completed in f359532.** Full743/108SQL,90frontend,317contracts and both-product
actual browser including account switch, stale412 and bulk denial passed.
See06-06-SUMMARY for final evidence. All sessions collected and owned previews
stopped. Earlier checkpoint/preparation text below is historical. Continue07.

## Active execution checkpoint 2026-09-16 23:48 local

Uncommitted implementation now includes server authorityViews (separate current
grants, no merged maxima), pure2tests and real API projection assertions. Both
targeted tests passed. Full backend running session47867, directory
`.local/phase6-06-backend-20260916-first`, log`.local/phase6-06-backend-first.log`;
608unit passed; integration pending. No backend changes since that build.

UI files now exist: quote-underwriting/referral-decisions/underwriting-evidence,
decision-command modal, pure underwriting-decisions helper and3semantic tests.
QuoteReceipt has Underwriting tab, Overview actual endorsements/licence timing.
89frontend tests, lint/typecheck/build passed. BuildAPI origin5087.

Owned running previews: API66724 and Next67288, PIDfiles`.local/phase6-06-api.pid`
and web.pid. Start-process hidden. Verify commandlines before stopping. API uses
already built output (do not rebuild while it holds files). Workers local only,
AgencyNotificationWorker disabled. DB/migrations from05 preserved.

Actual Chrome journey running session20552, log`.local/phase6-06-browser-first.log`.
Script`verify-underwriting-decisions-browser.mjs` depends on04rating browser report
for existing approved product/relationship fixtures, creates fresh fictional quotes.
Both-product query/decline/reopen/conditional, lost-response retry, actual proof
upload/review/resolution/withdrawal, read-only servicing/focus/desktop390screens.
Must add/verify explicit stale412 with retained form and bad bulk/no partial
decision scenario (Combined excess stock plus missingpremisesproof), then inspect
screenshots. No browser pass claimed yet. Any discovered UI failure must be fixed
and rebuilt after stopping only owned Next. No new backend migration needed.

Next: collect fullbackend/assert measuredcounts (expected743/108, verify), collect
browser/fix, addmissingbrowsercases, inspectactualscreens, finalcontracts/lint etc,
sourceownership, summary/commits/state thenautomatically07. All prior textbelow is
preparation history, superseded by this active checkpoint.

Implementation has not started. Dependency06-05 is completing full verification.
Continue sequentially inline, no subagents. User authorised automatic progression.

Resolved existing entry point: components/quotes/quote-receipt.tsx. It currently
has Overview/Risk/Cover/Drivers/Vehicles/History tabs. Add Underwriting while
preserving all capture/history behavior. New quote-underwriting.tsx,
referral-decisions.tsx and underwriting-evidence.tsx are intended outputs.

Reuse Panel/Status, underwriting-layout (314px rail), existing quote form classes,
useQuoteResource/Paging and the native dialog/focus/recovery pattern in
underwriting-action.tsx. Exact frozen body/key/root ETag plus child ETags must
survive uncertain retry. A 412 retains the draft and allows readback, not a silent
retry against new versions. Current account identity is rechecked before sending.

Backend projections: assessment proofRequirements, appliedEndorsements,
assuranceHash, current capabilities and current cycle. Referral detail includes
child condition ETags and recorded question. Evidence and decision histories are
paged. Listing referrals/evidence includes old cycles: filter current-cycle actions
explicitly while retaining historical visibility. Existing file-list/download
routes supply actual content type/length and immutable bytes.

Forms use the nine closed condition types. Signed statement stays disabled with
prepared-terms explanation until08/09. Documentary purposes and stable targets come
from current proposal/proof requirements. Upload is a separate root-versioned
command; refresh before attaching its file. Review does not resolve a condition
automatically; resolution selects exact same-condition reviewed proof.

Test first: selection/child-version/body freezing, foreign/stale target rejection,
condition output, lost-response identity, purpose/review distinction. Actual browser
journeys must exercise both products, stock plus missing premises proof, query,
conditional/decline/reopen, review/resolution/withdrawal, role/stale/bulk rollback,
keyboard, desktop and390px screenshots. Do not call API-only journeys UI evidence.

Backend follow-up needed for requested/actor/binder dimension projections: current
referral DTO reserves optional money fields but read model does not populate them.
Implement a safe server projection with exact current grant context, including
non-money dimensions, and test it; do not infer actor authority in the browser.
Any backend change requires a new full regression before completing06-06.

Replace overview endorsement/proof placeholders with actual server values.
Prototype licence check timing is "At renewal" after required driver proof has
been reviewed; do not invent a calendar interval or imply external DVLA verification.
