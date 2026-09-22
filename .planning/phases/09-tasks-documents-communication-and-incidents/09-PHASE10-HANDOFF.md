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

## Source census preparation

Read-only comparison while Phase 9 regression runs found **24 controls** assigned to Phase 10 in `docs/design/control-inventory.json`, all under `pAccounting` and all initially tagged FIN-01. This is a discovery baseline, not a complete finance denominator or requirement mapping. The decoded original `docs/design/source/prototype-template.txt:2824` also contains disabled, display-only and implied behavior that must be inventoried explicitly during Phase 10 planning.

| Original surface | Additional source obligations to preserve |
| --- | --- |
| Overview | Written premium/tax/fees/commission/net-to-insurer; cash in period; closing/overdue agency balances; earned premium on the stated pro-rata monthly basis; ageing buckets; scoped KPI and attention-row navigation. Define each calculation and time basis before rendering. |
| Transactions | Actual insurance movement and policy drill-through; effective versus posted date; signed components; invoice/paid/credited/write-off states require real persisted finance evidence. A posting alone does not establish paid status. |
| Broker accounts | Current and historical settlement terms, due/overdue balances and last receipt; selected agency/period statement with opening + debits − credits = closing. Do not copy the fixed Brightside statement. |
| Payments | Receipt details including the display-only View action, explicit payer assignment, match/allocation/unallocation and residual cash. Navigation must retain selected receipt/invoice identities. |
| Reconciliation | Bank-line identity and duplicate evidence; actual receipts, refunds, insurer settlements and fee drawings; explain/exclude/resolve with audit. Do not manufacture a balancing number to clear the banner. |
| Bordereaux | Prior submitted downloads, batch detail, all failing-row correction actions (including link-styled cells without handlers), repeat validation, reasoned exclusions, CSV, and disabled Submit becoming available only for a valid exact version. |
| Refunds | The final `else` branch renders refund records and audit history. Approval/payment actions are implied by the approved requirement even though the source has no clickable approval button. The displayed £250 second-approval threshold is a prototype demo value requiring explicit versioned rules, not a production compliance assertion. |
| Journal / period | Post journal and Export period are toast-only in the prototype and require actual authorized persisted behavior. Closed-period banner implies a real closure state and correction workflow; FIN-08 cannot be omitted because there is no source close button. |

Inherited entry `CTL-e834b4734421` is **pAgency → Open in Accounting**, historically owned by Phase 4 but explicitly handed to Phase 10 in AGENCY-SOURCE-COVERAGE. It must open the selected scoped agency account. `CTL-f68f49a17b06` (**pAdmin → Open bordereaux**) retains Phase 11's configuration owner; its Phase 10 destination must nevertheless be real. Policy finance and reporting's Finance-category navigation are additional cross-phase callers to reconcile. Keep separate raw source identities rather than assigning every accounting action to FIN-01 or treating an entire tab as verified from one click.

No finance source inventory was regenerated, no finance code or schema was changed, and no Phase 10 plan was approved by this preparation.

## Verified implementation constraints for finance planning

Read-only review during the frozen v4 gate found material differences between the original Phase1 illustrative model and actual Phase6–8 storage. Treat these as required design inputs, not an approved finance migration:

- `Policies/PolicyIssueWriter.cs` creates and seals a first-issue Journal with PostedAt, but without AccountingPeriodId/PostingDate. `Persistence/IssueFinancialModel.cs` explicitly allows that legacy shape. Servicing/cancellation journals pin a held period and posting date. A period-filtered ledger must include those genuine first issues using an explicitly documented historical time basis; an inner join to AccountingPeriod would silently omit them. Posted journals reject later updates/deletes, so do not backfill period columns by weakening their immutable-history guards. Choose and test an additive association or explicit historical projection, and require period locking for newly issued transactions once finance closure exists.
- `Policies/AccountingPeriods.HoldAsync` requires an existing transaction, converts the processing instant to London date, locks the earliest open period whose end is later than that date, and advances the posting date to the period start if necessary. It deliberately differs from contractual effective date. Closure must share that lock ordering; tests must exercise closure racing first issue/servicing and a correction after a closed period. `ServicingAccountingPeriodGuards` forbids boundary/creation-identity edits/deletes and overlapping ranges. Existing seeded annual periods are deliberate retained configuration; do not replace them with monthly periods merely because the prototype filters by month.
- The actual Journal and JournalLine are insurance-obligation records with required TransactionId/ObligationId/SourceComponentId links, GBP currency and a closed account-code set (agency/relationship receivable, insurer payable, fee income, broker remuneration payable). The original generic Journal design cannot be implemented by inserting cash or manual entries into this schema unchanged. Finance planning must specify compatible separate cash/adjustment records or an explicit additive journal extension with preserved insurance guards, rather than fabricate policy transactions or components for a receipt.
- Existing obligations/components/lines use decimal(15,2), while other prototype monetary contracts permit wider amounts. Preserve original storage and define explicit finance request/sum overflow bounds. API decimal strings and integer minor-unit client arithmetic remain the conventions; do not aggregate balances through JavaScript floating point.
- `DATA-MODEL.md` already proposes receipt, allocation/reversal, refund, bank-line/exclusion, reconciliation and bordereau/correction identities. Reconcile those approved semantics with actual storage during Phase10 contracts instead of silently introducing a second incompatible set of names or dropping reversal provenance.

No financial record, period, source code or schema was changed by this review. Phase10 remains unplanned and depends on successful Phase9 verification.

## Preparatory research and acceptance cases

Research checked22September2026 while the Phase9 gate runs. The choices below are planning recommendations derived from current code and the approved requirements; they are not implemented finance behavior or production accounting certification.

**Ledger extension.** Prefer an additive cash/manual posting boundary and a unified read projection over existing insurance journals, rather than weakening the insurance journal's required source-component relationships. Compare that with a generalized journal migration explicitly during Phase10 planning. Whichever is selected must preserve every existing insurance line and prevent a source movement appearing twice in balances. New first issues can pin period/date using the already allowed non-null shape; historic first-issue dates need a separately documented association/projection. Existing period locking must be used by every new posting path before closure can be considered functional.

**Allocation and reversal.** Define one lock order across payer scope, receipt, invoice/credit and period, then persist amount, original/reversal identity, reason, audit and receipt in one transaction. Test two different keys competing for the same final residual, crossed allocations involving the same two invoices, repeated reversal and allocation racing unallocation. An ETag alone does not serialize all rows participating in a balance. SQL Server update locks persist to transaction completion, while serializable reads can protect predicate ranges; choose supporting indexes and test the actual concurrent SQL behavior rather than assuming ROWLOCK prevents every race. [Microsoft table hints](https://learn.microsoft.com/en-us/sql/t-sql/queries/hints-transact-sql-table?view=sql-server-2016), [transaction isolation](https://learn.microsoft.com/en-us/sql/t-sql/statements/set-transaction-isolation-level-transact-sql?view=sql-server-ver17).

**Historic terms and dates.** Actual agency snapshots contain paymentTermsDays, creditLimit, settlement.statementCycle, premiumCollection and commissionSettlement. Invoice due dates must pin the original terms and a declared processing/statement basis, not use today's agency settings. Monthly/fortnightly statement windows are distinct from the retained annual posting periods. Test an agency terms change after issue, a backdated movement posted later, midnight/DST boundaries, a credit closing balance and an empty period with nonzero opening balance. Opening plus signed period movements must equal closing under the same source cutoff.

**Collected credit and refunds.** Explicitly design credit application and cash entitlement before implementing the Refund table. A cancellation credit alone is not refundable cash. Include examples of unpaid invoice100/credit100 (cash refund0), paid100/credit30 (at most30 before other reservations), paid10/credit100 (offset90unpaid and at most10cash), and competing requests against one remaining collected credit. These are proposed acceptance examples for the chosen source-allocation rules, not a claim that account-wide net balance alone identifies the source receipt or payee. Preserve the source debtor/relationship and current finance authority; finance users cannot approve their own request. Version any demo threshold, including the prototype250second-approval example.

**Payment outcomes.** Queueing reserves an exact obligation amount and pins the demo adapter operation; it does not mark cash paid. Store provider outcome separately from its local journal/application so a lost response or application crash recovers the same payment. Test revoked authority after queue, definite rejection, uncertain transport, replay with changed intent, same-key retry and a second key against reserved funds. An acknowledged provider effect with local application pending must be labelled honestly and must not permit a replacement payment.

**Bank reconciliation.** Keep import identity separate from candidate duplicate evidence. Equal date/reference/amount does not itself prove a duplicate. Reimporting the exact original line identity must not create another line; a reasoned duplicate exclusion must retain both imported records and cannot discard a matched line. Test split matches, reversed matches, attempted double reversal, opposite-sign/currency mismatch and unexplained residuals. A variance explanation is auditable evidence, not an invented balancing entry.

**Precision.** SQL SUM of decimal inputs returns decimal(38,s); that does not imply every result fits the application's Decimal or public monetary contract. Specify bounded individual and aggregate amounts, checked conversions and exact rounding. Keep API money as canonical decimal strings and browser arithmetic in minor units. Test aggregate overflow and signed credit totals rather than allowing truncation or floating-point conversion. [SQL SUM return types](https://learn.microsoft.com/en-us/sql/t-sql/functions/sum-transact-sql?view=sql-server-ver16), [.NET Decimal](https://learn.microsoft.com/en-us/dotnet/api/system.decimal?view=net-10.0).

**Bordereau versions and exports.** Pin membership, source values, corrections/exclusions, validation rules and exact generated bytes/hash in one immutable version. Editing a draft after validation invalidates that result; submission must name the exact validated version. A mapping correction cannot alter contractual premium/tax/commission. Test resubmission recovery, corrected successor to a submitted batch, source data changing after snapshot and download after current permission withdrawal. CSV tests must cover quotes, embedded line breaks and spreadsheet formula prefixes in untrusted text. Ordinary CSV quoting is not a formula-injection defense; select and document the intended spreadsheet-safe representation without corrupting genuine signed numeric values. OWASP also cautions that spreadsheet save/reopen behavior can undo mitigations, so do not promise universal safety. [OWASP CSV injection](https://community.owasp.org/attacks/CSV_Injection).

These cases extend the handoff for formal Phase10 research/contracts/plan checking. No live financial suites, new models or migrations were started alongside Phase9 regression.
