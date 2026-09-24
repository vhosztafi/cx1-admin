# Phase 10 research — accounting and insurer reporting

Researched 2026-09-24 from the retained Phase 9 finance handoff, current .NET/SQL implementation, approved data and API contracts, and the decoded original accounting prototype. This is implementation guidance, not evidence that finance behavior exists.

## Existing boundary and recommended architecture

`IssueFinancialObligation`, components and `JournalLine` already pin policy, term, transaction, agency, relationship, provider, historical terms and exact decimal movements. First issue journals are posted without `AccountingPeriodId`/`PostingDate`; servicing and cancellation use `AccountingPeriods.HoldAsync` under an issue transaction. The journal schema requires a source insurance component, so cash must use its own additive, immutable posting records and a unified read projection. Do not fabricate a policy transaction for a receipt or loosen existing journal constraints. Keep one source identity per projected movement and prove it appears once.

For new first issue, hold an open accounting period before journal sealing and persist the period/posting date in the already supported non-null shape. For legacy first issue, use the original `PostedAt` converted to London date as an explicit historical projection; do not update sealed rows. A future closed-period correction posts a new record in the earliest eligible open period, links the original source and carries an explicit reason. `AccountingPeriods.HoldAsync` lock order is the canonical posting/closure boundary. Annual seeded accounting periods remain, while statement windows can be monthly or custom.

Use SQL Server transaction locks plus uniqueness and database constraints for multi-row residuals. A mutable ETag alone cannot prevent concurrent over-allocation or double reservation. Retain current authorization before command receipt replay, return canonical decimal money strings, and perform browser arithmetic in integer pence. Individual finance amounts must fit the saved decimal(15,2) envelope; checked aggregate conversions must reject overflow rather than truncate. Preserve original posted evidence and append reversals.

## Source and requirement map

The decoded `pAccounting()` at `docs/design/source/prototype-template.txt:2824` has seven tabs, a dynamic batch detail tab, overview KPIs and two toast-only page actions. The initial `control-inventory.json` assigns 24 direct accounting controls; the Phase 9 handoff identifies display-only and implied obligations beyond that count. Inventory every tab, action, link, status and display occurrence, including inherited `CTL-e834b4734421` agency account entry and Phase 11's `CTL-f68f49a17b06` admin entry destination. Do not mark a whole tab covered based on a single click. The original fixed amounts, insurer labels, August 2026 dates and £250 threshold are demo illustrations, not persisted truth.

| Requirement | Implementation contract | Key negative proof |
| --- | --- | --- |
| FIN-01 | Scoped, paged ledger from exactly one posted insurance/cash/adjustment source per row; debtor and provider balances, effective versus posting dates, policy transaction drill-through and honest invoice/payment status | Legacy first issue included; foreign agency hidden before counts; no posted row duplicated; missing cash never labelled paid |
| FIN-02 | Agency statement snapshot with agency/terms identity, from-inclusive/to-exclusive window, opening + signed movements = closing, immutable bytes/hash and current download authority | Empty period with nonzero opening, backdated effective date, changed terms, credit closing and revoked download |
| FIN-03 | Persistent receipt/payer, allocation and reversal identities, held source/invoice/receipt residuals, ordered locks and whole-command rollback | Two distinct keys race final penny; same key replays; cross-allocation deadlock avoided; repeated reversal rejected |
| FIN-04 | Immutable bank import identity, candidate duplicate evidence, partial matching/reversal and reasoned exclusion/explanation audit | Same amount/date/reference distinct import remains distinct; matched line cannot be excluded; opposite sign/currency rejected |
| FIN-05 | Collected-credit entitlement and payee provenance, amount reservation, versioned approval limit, separate approver, durable demo payment operation/response/application | Unpaid credit yields zero refundable cash; two requests compete; definite failure, uncertain transport and crash replay never double pay |
| FIN-06 | Provider/period batch with frozen source membership and values, exact version, correction/exclusion events, validation and immutable CSV hash/bytes | Source changes after snapshot; invalid version cannot export as valid/submit; CSV quoting/formula protection and signed numeric fidelity |
| FIN-07 | Exact validated-version submission through persistent deterministic adapter with operation history and current scope before replay | Changed intent under key rejected; timeout after success and local apply crash recover same provider effect |
| FIN-08 | Period close under same posting lock; snapshot of prerequisites, unexplained exceptions block closure; later correcting entries in open period | Close/post race; sealed history immutable; later correction carries original source and reason |

`POL-01` also needs a real linked policy finance view. The agency page's Open in Accounting link must carry the selected agency identity. The Admin bordereaux link should land on a real Phase 10 destination while its source owner remains Phase 11.

## Data and API decisions for the planner

Use the entity names and relationships proposed in `docs/design/DATA-MODEL.md` (Receipt, Allocation, Refund, PaymentSettlement, BankLine, BankLineExclusion, Reconciliation, ReconciliationMatch, BordereauBatch/Row/RowCorrection) unless a contract records a specific compatibility refinement. Persist operation identities indefinitely with business rows. A receipt is a cash fact; an allocation applies it to an invoice; a bank match reconciles that cash fact to an imported line. Neither operation creates the original insurance premium. The same movement must not enter an agency balance twice.

Invoice due dates pin the applicable historical `AgencyTermsVersion` fields. Distinguish debtor agency from debtor relationship and net remittance from separate payment. Receipt payer reassignment must not silently move a historical obligation across agencies. A cancellation credit may offset debt before any cash refund is considered. Cash entitlement needs provenance to collected funds and a reservation to prevent later competing approvals. Approval authority is a versioned demo rule, not an FCA/compliance statement. Payment queueing is not payment completion; provider outcome and local posting/application are separate recoverable states.

Bank matching is signed and currency exact. An explained variance remains visible and auditable rather than being filled with a synthetic journal. Exclusion requires reason and immutable original line. Bordereau correction can alter export mapping fields only; contractual premium/tax/commission must remain pinned. Exported CSV needs stable column order, RFC-style quoting and explicit handling of untrusted spreadsheet formula prefixes. Test spreadsheet open/save behavior only to the level actually implemented; do not claim a universal safety guarantee.

## Validation Architecture

Use existing `BackOffice.UnitTests`, native SQL Server `BackOffice.IntegrationTests`, `apps/backoffice/tests`, root/frontend checks and browser collectors. Each implementation slice writes meaningful business-rule tests before code. Focused filters must discover and execute nonzero tests. SQL tests must exercise real uniqueness, locks, concurrent distinct keys, rollback, current scope and process restart; pure mocks do not establish the key finance invariants. Final acceptance compares discovered and executed unique test names, zero skips and current source/assembly signatures; run the full costly SQL gate once after the last product change. Use `scripts/assert-test-results.ps1` in a child PowerShell. Retained demo database, file root and data protection keys are preserved through additive initialization/restart.

| Family | Fast feedback | Full evidence |
| --- | --- | --- |
| Contracts/source | Node tests over exact DTOs/source ledger | Reconcile each original accounting control/display and inherited caller |
| Ledger/period | Unit formulas/precision/temporal cases | Native SQL legacy/new posting, period close races, policy drill-through |
| Cash/reconciliation | Unit residual state machines | Native SQL concurrent allocation, reversal, duplicate import, scope/retry |
| Refund/adapter | Unit entitlement/authority | Native SQL lost-response/crash/retry/reservation and browser outcome |
| Bordereau | Unit mapping/CSV/validation | Native SQL version immutability, provider replay and downloaded bytes |
| UI | Component and API client tests | Live saved-record browser flows at desktop and 390px/200% zoom |

## Risks and phase boundaries

High: over-allocation, double cash refund, missing legacy first issue, false period closure, duplicated ledger movement, external agency disclosure, invalid insurer submission. Plans must name each threat and pair it with a negative test. There are no real bank transfers, insurer transmissions or production accounting certifications. Phase 11 still owns configuration editing, Phase 12 broad reporting, and Phase 13 business/assistive-technology UAT. This phase may seed immutable demo rule/scenario versions needed for its own flows, but should not build a generic administration console.
