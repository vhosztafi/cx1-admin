# Finance handoff — provisional while Phase 9 verification runs

Prepared from the implementation on 22 September 2026. This records existing integration boundaries for Phase 10 research and planning; it does not approve a finance design or mark Phase 9 complete.

## Existing money and identity sources

`backend/src/BackOffice.Infrastructure/Persistence/IssueFinancialRecords.cs` contains the relational obligation, component, journal and journal-line records. An obligation pins policy, term, transaction, agency, client relationship, provider and agency-terms version. It records currency, settlement mode, debtor and exact decimal premium/tax/fee/commission/share and payable totals. Consume these saved identities and amounts rather than recalculating historical balances from current product rates or agency terms.

Components retain coverage intervals and optional `OriginalComponentId` for reversals. Journal lines pin their component, transaction, account and party; posted journals retain posting date and accounting period. `ServicingPostingService.WriteAsync` requires the caller's transaction, derives adjustment/renewal movements from saved lifecycle evidence, holds the accounting period and seals the journal after its lines. `CancellationPostingService` consumes `CancellationExpectedReturnMovement` and original component identities. These are posted insurance movements, not bank receipts or cash refunds.

`AccountingPeriod` currently has start/end dates and state. Phase 10 must inspect the existing `AccountingPeriods.HoldAsync` and SQL posting constraints before designing period closure and later adjustments. Closing a period must not enable edits to already posted component amounts or original policy snapshots.

## Phase 9 boundaries to preserve

- Administrator paid, reserve, incurred and recovery figures are read-only reported claims facts. They are not cash-ledger entries, settlement instructions or liability decisions. Missing amounts stay unknown.
- Incident underwriting contribution uses one reported incident and the exact selected policy version's written premium. It is explicitly not an earned or aggregate loss ratio. Finance reporting must not relabel it.
- Documents pin immutable policy/template/file versions. Agency delivery pins exact recipients and versions. A statement or bordereau export should receive its own immutable identity and content hash; do not repurpose or overwrite a policy PDF.
- Tasks and correspondence retain their real subject and current authorization. Completing a task or sending an information request does not approve an insurance or finance transaction.
- Cancellation document withdrawal, MID consequences and operational task closure do not execute a cash refund. The retained commercial cancellation records a credit with cash paid zero.
- Persistent deterministic adapters are mandatory for payment/submission outcomes. Preserve operation identity across uncertain transport responses, scope before replay, and independent retry/application states. No real payments, notifications or provider submission.

## Required planning decisions

| Requirement | Design work still required in Phase 10 |
| --- | --- |
| FIN-01 | Read model over posted movements, debtor/agency scope, signed balances and transaction drill-through. |
| FIN-02 | Period statement opening/movement/closing equations, immutable generation inputs and download authority. |
| FIN-03 | Receipt/payer identities, allocation and reversal records, residuals, concurrent allocation protection and over-allocation rejection. |
| FIN-04 | Bank-line import/demo identity, match/reconcile/unmatch rules and reasoned variance audit. |
| FIN-05 | Refund entitlement separate from credit, approval limits/separation of duties, payment operation and retry-safe final outcome. |
| FIN-06 | Bordereau snapshot/version membership, validation, corrections/exclusion reasons and stable CSV output. |
| FIN-07 | Valid-version-only submission, idempotency and persistent deterministic adapter history. |
| FIN-08 | Period lock ordering, closure prerequisites and later correcting journals without rewriting posted history. |

POL-01 remains partial until its linked finance view is implemented and verified. CC-05 must only be completed after Phase 9's actual operational verification, not from this handoff. The original prototype remains the source inventory for finance UI controls; this document does not reduce that inventory. Human business and assistive-technology UAT remains unperformed.
