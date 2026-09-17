# Phase 7 source audit — in progress

Inspected 2026-09-17. This is not a passed source coverage gate.
07-SOURCE-INVENTORY.json preserves all 137 original control IDs and handlers,
and 29 policy/term/draft path-family operations from the existing OpenAPI.
Runtime support is not inferred from the presence of a contract.

## Source-specific obligations found

- pMta (prototype-template.txt:1529) has separate locked, editable, rated,
  unrated, missing-information and carrier-response branches. Issue is conditional
  and absent from the initial 24-control rendered subset. Audit it explicitly.
  The source says 30-minute inactivity; approved LIFECYCLE/07-CONTEXT instead
  require a five-minute renewable lease. Preserve unsaved edits on loss/takeover.
- pMta has per-change dates restricted to cover changes, requested-by selection,
  grouped before/after proposals, edits/removals, re-rating and abandonment.
  The source's fixed dates and names must become actual records and clock values.
- pRenewal (:4214) changes the header command from rate to invitation to
  acceptance to issue; stage navigation must not mutate persisted authority.
  Source checks include loss ratio, delegated authority, fair value and broker
  account. The final design must give each an actual data source and failure
  state. Fixed 64%, 8% loading and February assessment cannot be copied as facts.
- pAsAt (:5108) includes date selection, effective/transaction basis, explicitly
  excluded drafts, applied/not-applied transaction rows, supersession information,
  version-specific documents and export. Date input should support actual dates,
  not just the prototype's change-date dropdown. Unknown dates must not silently
  choose the first version (the source uses Math.max(0,indexOf(...))).
- The policy menu includes Clone to New Quote. It needs explicit Phase7 plan
  coverage using the approved clone rules: fresh risk IDs and no acceptance,
  rating, provider identities, claims flags or private client flags copied.
- Vehicle and driver shortcuts include MID retry, exception/referral task links
  and agency chase. Distinguish servicing evidence/capacity commands from the
  generic Phase9 task/message/MID delivery modules. Keep concrete future owners;
  a disabled placeholder is not completed POL-01 functionality.
- pAsAt export remains a durable reconstruction request bound to E/K, selected
  version and hash until the Phase9 document renderer exists. Do not return a
  successful PDF download or silently export the current version.

## SQL migration hazards confirmed by inspection

| Existing guard/key | Required deliberate change |
| --- | --- |
| PolicyVersion source trigger requires version EffectiveAt = transaction EffectiveAt | New business keeps equality; servicing slices validate against their exact ordered schedule, not arbitrary dates. |
| Quote bound-policy trigger traverses current term/current version and requires new-business | Validate the immutable original first-issue graph independently of current pointers; otherwise later policy servicing can make an unrelated update to the bound quote fail. |
| Component unique index (ObligationId, Code) | Add a component ordinal or explicit movement identity for repeated codes over multiple intervals; preserve old identities. |
| Component trigger requires each component equals the entire obligation code total and full term interval | For servicing, validate each rated movement/interval and validate aggregated obligation totals at sealing. |
| Journal-line trigger requires Debit+Credit = component.Amount and positive-direction account mapping | Signed movements use absolute amount and reversed account sides for negatives, preserving original party/settlement provenance. |
| Posting trigger requires exactly five components and two lines only when amount > 0 | Preserve the rule for first issue; servicing requires its exact movement set, two lines for each nonzero movement, none for zero, aggregate totals and balance. |
| Financial source trigger inner-joins quote cycle/rating | Explicit servicing branch must validate its own required provenance. Nullable quote IDs must not cause a join to silently skip validation. |

Inspected files: PolicyModel.cs, IssueFinancialModel.cs,
FirstPolicyIssueStorageGuards.cs and FirstIssuePostingGuards.cs under
backend/src/BackOffice.Infrastructure/Persistence (guards in Migrations).
These are anticipated extension hazards, not defects in the verified first-issue
scope. Do not edit old migrations; add a new migration with replacement guards.

## Remaining audit work

Map every control to an owning capability and plan; inspect the remaining policy
tabs, servicing modal branches and MTA review/document sections; inventory fields
and implied branches in addition to original control IDs. Complete evidence and
capacity reuse analysis, financial data/API contracts and mixed-date examples.
Then produce UI/validation contracts and plans and run coverage checks. No Phase7
implementation or source-audit completion is claimed by this inventory.
