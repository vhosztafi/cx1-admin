# Phase 7 research: policy lifecycle and history

Researched: 2026-09-17. Status: architecture investigation in progress.
This is research evidence, not an approved implementation plan or completed gate.
Read 07-CONTEXT.md and 06-PHASE07-HANDOFF.md alongside this document.

## Findings and recommended boundaries

Keep SQL Server, the pinned .NET 10/EF Core 10 packages, and versioned JSON.
No database or framework replacement is needed. The hard problems are business
chronology, explicit provenance, signed financial movements and atomic authority.

### Business time must remain explicit

SQL system-versioned tables record database transaction time. They do not supply
the independent insurance effective time needed for a future adjustment or a
backdated change. Keep immutable PolicyVersion/PolicyTransaction records with
EffectiveAt and ProcessedAt; select by both cutoffs. This recommendation is an
application inference from [Microsoft's temporal-table semantics](https://learn.microsoft.com/en-us/sql/relational-databases/tables/temporal/overview?view=sql-server-ver17).

Local inspection: PolicyReadService currently defaults to CurrentTermId and
CurrentVersionId. PolicyIssueWriter sets those pointers during first issue.
Those paths are correct only for the existing first-issue boundary. Before any
future servicing issue, reads and discovery need a shared temporal selector.
Current pointers may remain navigation hints; they cannot decide today's cover.
Choose the applicable term at effective cutoff E and processing cutoff K, then
the last issued slice with EffectiveAt <= E and ProcessedAt <= K, using the
documented transaction-sequence tie-break. Term existence itself must be visible
at K. Use half-open cover intervals; cancellation is an explicit outcome, never
an empty risk object masquerading as active cover.

Renewal preparation selects the last applicable slice strictly before the old
exclusive term end. Do not implement this by subtracting a guessed millisecond.
Queries should express the strict boundary directly. Include cancellation when
deciding whether an expiring term is eligible to renew.

### Editing lease and optimistic concurrency solve different problems

A five-minute renewable lease expresses editor ownership. Rowversion/ETag
protects against stale writes even by the lease owner. Every save checks both;
takeover must advance the lease generation so an old tab cannot regain authority
with a delayed heartbeat. Never keep a SQL transaction open for the user's edit
session. SQL transactions cover individual commands, with consistent policy and
draft lock ordering. EF supports rowversion concurrency checks; a failed check
must become a recoverable conflict, not an unconditional retry that overwrites
the new revision. [EF Core concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).

Issue must hold one transaction across policy graph, financial obligation,
balanced journal, request/outbox records, audit and idempotency receipt. Existing
PolicyIssueWriter already requires a held transaction and makes multiple saves.
Reuse that boundary pattern. Do not assume separate SaveChanges calls commit
together without an enclosing transaction. [EF Core transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions).

### Preserve quote provenance; introduce a servicing-owned decision graph

Local PolicyModel requires every transaction to have QuoteRevisionId, CycleId,
RatingId and AcceptanceId with same-quote compound FKs. UnderwritingCycle,
QuoteRatingResult and QuoteReferral also require QuoteId. A servicing draft cannot
use these tables by inventing a quote or reopening the bound source quote.

Recommended design for the detailed data contract:

- ServicingDraft owns policy, existing/base term, immutable base version, kind,
  current revision, lease generation and lifecycle state.
- ServicingRevision is append-only and contains proposed risk, dated schedule,
  reason, local-time intent and a deterministic input hash.
- ServicingDecisionCycle owns its exact revision/base/configuration pins. Its
  rating, referrals, decisions, evidence associations, terms, delivery and
  acceptance belong to this cycle through compound FKs.
- Reuse pure QuoteRatingRules, term validation, money and authority evaluation
  through typed inputs. Keep persistence orchestration subject-specific in v1;
  avoid a repository-wide polymorphic rewrite of the verified quote graph.
- Extend PolicyTransaction with explicit servicing provenance. Existing quote
  origin remains available, but quote decision columns become nullable only for
  servicing kinds. A kind-dependent SQL check requires exactly the appropriate
  complete provenance set. New business retains all current quote FKs; servicing
  references a same-policy issued servicing decision. Cancellation approval is
  its own valid provenance, not fabricated rating or acceptance.

The final schema must specify the exact one-of constraints, issue-time linkage,
and immutable guards before generating a migration. This is a recommendation,
not permission to weaken existing owner checks.

### Signed movements need deliberate storage evolution

IssueFinancialModel currently rejects negative obligation amounts and component
amounts; Purpose is first-issue only. IssuePostingRules explicitly accepts only
nonnegative exact pennies. Passing negative cancellation values through this
calculator will fail; removing the checks without replacement would be unsafe.

Prefer explicit signed component movements with purpose-specific validation,
original-component lineage and coverage intervals. Journal debit and credit
amounts stay positive: a return reverses sides. Preserve the first-issue rules
for first-issue rows, and make MTA/cancellation rules explicit. Cancellation must
reverse unearned movements over each original component's interval, including
earlier positive and negative MTAs, without reversing any part twice. A positive
fee and negative premium can coexist; net sign alone cannot determine all lines.

The detailed contract must resolve multiple same-code components within one
transaction. Current JournalLine's compound component FK includes transaction
and code; multi-slice rating may create several premium components with distinct
intervals. Inspect all keys and SQL triggers before choosing an ordinal/key
extension. Keep the original source component separate from the new movement
component referenced by the new journal line.

Use FINANCIAL-EXAMPLES.md as the business arithmetic authority. No external
insurance, legal or tax rule is inferred. Refund obligation does not prove cash
payment. Posting-period design needs an explicit minimal open-period boundary
compatible with Phase 10; do not label a date field as closed-period enforcement.

### Dates and current permissions

Use existing QuoteTerm conventions and London local intent. Validate invalid
local times and ambiguous offsets before UTC conversion; DateTimeKind must be
handled deliberately. [.NET TimeZoneInfo ambiguity documentation](https://learn.microsoft.com/en-us/dotnet/api/system.timezoneinfo.isambiguoustime?view=net-10.0).
Backdating, takeover and cancellation approval use current grants under lock,
including before successful idempotency replay. Processing chronology comes
from the configured trusted clock, never a client-supplied processed timestamp.

### Renewal experience and notifications

Prototype UW-31 must use actual persisted supplied experience, with source,
observation period, premium denominator, paid/outstanding amounts, recorder and
evidence reference. Missing experience is unknown, not zero losses. Define the
demonstration rule for missing/zero-denominator cases explicitly before coding.
Do not invent the prototype's fixed loss ratio or import a future claims module.

Invitation and lapse use persistent deterministic delivery adapters. A queued
outbox item means pending; only an applied delivery permits the invited state.
Acceptance is separately recorded against exact current terms. Cancellation
notice and document/MID intents remain distinct persisted obligations.

## Validation Architecture

Retain the verified Phase 6 baseline: 838 backend tests, including 169 real SQL,
104 frontend, 334 contract/source/gate tests, 37 prior browser journeys plus
carrier issue/discovery, restart and 44-set preservation. These are prior-phase
results; no Phase 7 production behavior has been tested yet.

Pure tests must cover effective/processing cutoffs, future slices, equal-time
ordering, local DST gap/fold, anniversary/leap boundaries, all worked financial
examples, negative/mixed movements and stable-ID diffs. SQL tests must cover
compound ownership, provenance one-of rules, rollback at every write boundary,
lease races, issue versus cancellation, stale base, revoked authority before
replay, duplicate worker delivery and upgrade preservation of old JSON hashes.

Browser evidence must exercise persisted reload/resume, lost lease with local
edits retained, conflict recovery, both-product MTA, multiple cover dates,
renewal invitation/acceptance/issue, lapse, cancellation and historical comparison.
Test future changes against current discovery and agency-safe projections.
Use fresh TRX directories and actual-start cutoffs. Full SQL regression previously
took about 37 minutes; do not substitute a skipped or filtered run for that gate.
Human business and assistive-technology UAT remain separate unperformed work.

## Remaining research and planning gates

1. Complete source audit: 137 originally assigned controls across pPolicy(62),
   pMta(24), pRenewal(14), pCancelReview(5), pIssued(2), pVehicle(2), pDriver(2),
   pAsAt(6), modalVals(20), plus conditional branches and display fields. Map
   real implementation owners; POL-01 retains Phase 9/10 compound portions.
2. Inspect existing financial keys/triggers, evidence/capacity service boundaries,
   all design-only term/draft endpoints and source data shapes. The initial scout
   matched policy-prefixed paths only and is not a complete API inventory.
3. Write explicit data/API design, financial examples for mixed multi-date MTA,
   state machines, migration strategy and source-to-plan traceability.
4. Produce UI-SPEC, validation task map and bounded sequential plans, then run
   plan, requirement and decision coverage checks. No plan is ready to execute yet.

Research was performed inline under the GSD skill's Codex adapter. No agent,
production edit, database mutation or live external delivery was used.
