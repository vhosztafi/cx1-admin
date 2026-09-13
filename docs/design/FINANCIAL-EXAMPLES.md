# Financial worked examples v1

All rates are fictional demo configuration, not legal or commercial advice. Currency GBP; decimal arithmetic; round each premium, tax and commission component to two decimal places, halves away from zero. Never use binary floating point for ledger amounts. The API uses decimal strings. Commission here means broker commission; MGA income is the explicit fee. Additional commission splits require a separate defined component.

## Bases

Agency onboarding's “commission basis” selects per-product rates or one flat rate across products. It does not select a gross/net calculation base. Approved agency terms pin the actual basis-point rate, fee-sharing percentage, premium collector and net-remittance versus separate commission settlement. Changing agreed terms requires independent approval; existing policy movements retain original terms. The simple examples below assume zero fee sharing and net remittance; the separate-settlement examples follow them.

Contract terms are half-open local calendar intervals. Prorated premium = annual change × remaining local calendar days / term calendar days. For an MTA use the annualised premium difference, not the full new premium. For cancellation compute the return against the applicable remaining premium exposure, accounting for earlier adjustments; the simple case below has no prior MTA. For a general adjusted term, calculate each effective premium segment and reverse only unearned exposure once; do not simply multiply the original premium after amendments.

Tax = rounded transaction premium × tax rate. Broker commission = rounded transaction premium × commission rate. Gross payable = premium + tax + fee. Net broker due = gross payable − broker commission. Insurer due = premium + tax − broker commission. Fees are retained on cancellation unless the versioned rule explicitly refunds them. Paid amounts and unpaid receivables determine actual cash refund; a credit note is not automatically a full cash payment.

## New business

Term 2026-01-01 through 2027-01-01 (365 days). Premium 1,200.00, tax 12%, broker commission 10%, fee 35.00.

| Component | GBP |
|---|---:|
| Premium | 1,200.00 |
| Tax | 144.00 |
| Fee | 35.00 |
| Broker commission | 120.00 |
| Gross invoice | 1,379.00 |
| Net broker receivable | 1,259.00 |
| Insurer payable | 1,224.00 |

Journal: Dr agency receivable 1,259.00; Cr insurer payable 1,224.00; Cr fee income 35.00. Debits=credits. The gross invoice displays the commission deduction explicitly. Receipt 1,259.00: Dr cash 1,259.00, Cr agency receivable 1,259.00. Allocation links receipt and invoice; do not post the receipt a second time during allocation.

## MTA from 15 September

108 days remain. Annual premium change +600.00; adjustment fee 15.00.

| Component | GBP |
|---|---:|
| Prorated premium | 177.53 |
| Tax | 21.30 |
| Broker commission | 17.75 |
| Fee | 15.00 |
| Gross additional payable | 213.83 |
| Net broker receivable | 196.08 |
| Insurer payable | 181.08 |

Journal: Dr agency receivable 196.08; Cr insurer payable 181.08; Cr fee income 15.00. A return-premium MTA reverses direction; no negative debit/credit line is stored.

## Cancellation from 15 September, unadjusted policy

Return premium −355.07, tax −42.61, commission reversal −35.51, fee return 0.00. Gross credit −397.68, net broker credit −362.17, insurer reversal −362.17. Journal: Dr insurer payable 362.17; Cr agency receivable 362.17. If original invoice is fully paid, the 362.17 customer-account credit can become a refund liability after approval. If unpaid, offset the outstanding receivable first. Retained 35.00 fee is not charged again.

The prototype's cancellation numbers do not consistently reconcile; use these computed fixtures, not copied totals. Reinstatement is not a reversal of cash alone and is outside v1.

For cancellation after adjustments, prorate each original posted premium/tax/commission component over its own coverage interval, round per component per movement, then aggregate. This preserves original rates/rounding and prevents reversing an amount twice. After the above MTA, cancellation on 1 October has 92 days remaining: original premium return 302.47 plus MTA return 151.23 = 453.70; tax return 36.30 + 18.14 = 54.44; commission reversal 30.25 + 15.12 = 45.37; net credit 462.77. Fees remain retained. Store coverage interval and original component lineage on each financial movement. The cancellation operation key prevents a second reversal of the same remaining exposure.

## Receipt allocation and refunds

### Shared fee and separate settlement

On the 1,379.00 gross invoice above, 20% sharing of the 35.00 fee gives a 7.00 broker fee share and 28.00 MGA retained fee. Broker remuneration is 120.00 commission + 7.00 fee share = 127.00. Insurer due remains 1,224.00.

For net agency remittance: Dr agency receivable 1,252.00; Cr insurer payable 1,224.00; Cr retained fee income 28.00. No separate broker payable is created.

For separate commission payment: Dr agency receivable 1,379.00; Cr insurer payable 1,224.00; Cr retained fee income 28.00; Cr broker remuneration payable 127.00. If the MGA collects directly, use the insured/client relationship receivable instead of agency receivable and always keep the broker remuneration payable separate. Distribution agency stays linked for authority/reporting; it is not necessarily the debtor. Paying the 127.00 is Dr broker remuneration payable / Cr cash, with unique payment identity and residual locking. A negative remuneration correction is a clawback receivable, not a negative outgoing payment.

Separate-settlement cancellation in the unadjusted example creates gross debtor credit 397.68 and broker commission clawback 35.51; economic net credit remains 362.17. The retained shared fee is not refunded under the example rule, so no additional 7.00 reversal is created. Whether previously collected cash can be refunded follows the actual debtor/payee account balance; do not pay agency credit to the insured or vice versa.

`settlementAmounts` exercises both settlement modes and direct collection using exact pennies. Relational InvoiceDue is actual debtor due, NetDue is economic net after remuneration, and BrokerRemuneration retains separate obligations. SQL posting and payment-worker verification remain implementation work.

Receipt 1,240.00 allocated 900.00 to invoice A leaves 340.00. A concurrent attempted 400.00 allocation must fail and preserve both residuals. Unallocation appends a reversal; resulting receipt availability becomes 1,240.00 again if no other allocations exist. No allocation may cross agencies.

A 362.17 refund requires eligible second-person approval and one durable payment key. On success: Dr refund/agency credit liability 362.17; Cr cash 362.17. A timeout is uncertain, so query the adapter outcome with the same key before retry. Do not book another payment or decrement credit twice.

## Dates and periods

2024-01-01 to 2025-01-01 has 366 calendar days. Europe/London spring/autumn days can be 23/25 hours; day-based earning still counts each local date once. Invalid/nonexistent local times must be rejected or disambiguated before posting. Same-day cancellation at term start returns full premium under this demo rule; at term end no remaining premium exists and ordinary cancellation is rejected as out of term.

Closed periods cannot be changed. A backdated insurance change processed after close posts to the next open accounting period with original effective date and correction reference. Written-premium reports use processing/posting basis by definition; coverage exposure uses effective date. Earned-premium reports state the segment-based local-day convention explicitly.

## Executable evidence and remaining runtime checks

`scripts/design-rules.mjs` and `tests/policy-contracts.test.mjs` verify full-term/MTA/cancellation rounding, adjusted-policy returns, leap-year and DST calendar-day bases, negative rounding, version selection, issue prerequisites, stale-write/replay decisions and allocation residual decisions. Renewal uses the same full-term calculation with a newly pinned rating and a new term; it never mutates last year's movements. Allocation locking, durable refund idempotency and balanced SQL posting still require implementation tests; these decision examples do not prove database concurrency.
