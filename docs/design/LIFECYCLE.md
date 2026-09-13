# Lifecycle and temporal rules

Version 1, demo domain defaults. Every mutation includes actor, reason where specified, expected ETag and correlation ID. Rejected commands leave stored state unchanged. UI labels are projections of the states below; never accept an arbitrary status patch.

## Quote and rating

| From | Command | To / effects | Preconditions |
|---|---|---|---|
| none | createQuote | draft + revision 1 | Active authorised agency, valid product reference |
| draft/rated/referred/approved/sent/accepted | saveQuoteRevision | draft + new revision; old rating/acceptance remain history only | Current ETag; typed draft; reason for material edit after sending |
| draft/rated/referred/approved | rateQuote | rating-pending | Complete product answers; match review resolved; current product/binder |
| rating-pending | completeRating | rated or referred | Result matches input hash/revision; persist individual rule hits |
| rated/referred | decideReferral | approved when all decisions/conditions satisfied; otherwise referred | Actor meets required authority; evidence supplied; reason |
| approved | sendQuote | sent + document/message work | Current unexpired rating and resolved required evidence |
| sent | acceptQuote | accepted | Exact revision/rating/terms hash and acceptance evidence |
| accepted | bindQuote | bound + Policy/Term/Transaction/Version/Journal/outbox | Current rating, acceptance, approvals, authority, active agency/product; unique issue operation |
| any unbound | withdrawQuote | withdrawn | Reason; cancel outstanding quote work that is no longer applicable |
| any | cloneQuote | New draft/reference | Copy selected risk only, new child IDs; no copied acceptance, decisions, bind state or private cross-agency content |

Expiration is derived from rating/terms validity and clock, not destructive deletion. A quote cannot remain issuable merely because its stored label says accepted. Re-rating the same revision may create a new rating ID/terms hash; old acceptance is invalid unless the domain explicitly proves identical contractual terms, and v1 conservatively requires new acceptance.

## Referral and capacity response

Open → assigned → queried/awaiting-information → ready-for-decision → approved/approved-with-conditions/declined. Reopen is explicit and audited. Approved-with-conditions is not issuable until required conditions are marked satisfied with evidence. Conditions that change terms require a new rating/acceptance cycle. Missing driver data and stock authority are independent referrals.

Capacity escalation: draft → queued → sent → query/approved/approved-with-conditions/declined. Provider message, reference, named underwriter, received timestamp and verbatim supplied response are retained. A demo provider callback uses a unique event ID. An internal user recording a response records evidence; it is not a shortcut to impersonate a capacity-provider role.

## Policy drafts and concurrency

Draft states: editing, rating-pending, referred, awaiting-acceptance, ready-to-issue, issued, abandoned. Save/change date/add/remove risk increments revision and invalidates downstream applicability. Editing uses an expiring five-minute lease renewed during activity (demo default); takeover requires `draft.takeover` and reason. Revoked/suspended sessions cannot renew leases. API also requires current rowversion; a lease never authorises stale overwrites.

The issue transaction locks/checks the policy aggregate, draft revision and base version before appending records. Competing adjustment and cancellation cannot both use the same stale base. On conflict return 409 with current base and a reload/compare action; do not silently rebase.

## LIFE-01: Effective time versus knowledge time

Terms use half-open intervals `[StartsAt, EndsAt)`. Contractual date/time inputs include Europe/London; normalise to UTC while preserving original intent for audit. Reject non-existent spring-forward local times; ambiguous autumn times require explicit offset. Do not assume every day is 24 hours for policy-day proration: calculate using contractual local calendar dates.

For effective time E and knowledge cutoff K: select issued versions for the selected term where EffectiveAt<=E and ProcessedAt<=K, ordered by EffectiveAt descending then transaction Sequence descending. No version before first issue means not yet covered. Cancellation at/before E returns cancelled cover, not the prior active version. An expired term returns historical data labelled expired, not active insurance. Querying processing basis alone uses E=K unless an explicit effectiveAt is also supplied. Default K is the current clock.

Example: v1 effective 1 January, processed 20 December prior year. v2 effective 1 July but processed 10 July. On 5 July using knowledge 5 July show v1; using knowledge 1 September show v2. v3 drafted 11 July is excluded. v4 effective 1 October but processed 1 August does not change September cover. Same-effective-time corrections use processing sequence but cannot mutate an earlier version's bytes.

Initial MTA effective-date rule: within the selected term and at or after the latest issued effective change; ordinary backdating within that boundary requires senior authority/reason. A pending future-effective transaction therefore blocks an earlier-effective new MTA until resolved through a supported operation. Out-of-sequence rebasing and reinstatement are explicit unsupported operations in v1.

## Renewal

Prepare renewal from the version applicable at the expiring term end, not an arbitrary latest/current version. New term starts exactly at old EndsAt, default 12 local calendar months; 6-month Motor Trade proposal is supported when configured. Leap-day anniversaries clamp to the last valid day of the target month. Product/binder version must cover inception. Do not create overlapping terms.

Preparation → rated/referred → invited → accepted → issued. Invitation due defaults to 45 days before expiry. Changing terms invalidates rating and acceptance. Acceptance after expiry must not fabricate uninterrupted cover; require supported inception/authority or explicit lapse. Lapse creates history/reason/notification without a new active term. Auto-lapse timing is configurable; its command deduplicates by term/event.

## Cancellation

Prepare → review/rate → required approval → issue. Validate permitted reason, effective time, notice and authority. User sees financial effect before issuing. A cancellation effective in future is scheduled until then; active status selection follows LIFE-01. Issue atomically creates the cancellation version/transaction/journal and refund obligation, abandons conflicting drafts, closes applicable tasks with `policy-cancelled` reason and queues notice/certificate-withdrawal/MID work. Keep task exceptions that must continue, e.g. unpaid refund, clearly open.

Cancellation amounts follow FINANCIAL-EXAMPLES.md. No refund payment occurs during cancellation commit. Failed payment leaves the policy cancellation intact and creates a finance exception. A duplicate cancellation operation returns the existing result; a second different operation against an already cancelled base is rejected.

## Finance and jobs

Refund: pending-approval → approved/rejected → payment-queued → paid/failed. Require a separate eligible approver and configured refund authority. Retry failed payment under the same payment operation key. Partial provider success/unknown timeout requires querying/reconciling the adapter result before another payment.

Receipt allocation checks both receipt residual and invoice residual under aggregate locks. Unallocation appends a reversal. Journal posting requires balanced signed effects represented as positive debit/credit lines, open accounting period and unique operation key. Period close blocks ordinary later posting to that period; adjustments post to the next open period while retaining effective date and original reference.

Bordereau: draft → validating → invalid/ready → queued → submitted/failed. Edits/exclusions return to validation; excluded rows require reasons. Submitted rows/hash/file are immutable. A correction creates a new linked batch/version, not an edited submission. Included errors block submit.

Outbox: pending → leased → succeeded or failed/retry-due. Lease expiry recovers crashed workers. Backoff and maximum attempts are configured; exhausted jobs create one exception task. Adapter requests/results are persistent deterministic scenarios. Business operation identity survives idempotency-cache expiration.

## Validation evidence

`tests/policy-contracts.test.mjs` proves structural schemas, selected money/date/version and prerequisite examples. It does not prove SQL atomicity, leases, identity or production rating. Future .NET unit/integration suites must adopt these examples plus detailed phase cases. Structural schema acceptance alone never authorises issue: product question-set completeness, reference membership, same-policy risk links, dates and permissions are domain checks.
