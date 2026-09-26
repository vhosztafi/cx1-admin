# Delivered report measures

The library exposes eleven distinct definitions grouped into six categories. `ReportCatalogue.cs` is the executable definition list; `contracts/generated/reporting.ts` is generated from OpenAPI. A run reads current saved sources in a serializable transaction after current identity/role checks. Dates are inclusive London calendar days. There is no persisted ReportRun row snapshot: paging and CSV explicitly rerun the applied filters and can reflect subsequent changes. The UI labels this behaviour. Recent runs retain filters, time and count only; favourites retain owner-defined names and filters.

| Definition | Cohort and measures | Access / basis |
|---|---|---|
| Underwriting performance | Current quotes created in the window; distinct bound/referred/declined outcomes, bound/all conversion, first-issue pinned rating premium. | Quote read / processed |
| Policy portfolio | One issued transaction in the effective or processed window, new business/cancellation counts and sealed obligation premium movement. | Policy read / effective or processed |
| Active policy portfolio | One active temporal policy at the range-end cutoff, known at run time; cancellation and expiry excluded. | Policy read / effective |
| Renewal retention | Expiring terms, delivered invitations, issued contiguous successors and explicit lapse; retention divides renewed expired terms by eligible expired terms. | Policy read / effective |
| Renewal invitations due | Future eligible term ends inside saved invitation rules; excludes delivered invitations, acceptance, lapse and issued successor. Saved rule identity is returned. | Policy read / effective |
| Financial performance | Sealed premium/commission, separate signed cash/debt movements and closing receivable balance. Opening sources contribute only to closing balance; no duplicate receipt/posting count. | Finance read / processed or effective |
| Earned premium | Persisted source-component/month slices, complete past London months, saved algorithm/hash validation and closed-period source cutoff. A month must fit within one configured accounting period. | Finance read / effective |
| Bordereau reconciliation | Latest provider/period batch, current version, included members; saved member premium minus sealed source premium. | Finance read / processed |
| Agency conversion league | Quote-created cohort by agency, conversion ranking, task-created service cohort and hours to recorded completion event. Subsequent task updates do not change duration. | Quote read / processed |
| Referral turnaround | Current-cycle referrals created in range; hours to their latest saved decision divided by decided referrals. | Underwriting read / processed |
| Compliance and exceptions | Recorded complaint/source-changed tasks, overdue support-review metadata and failed MID submissions. Not a complaint adjudication or unrestricted support-detail report. | Internal support read / processed |

Applicable agency/provider/product/underwriter filters are declared per definition. Finance performance accepts agency only because unallocated cash has no reliable product/provider attribution. Unsupported combinations and invalid/overlong ranges return 400; more than 10,000 sources returns 422. Empty ratios return null and display an em dash. Money remains invariant decimal text; averages/ratios round to four decimal places at calculation and two for display.

CSV synchronously exports all authorised matching sources with allowlisted fields and source/rule IDs. Text is quoted and formula-neutralised; numeric negative money is preserved. Agency identities cannot enter internal reporting. Finance does not gain underwriting risk access, and system administrators do not gain finance by virtue of administration. Internal support categories/instructions and provider payloads are never exported.

See [Phase 12 verification](../../.planning/phases/12-dashboards-search-and-reports/12-VERIFICATION.md) for actual isolated SQL, browser and contract evidence. The earlier Phase 1 conceptual snapshot model is superseded by this bounded current-data design and the approved lightweight delivery agreement.
