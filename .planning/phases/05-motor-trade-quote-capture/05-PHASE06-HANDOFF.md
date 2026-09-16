# Phase6 handoff — capture boundary

Phase5 implements Motor Trade capture, not a rated quote or policy. Consume final05-VERIFICATION/05-11-SUMMARY before treating acceptance as complete. All sales-funnel files remain read-only.

## Authoritative persisted snapshot

Quote holds agency/client/relationship, current revision, ETag and capture closure. QuoteRevision is append-only and retains its own client/relationship, product version, agency terms, question/reference pins, proposal JSON and content hash. Matching reassociation can preserve proposal bytes/hash while changing revision identity and ownership. Therefore a content hash or quote ID alone is insufficient for rating/acceptance validity. Use QuoteLifecycleRules.MatchesCurrent with quote/revision/hash and also bind product/terms/configuration and trusted ownership to each result.

Every progression command must use current identity/capability and agency state before a successful receipt replay, then fresh ETag/state checks before a new effect. Retain the established agency → actor → intake/review (if any) → quote → client/relationship ordering; acquire multiple agency locks in deterministic order. Re-read current review state and eligibility under the transaction. Capture readiness is a read model, not a durable permission. Never accept a client-supplied ready/verified/accepted/rated flag.

Close capture atomically with the downstream progression effect by setting CaptureClosedAt and reason while holding the same agency/quote fence used by withdrawal/matching. Existing tests race fresh match decisions with closure and verify historical successful-receipt replay still reauthorizes access. Phase6 must exercise the real rating/submission command through that fence and define which transitions reopen editing. Never simply clear closure to bypass an existing decision.

## Evidence and adapters

Vehicle capture modes come only from stored selected/manual lookup decisions whose input fingerprint matches current vehicle data. Evidence sufficiency comes from stored accepted file/attachment and current requirement fingerprint, with withdrawal respected. Relevant edits can invalidate these projections without deleting their history. Clone transfers no evidence, lookup or review state; child IDs and internal links are remapped. Demo adapters make no provider calls. Preserve outbox/inbox/lease and exact-retry patterns for future services.

## Remaining compound requirements

- QUO-01 and CLI-01: actual policy discovery/client links require real issued policy IDs; only quote portions are supplied here.
- QUO-03: user-authored capture declarations/limits are distinct from applied endorsements/warranties.05-01 explicitly excluded authoritative cover sections/endorsements/warranties from quote writes; Phase6 must materialize and display real underwriting/rating outcomes.
- QUO-06: revision/clone/withdraw and invalidation tokens exist; actual rating/acceptance invalidation needs real Phase6 records and tests.
- AGY-04: common own-agency quote summaries exist; policies and tasks remain Phase6/9. Do not add hidden risk/registration search or counts to the safe projection.

Capture uses explicit capture-enabled configuration and effective agency distribution eligibility. Original product metadata may still be foundation-only with ratingAvailable=false. Phase6 must implement actual product/rating configuration; do not relabel the seed as rated-ready. Resolve the business rule for applying current versus retained agency commercial terms explicitly, and bind the selected terms to the immutable rating result.

## Regression obligations

Retain all17quote browser journeys and all20prior agency journeys, including actual own-agency cookies, role revocation, cross-scope/cursor denial, exact retries, rollback and race coverage. The three agency terms UI fixture journeys do not replace real terms publication tests. Future database migrations must preserve retained JSON/ownership, evidence bytes and adapter attempts. Human business/assistive-technology UAT, hosted CI and Docker execution are separate evidence; native SQL/Chrome passes do not imply them.
