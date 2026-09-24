# Finance source controls to saved workspace bindings

This maps the full current-source inventory in `docs/design/finance-source-ledger.json`. A binding means the control opens a scoped saved record or issues a guarded command. Source mock amounts and toast outcomes are not reused.

| Source ID | Binding | State |
| --- | --- | --- |
| CTL-dc74aa65f196 | Overview Export period generates and downloads the exact saved agency statement | bound |
| CTL-af1ae0986051 | Overview Post journal posts a balanced linked correction to an eligible open period | bound |
| CTL-f5d7587f3dea | Overview unmatched receipt row opens its saved receipt ID | bound |
| CTL-6a2ad506c5b6 | Overview bank reconciliation row opens scoped reconciliation | bound |
| CTL-35a9de20c7a1 | Overview bordereau blocker opens provider/period workspace | bound |
| CTL-74e24a6ada25 | Overview refund row opens scoped saved-ID review | partial: no refund list API for a current count |
| CTL-740ea920a861 | Transactions posted-from/to window filters saved posting dates | bound |
| CTL-d29d6b03b3a0 | Agency picker fixes transactions to the selected agency ID | bound |
| CTL-51b829721081 | Transactions posting-state filter uses saved ledger status | bound |
| CTL-51b56a6368c9 | Transactions row 1 opens exact saved transaction ID | bound |
| CTL-33913bc8d7ea | Transactions row 2 opens exact saved transaction ID | bound |
| CTL-c0aa6fc018d4 | Transactions row 3 opens exact saved transaction ID | bound |
| CTL-8f570efb8f02 | Transactions row 4 opens exact saved transaction ID | bound |
| CTL-ba2332ce84de | Transactions row 5 opens exact saved transaction ID | bound |
| CTL-4c1e47f2f86f | Transactions row 6 opens exact saved transaction ID | bound |
| CTL-7c5cc131108a | Payments allocation uses a selected saved invoice candidate and receipt residual | bound |
| CTL-f6945a03e766 | Payments payer assignment saves exact receipt assignment version | bound |
| CTL-afa1f6836cbc | Reconciliation Resolve exposes match, exclusion, variance and reversal records | bound |
| CTL-5f1aa84bef57 | Bordereaux Open batch resolves exact saved batch and version | bound |
| CTL-9722f94fe830 | Bordereaux Re-run validation creates a saved successor version | bound |
| CTL-172f7f829df2 | Every failing member has an independent mapping-only Correct command | bound |
| CTL-704dcdf1dd76 | Export CSV reads immutable exact valid version bytes | bound |
| CTL-935c7ce672a6 | Every failing member has an independent reasoned Exclude row command | bound |
| CTL-7bc8cfc00566 | Back to bordereaux clears selected batch while retaining agency scope | bound |
| CTL-e834b4734421 | Agency record finance entry preserves agency ID and reads scoped saved account | bound |
| CTL-f68f49a17b06 | Admin overview links to bordereaux under current finance authorization | bound |
| CTL-92991f22c2a4 | Policy finance reads policy scope and links to its saved agency account | bound |
| FIN-IMPLIED-overview-metrics | Overview shows exact written premium, tax, fees, commission, net, cash, closing and overdue residual from saved sources | partial: no pinned coverage earning schedule for earned premium |
| FIN-IMPLIED-ageing-buckets | Overview derives current, 1–30, 31–60 and 61+ invoice residual buckets as of selected period end | bound |
| FIN-IMPLIED-transaction-status | Transactions display posted/credit/receivable evidence without inventing paid or write-off state | bound |
| FIN-IMPLIED-receipt-view | Payments View opens selected saved receipt and survives reload | bound |
| FIN-IMPLIED-bank-line-detail | Reconciliation shows imported bank identity, signed residual, matching and explanation | bound |
| FIN-IMPLIED-batch-failure-row-two | Second failed source member saved mapping correction is exercised in live browser with lost-response replay | bound |
| FIN-IMPLIED-batch-failure-row-three | Third failed source member saved exclusion is exercised in live browser | bound |
| FIN-IMPLIED-batch-submit-disabled | Submit is enabled only for exact valid current version with saved content hash | bound |
| FIN-IMPLIED-prior-batch-download | Only current valid or previously submitted valid historical version offers exact download | bound |
| FIN-IMPLIED-refund-approval | Refund detail shows immutable rule, decision chain and linked payment state | bound |
| FIN-IMPLIED-period-close | Overview shows blockers, reasoned guarded close and linked later correction | bound |

`10-12` browser evidence: `.local/phase10-12-browser/browser-report.json` and desktop, mobile and 200% screenshots. The saved invalid batch had three distinct mapping failures; the second member was corrected with an identical replay key after a dropped response and the third was excluded with a reason.
