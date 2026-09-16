# 06-06 preparation

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
