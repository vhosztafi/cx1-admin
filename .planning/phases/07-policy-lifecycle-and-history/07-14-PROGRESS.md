# 07-14 — Atomic cancellation, execution in progress

07-13 is complete in commit0f9e2ff. Continue inline; no new user command needed.

## Implemented prerequisite

- `issued-cancellation-1` is a separate closed immutable snapshot format with
  explicit cancellation outcome and preview/approval/issue decision provenance.
- Snapshot builder retains insured, term, risk, cover and cumulative historical
  premium declarations. A separate financial obligation will own the credit.
- Both-product tests first failed for the absent format/builder, then passed.
  `.local/phase7-14-builder/unit.trx`:13 passed, zero skipped, including existing
  issued-policy and servicing regression. These are prerequisite unit checks,
  not proof of atomic cancellation issue.
- Regenerating contracts also reconciles the existing generator's issued/lapsed
  servicing read states; no sales funnel change.
- Additive migration20260919060215_CancellationIssueStorage introduces an immutable
  decision linked to the exact approval/draft/revision/hash, owned consequence
  intents and deterministic notice receipts. No cancellation transaction is yet
  enabled. Source guards require the current base, permitted current issuing and
  approving grants, correct role/separation and no later issued term.
- Native SQL storage test first failed for the absent mapped entity, then exposed
  a binary/default collation mismatch in the consequence operation comparison.
  Corrected test passes in `.local/phase7-14-storage-v2/sql.trx`: one real SQL case,
  no skips, including wrong approval, duplicate and immutable-decision checks.
-17 servicing contract/source checks pass in `.local/phase7-14-contracts.log`.
  Three existing cancellation-review SQL cases also pass. The clean foundation
  gate in `.local/phase7-14-foundation` verifies17 cases (13unit+4SQL), no skips.
  EF reports no pending model changes. These checks do not complete plan07-14.

## Required next work — not complete

Implement current issue authority before replay, exact current approval/preview
reassessment under shared policy/draft locks, owned immutable decision and SQL
provenance alternatives, cancellation version/transaction and signed posting.
Extend independently derived SQL movement checks with original component IDs and
single reversal protection. Persist notice, certificate withdrawal, MID and task
close intents atomically; apply only the owned deterministic demo notice worker.
Abandon conflicting drafts and fence late work, then verify races, rollback,
scheduled/effective reads, current-scope retry, real browser and restart.

SQL integration points inspected: PolicyModel, ServicingIssueModel,
TR_PolicyTransaction_Source, TR_PolicyVersion_Source, TR_ServicingDraft_Issued,
TR_IssueFinancialObligation_Source, TR_IssueFinancialComponent_Source,
ServicingExpectedPostingMovement and journal sealing. Preserve existing branches
and use a new migration, without rewriting historical migration files.

Existing SQL definitions for those six objects were captured read-only from the
verified demo in `.local/phase7-14-graph-baselines.json`. Use them to preserve the
old branches in the next graph migration. PolicyReadService currently throws on
missing sourceCycleId/ratingId/acceptanceId: cancellation must expose its own
preview/approval/issue decision IDs instead. Extend its closed API read shape and
the frontend reader; do not invent quote/rating acceptance provenance.

Current demo remains the verified07-13 Release build, API5087/web3100, hidden
process IDs in `.local/phase7-13-preview-pids.json`.123 prior table/setting hashes
were unchanged. No07-14 migration or live issue endpoint has been applied yet.
