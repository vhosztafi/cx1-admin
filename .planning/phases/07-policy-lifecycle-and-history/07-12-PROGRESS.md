# 07-12 ongoing execution

Continue inline. No completed summary or requirement closure yet.07-11 completed
in90fd5a7; shared preview/demo remains that verified implementation.

## Invitation and calendar foundation

Twenty pure lifecycle cases pass after initial missing-implementation red:
London45/14calendar-day shifts across DST, ambiguous/nonexistent derived times,
bounded inputs, exact inception cutoff, and accepted/issued/deduplicated lapse
eligibility. Future service/worker must serialize this predicate under SQL locks.

Renewal templates are a separate immutable kind and format. Shared terms service
retains new-format renewal payloads with exact input/rating/preparation/evidence
and contractual amounts; old adjustment payload bytes remain unchanged. Real
delivery and separate evidenced acceptance reuse the existing owned durable graph.
Renewal readiness requires reviewed experience, a passing exact fair-value source,
and supported inception; arrears remains explicitly unavailable.

New additive migration20260918171002_RenewalInvitationTemplates expands template
kind and adds exact-kind source plus renewal evidence/time guards. Historical
migrations unchanged; new baseline freezes prior trigger for reversible upgrade.
Targeted template seed requires a held transaction and is not yet wired to shared
demo initialization. The shared demo has NOT received this new migration/seed.

Two both-product SQL invitation scenarios pass; the broader final run passes
6realSQL scenarios (both renewal cases, existing adjustment terms HTTP cases and
latest-migration storage upgrade/downgrade):.local/phase7-12-invitation-final/sql.trx.
The initial red at the former adjustment-only template guard is retained in
.local/phase7-12-invitation-red.48 API/source checks pass; contract document union
retains old strict schema and adds separate renewal schema.

## Verified new-term issue and invitation UI — 2026-09-18

Renewal issue now uses the existing atomic servicing boundary and creates a new
nonoverlapping PolicyTerm, sequence-one renewal transaction, exact new-term
snapshot, sealed financial posting, three document requests and one MID intent.
The original term/snapshot remain unchanged; current/as-of reads select the old
or new term by inception. New database guards retain the prepared term and its
strict expiring-risk provenance. Original migrations remain untouched.

Real SQL tests cover both products, current authority before receipt replay,
stale ETag/lease/assurance, one-tick late issue, competing issue commands and
injected rollback at thirteen write boundaries (including PolicyTerm and the
command receipt). HTTP checks prove strict DTO/query/header/CSRF handling and
persisted replay. Shared adjustment issue and migration upgrade/downgrade pass.
The final gate has **23 unit + 8 SQL = 31 passing cases, no skips**, in
`.local/phase7-12-issue-final`. Earlier expected red issue-kind rejection is in
`.local/phase7-12-issue-red`; the timeline configuration bound regression red is
`.local/phase7-12-timeline-bounds-red.log`. Timeline accepts the same 0..365 bounds
as versioned settings; unchanged defaults are 45/14 London calendar days.

Renewal workspace now exposes actual invitation preparation, signed proof,
delivery, evidenced acceptance and new-term issue, with separate renewal labels
and current-policy navigation. Existing adjustment controls remain available.
61 API/source contract checks, TypeScript and ESLint checks pass; Release API
build has zero warnings/errors and the production Next.js build passes.

Chrome completed both real UI journeys: six-month Combined and twelve-month
Road Risks, including supplied experience/senior UW-31 review, invitation
delivery, acceptance, issue and 390px containment. Current-policy readback is
unchanged. Evidence: `.local/browser-evidence/renewal-issue/report.json`, finished
2026-09-18T17:44:22.855Z; invitation and mobile issue screenshots inspected.
After API/web restart, both retained issue graphs, version/transaction links and
actual HTTP response contracts pass: `restart-readback.json` in the same folder,
plus `.local/phase7-12-issue-responses`. Browser tests have not exercised lapse.

The shared demo received only the two new additive migrations through
20260918172420_RenewalIssueGraph. Before/after hashes match for the 115 tables in
the existing preservation check (`.local/phase7-12-before.txt` / `after.txt`).
Targeted `--seed-renewal-lifecycle-demo` adds only missing fictional invitation
templates. Browser-created renewal history is intentionally retained. No full
reseed, reset, real email, payment or provider operation occurred.

07-12 remains **in progress**. Still required: durable lapse event/deduplication,
date worker and notification with restart/race proof, due/overdue/invited/accepted/
issued/lapsed timeline and API/UI, and a browser lapse scenario. Also verify
conditional referral targeting for an unchanged renewal (empty change slices).
Continue implementation inline without asking the user for another command.
